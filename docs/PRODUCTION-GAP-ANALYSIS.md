# SerialPortService 生产级缺口诊断报告

> 版本: 1.0.0 | 诊断日期: 2026-07-01 | 诊断范围: 全部源码 + 文档

---

## 目录

- [1. 诊断总览](#1-诊断总览)
- [2. 已修复项（本次）](#2-已修复项本次)
- [3. 未修复高危项](#3-未修复高危项)
- [4. 未修复中危项](#4-未修复中危项)
- [5. 未修复低危项](#5-未修复低危项)
- [6. 修复优先级路线图](#6-修复优先级路线图)

---

## 1. 诊断总览

| 级别 | 数量 | 含义 |
|:----:|:----:|------|
| 🔴 严重 | 6 → 2 | 上线前必须修复（本次修复4项） |
| 🟡 中度 | 6 → 3 | 强烈建议修复（本次修复3项） |
| 🟢 轻度 | 8 | 锦上添花，迭代优化 |

---

## 2. 已修复项（本次）

### ✅ #4 — 配置外部化

| 项目 | 修复前 | 修复后 |
|------|--------|--------|
| 配置方式 | 16 个配置硬编码在源码中 | `appsettings.json` + 环境变量覆盖 |
| 修改成本 | 需重新编译发布 | 修改配置文件后重启即可 |
| 多环境支持 | 无 | `appsettings.Production.json` / `appsettings.Development.json` |

**新增文件**: `SerialPortService/appsettings.json`

**配置项清单**:

| 配置路径 | 默认值 | 说明 |
|----------|--------|------|
| `Urls` | `http://0.0.0.0:9600` | 监听地址 |
| `WebSocket.KeepAliveSeconds` | `30` | WS 保活间隔 |
| `WebSocket.MaxConnections` | `100` | 最大 WS 连接数 |
| `WebSocket.ReceiveBufferSize` | `4096` | 接收缓冲区 |
| `SerialPort.DefaultBaudRate` | `9600` | 默认波特率 |
| `SerialPort.ReadTimeoutMs` | `500` | 读超时 |
| `SerialPort.WriteTimeoutMs` | `500` | 写超时 |
| `SerialPort.NewLine` | `\r\n` | 换行符 |
| `Serilog.MinimumLevel` | `Information` | 日志级别 |
| `Serilog.WriteTo[0].Args.path` | `logs/serialport-.log` | 日志路径 |

---

### ✅ #5 — 日志持久化

| 项目 | 修复前 | 修复后 |
|------|--------|--------|
| 日志框架 | `Microsoft.Extensions.Logging` (控制台) | Serilog |
| 输出目标 | 仅控制台 | 控制台 + 文件滚动 |
| 文件滚动 | 无 | 按天滚动，保留30天 |
| 结构化日志 | 无 | JSON 模板，关键字段可检索 |

**修复后行为**:
- 日志文件路径: `logs/serialport-20260701.log`
- 滚动策略: 每天一个新文件
- 保留策略: 自动清理 30 天前的日志
- 同时输出到控制台 + 文件

---

### ✅ #6 — ReceiveLoop CancellationToken

| 项目 | 修复前 | 修复后 |
|------|--------|--------|
| `ReceiveAsync` 取消 | `CancellationToken.None` | 传递 `context.RequestAborted` |
| 服务关闭时 | WebSocket 连接无法终止 | 优雅终止，断开所有 WS 连接 |
| 广播发送 | `CancellationToken.None` | 传入 `CancellationToken` |

**核心变更**:
```csharp
// 修复前
var result = await ws.ReceiveAsync(buffer, CancellationToken.None);

// 修复后
var result = await ws.ReceiveAsync(buffer, context.RequestAborted);
```

---

### ✅ #7 — 应用优雅关闭

| 项目 | 修复前 | 修复后 |
|------|--------|--------|
| 关闭钩子 | 无 | 注入 `IHostApplicationLifetime` |
| 串口资源 | 依赖 `Dispose` 被动释放 | `OnStopping` 回调主动关闭所有串口 |
| WS 连接 | 无处理 | 关闭所有活跃 WebSocket 连接 |
| 关闭超时 | 无 | 30 秒超时兜底 |

**核心流程**:
```
服务收到停止信号
  → ApplicationStopping 触发
    → 关闭所有串口 (Dispose)
    → 关闭所有 WebSocket 连接
    → 等待最多 30s
  → ApplicationStopped 触发
    → 清理 Serilog
  → 进程退出
```

---

### ✅ #8 — HEX 读取数据完整性

| 项目 | 修复前 | 修复后 |
|------|--------|--------|
| 读取方式 | `port.ReadExisting()` → `string` → `UTF8.GetBytes` → HEX | `port.BaseStream.ReadAsync` → `byte[]` → HEX |
| 二进制数据 | ❌ UTF8 编解码会损坏数据 | ✅ 原始字节保留 |
| API 响应 | `{"data":"...", "hex":"..."}` | `{"data":"base64...", "hex":"A5 5A 01 02", "byteCount":4}` |

**问题示例**:
```
原始字节: 0xA5 0x5A 0x01 0x02
修复前 UTF8 解码 → 乱码字符 → 再次 UTF8 编码 → 可能丢失/改变字节
修复后 直接 byte[] → 保留完整原始数据
```

---

### ✅ #11 — 串口并发写入保护

| 项目 | 修复前 | 修复后 |
|------|--------|--------|
| 并发控制 | 无，多客户端直接并发写 `BaseStream` | `ConcurrentDictionary<string, SemaphoreSlim>` 每串口一把锁 |
| 写入时序 | 可能交织乱序 | 严格 FIFO 序列化 |
| 等待策略 | 无 | `WaitAsync` 异步等待，不阻塞线程 |

**核心变更**:
```csharp
// SerialPortManager 新增字段
private readonly ConcurrentDictionary<string, SemaphoreSlim> _writeLocks = new();

// SendAsync 修改为
public async Task SendAsync(string portName, byte[] data) {
    var semaphore = _writeLocks.GetOrAdd(portName, _ => new SemaphoreSlim(1, 1));
    await semaphore.WaitAsync();
    try {
        await port.BaseStream.WriteAsync(data);
        await port.BaseStream.FlushAsync();
    } finally {
        semaphore.Release();
    }
}
```

---

### ✅ #10 — WebSocket 最大连接数限制

| 项目 | 修复前 | 修复后 |
|------|--------|--------|
| 连接限制 | `ConcurrentDictionary` 无上限 | 连接时检查 `Count >= MaxConnections` → 返回 503 |
| 配置方式 | 无 | `appsettings.json` → `WebSocket:MaxConnections` (默认 100) |

**核心变更**:
```csharp
// Program.cs WebSocket 路由
if (wsManager.Count >= wsOptions.MaxConnections) {
    context.Response.StatusCode = 503;
    await context.Response.WriteAsync($"已达到最大连接数限制 ({wsOptions.MaxConnections})");
    return;
}
```

---

## 3. 未修复高危项

### 🔴 #1 — 无身份认证

**现状**: CORS 全开 + API 零鉴权

**风险**: 任何能访问 9600 端口的人都能操控串口设备

**建议方案**:
- 短期: 添加 `X-API-Key` Header 校验（`appsettings.json` 中配置预共享密钥）
- 长期: JWT Bearer Token + 刷新机制

**预计工时**: 2-3h

---

### 🔴 #2 — 无速率限制

**现状**: 无任何请求频率控制

**风险**: 恶意或失控客户端可瞬间打爆服务

**建议方案**:
- 使用 `AspNetCoreRateLimit` 中间件
- 配置: 每 IP 每秒最多 100 请求，串口写入每秒最多 10 次

**预计工时**: 1-2h

---

## 4. 未修复中危项

### 🟡 #3 — 串口读取无推送机制

**现状**: 客户端必须轮询 `GET /api/serial/read`

**问题**: 高频轮询 CPU 空转 + 数据可能丢失

**建议方案**:
- 监听 `SerialPort.DataReceived` 事件
- 收到数据后通过 WebSocket 推送给订阅该串口的客户端
- WebSocket 消息类型: `{ type: "serial_data", portName: "COM3", hex: "...", timestamp: "..." }`

**预计工时**: 3-4h

---

### 🟡 #9 — 串口热插拔无感知

**现状**: 仅在请求 `/ports` 时扫描一次

**建议方案**:
- 定时轮询（每 2 秒）检测串口变化
- 变化时通过 WebSocket 广播 `{ type: "ports_changed", ports: [...] }`

**预计工时**: 1-2h

---

### 🟡 #12 — 无请求体大小限制

**现状**: POST body 无上限

**建议方案**:
- 中间件限制 `MaxRequestBodySize = 64KB`
- 串口发送接口单独限制 `data` 字段最大 4096 字节

**预计工时**: 0.5h

---

## 5. 未修复低危项

| # | 项目 | 说明 | 预计工时 |
|---|------|------|:---:|
| 13 | API 版本号 | 路由加 `/api/v1/` 前缀 | 1h |
| 14 | Swagger / OpenAPI | 自动生成交互式 API 文档 | 2h |
| 15 | Prometheus Metrics | 暴露 QPS/延迟/错误率 | 2h |
| 16 | 健康检查详情 | health 增加串口状态、内存等 | 1h |
| 17 | WS Binary 帧支持 | 直接透传串口原始字节 | 2h |
| 18 | Docker 支持 | Windows 容器部署 | 2h |
| 19 | 单元测试 | xUnit + 覆盖核心逻辑 | 4h |
| 20 | CI/CD | GitHub Actions 自动构建发布 | 2h |

---

## 6. 修复优先级路线图

```
第一阶段（本次已完成）
├── ✅ #4  配置外部化
├── ✅ #5  日志持久化
├── ✅ #6  CancellationToken
├── ✅ #7  优雅关闭
├── ✅ #8  HEX 数据完整性
├── ✅ #10 最大连接数限制
└── ✅ #11 并发写入保护

第二阶段（上线前必须）  ← 当前
├── 🔴 #1  身份认证（API Key）
├── 🔴 #2  速率限制
└── 🟡 #12 请求体大小限制

第三阶段（上线后尽快）
├── 🟡 #3  串口数据推送（轮询→事件驱动）
├── 🟡 #9  热插拔感知
└── 🟢 #16 健康检查增强

第四阶段（迭代优化）
├── 🟢 #13 API 版本号
├── 🟢 #14 Swagger
├── 🟢 #15 Metrics
├── 🟢 #17 Binary 帧
├── 🟢 #18 Docker
├── 🟢 #19 单元测试
└── 🟢 #20 CI/CD
```

---

## 附录：变更文件清单（本次修复）

```
SerialPortService/
├── appsettings.json              [新增] 外部配置文件
├── SerialPortService.csproj      [修改] 添加 Serilog NuGet 引用
├── Program.cs                    [修改] 配置加载 + Serilog + 优雅关闭 + CancellationToken
├── SerialPortManager.cs          [修改] 并发写入锁 + 优雅关闭 + byte[] 读取
└── WebSocketConnectionManager.cs [修改] CancellationToken + 批量关闭
```

---

> **下次建议**: 优先修复 #1 身份认证和 #2 速率限制，这两项是生产环境安全底线。
