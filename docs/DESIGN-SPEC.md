# SerialPortService 设计规格文档

> 版本: 1.0.0 | 作者: MesTeam | 日期: 2026-07-01

---

## 目录

- [1. 文档概述](#1-文档概述)
- [2. 产品概述](#2-产品概述)
- [3. 系统架构](#3-系统架构)
- [4. 模块设计](#4-模块设计)
- [5. 接口规格](#5-接口规格)
- [6. 数据模型](#6-数据模型)
- [7. 部署与运维](#7-部署与运维)
- [8. 配置项](#8-配置项)
- [9. 安全设计](#9-安全设计)
- [10. 非功能需求](#10-非功能需求)

---

## 1. 文档概述

### 1.1 文档目的

本文档描述 SerialPortService（串口通信服务）的完整系统设计，包括架构、模块划分、接口定义、数据模型和部署方案，作为开发、测试、运维的权威参考。

### 1.2 适用范围

- 开发人员：理解系统架构和模块职责
- 集成人员：了解 API 接口和通信协议
- 运维人员：掌握部署配置和运维命令

### 1.3 术语定义

| 术语 | 说明 |
|------|------|
| 自宿主 (Self-Hosted) | 不依赖 IIS / Nginx 等外部 Web 服务器，由应用内嵌 Kestrel 服务器直接监听端口 |
| WebSocket | 基于 TCP 的全双工通信协议，用于实时双向数据传输 |
| 串口 (Serial Port) | 计算机上用于串行通信的物理接口，Windows 下命名为 COM1/COM2/... |
| 广播 (Broadcast) | 向所有已连接的 WebSocket 客户端同时发送消息 |

---

## 2. 产品概述

### 2.1 产品定位

SerialPortService 是一款运行在 Windows 平台的**串口通信中间件服务**，将本地物理串口的能力通过 HTTP RESTful API 和 WebSocket 暴露给局域网内的其他应用，使不具备串口访问能力的远程客户端（如 Web 前端、移动端、跨平台桌面应用）也能操控串口设备。

### 2.2 核心功能

```
┌─────────────────────────────────────────────────────┐
│                   SerialPortService                  │
│                                                     │
│  ┌──────────┐  ┌──────────────┐  ┌───────────────┐ │
│  │ 串口管理  │  │  WebSocket   │  │  RESTful API  │ │
│  │          │  │  实时通信     │  │  请求响应      │ │
│  │ 打开/关闭 │  │  双向/广播   │  │  JSON 格式    │ │
│  │ 读写数据  │  │              │  │              │ │
│  └────┬─────┘  └──────┬───────┘  └──────┬────────┘ │
│       │               │                  │          │
│  ┌────┴───────────────┴──────────────────┴────────┐ │
│  │          HTTP 服务 (Kestrel :9600)              │ │
│  └──────────────────────┬─────────────────────────┘ │
│                         │                            │
│  ┌──────────────────────┴─────────────────────────┐ │
│  │         Windows 服务宿主 (开机自启)              │ │
│  └────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────┘
```

### 2.3 技术选型

| 层次 | 技术 | 选型理由 |
|------|------|---------|
| 运行时 | .NET 6.0 | 兼容 Win7 SP1+，自包含部署无需安装 Runtime |
| Web 框架 | ASP.NET Core Minimal API | 轻量级、启动快、代码简洁 |
| Web 服务器 | Kestrel (内嵌) | 自宿主，无需外部依赖 |
| 串口通信 | System.IO.Ports 6.0 | .NET 官方串口库 |
| WebSocket | ASP.NET Core WebSocket 中间件 | 原生支持，无需 SignalR 等额外封装 |
| 服务宿主 | Microsoft.Extensions.Hosting.WindowsServices | 原生 Windows 服务集成 |
| 日志 | Serilog | 结构化日志，控制台 + 文件滚动（保留30天） |
| 配置 | appsettings.json + 环境变量 | 配置外部化，支持多环境 |
| 并发管理 | ConcurrentDictionary + SemaphoreSlim | 线程安全，串口写入 FIFO 序列化 |
| 分发方式 | 单文件 EXE (SelfContained) | 自包含运行时，复制即运行，x64+x86 双架构 |

---

## 3. 系统架构

### 3.1 分层架构

```
┌────────────────────────────────────────────────────┐
│                    表示层                            │
│  wwwroot/index.html   wwwroot/serial-debug.html    │
│  (调试面板)            (串口调试工具)               │
├────────────────────────────────────────────────────┤
│                   通信层                             │
│  ┌──────────────────┐  ┌─────────────────────────┐ │
│  │  RESTful API     │  │  WebSocket (/ws)         │ │
│  │  /api/*          │  │  - 双向消息转发          │ │
│  │  - 串口 CRUD     │  │  - 多客户端广播          │ │
│  │  - 健康检查      │  │  - 8位 clientId 标识     │ │
│  └────────┬─────────┘  └────────────┬────────────┘ │
├───────────┴──────────────────────────┴────────────┤
│                   业务逻辑层                         │
│  ┌────────────────────┐  ┌───────────────────────┐ │
│  │ SerialPortManager  │  │ WebSocketConnection   │ │
│  │ - 串口生命周期管理  │  │ Manager               │ │
│  │ - 异步读写操作     │  │ - 连接生命周期         │ │
│  │ - 线程安全        │  │ - 广播/单播            │ │
│  └────────────────────┘  └───────────────────────┘ │
├────────────────────────────────────────────────────┤
│                   基础设施层                         │
│  ┌────────────────────────────────────────────────┐ │
│  │  WindowsServiceInstaller                       │ │
│  │  - 服务安装/卸载 (sc.exe)                       │ │
│  │  - 启停/状态查询 (ServiceController)            │ │
│  │  - 失败自动重启策略                             │ │
│  └────────────────────────────────────────────────┘ │
│  ┌────────────────────────────────────────────────┐ │
│  │  Kestrel Web Server (HTTP :9600)               │ │
│  └────────────────────────────────────────────────┘ │
└────────────────────────────────────────────────────┘
```

### 3.2 启动流程

```
启动 SerialPortService.exe
         │
         ├── 有命令行参数? ──是──▶ 参数为 install/uninstall/start/stop/status?
         │                              │
         │                             是          否
         │                              │           │
         │                              ▼           ▼
         │                     WindowsService    直接启动
         │                     Installer          Web 服务
         │                     .HandleCommand()
         │                         │
         │                         ▼
         │                      执行完毕退出
         │
         否
         │
         ▼
   WebApplication.CreateBuilder()
         │
         ├── 注册 Singleton: SerialPortManager
         ├── 注册 Singleton: WebSocketConnectionManager
         ├── 配置 CORS (AllowAny)
         ├── 配置 Windows Service 宿主
         ├── 配置监听地址 http://0.0.0.0:9600
         │
         ▼
   app.Build()
         │
         ├── UseCors()
         ├── UseWebSockets()
         ├── UseDefaultFiles()
         ├── UseStaticFiles()
         ├── RegisterRoutes()
         │
         ▼
   app.RunAsync() ─── 阻塞等待，处理请求
```

### 3.3 请求处理流程

```
客户端请求
    │
    ├── HTTP GET/POST /api/*  ──▶ Minimal API 路由匹配
    │                                  │
    │                                  ├── 参数绑定 (模型验证)
    │                                  ├── 调用 Manager 方法
    │                                  └── 返回 JSON 响应
    │
    ├── HTTP GET /ws  ──▶ WebSocket 升级
    │                          │
    │                          ├── 生成 clientId (8位hex)
    │                          ├── 加入连接管理器
    │                          ├── ReceiveLoop (循环接收)
    │                          │     ├── 收到文本 → echo 广播给所有客户端
    │                          │     └── 收到 Close → 退出循环
    │                          └── 移除连接
    │
    └── HTTP GET /*  ──▶ 静态文件中间件
                              │
                              ├── 匹配 wwwroot/index.html (默认文档)
                              └── 匹配 wwwroot/*.html
```

---

## 4. 模块设计

### 4.1 Program — 主程序入口

**文件**: `SerialPortService/Program.cs` (420 行)

**职责**:
- 命令行参数解析与分发
- WebApplication 构建与配置（含 appsettings.json 加载）
- Serilog 日志配置
- 路由注册（WebSocket + 9 个 REST API 端点）
- 优雅关闭钩子注册
- 请求模型定义

**关键设计决策**:

| 决策 | 说明 |
|------|------|
| Minimal API | 不使用 Controller 模式，采用 `MapGet`/`MapPost` 内联路由，减少代码量 |
| 独立路由 | 串口 API 使用完整路径 `/api/serial/*`，兼容 .NET 6.0 |
| 请求模型 | 使用 C# `record` 类型，不可变、自带值语义，适合 API 参数绑定 |
| 配置外部化 | `appsettings.json` 管理配置，环境变量 `SERIALPORT_` 前缀覆盖 |

### 4.2 SerialPortManager — 串口管理器

**文件**: `SerialPortService/SerialPortManager.cs` (95 行)

**职责**:
- 管理串口的完整生命周期（打开/关闭/读写）
- 维护串口实例字典，确保同一串口不被重复打开

**数据结构**:
```
ConcurrentDictionary<string, SerialPort>
    key: 串口名称 (如 "COM3")
    value: System.IO.Ports.SerialPort 实例
```

**接口定义**:

| 方法 | 签名 | 说明 |
|------|------|------|
| `Open` | `(string portName, int baudRate, Parity parity, int dataBits, StopBits stopBits) → void` | 打开串口，重复打开时仅记录警告 |
| `Close` | `(string portName) → void` | 关闭并释放串口资源 |
| `IsPortOpen` | `(string portName) → bool` | 检查串口是否已打开 |
| `GetOpenedPorts` | `() → IEnumerable<SerialPort>` | 获取所有已打开串口实例 |
| `SendAsync` | `(string portName, byte[] data) → Task` | 异步写入数据到串口（SemaphoreSlim 并发保护） |
| `ReadAllBytesAsync` | `(string portName) → Task<byte[]>` | 异步读取缓冲区原始字节 |
| `Dispose` | `() → void` | 释放所有串口资源 |

**串口配置默认值**:
```
BaudRate: 9600
Parity:   None
DataBits: 8
StopBits: One
ReadTimeout: 500ms
WriteTimeout: 500ms
NewLine: "\r\n"
```

### 4.3 WebSocketConnectionManager — WebSocket 连接管理器

**文件**: `SerialPortService/WebSocketConnectionManager.cs` (63 行)

**职责**:
- 管理所有 WebSocket 客户端的连接生命周期
- 提供广播和单播消息能力

**数据结构**:
```
ConcurrentDictionary<string, WebSocket>
    key: 客户端 ID (8位 hex 字符串)
    value: System.Net.WebSockets.WebSocket 实例
```

**接口定义**:

| 方法 | 签名 | 说明 |
|------|------|------|
| `Add` | `(string clientId, WebSocket socket) → void` | 注册新连接 |
| `Remove` | `(string clientId) → void` | 移除连接 |
| `BroadcastAsync` | `(string message) → Task` | 向所有在线客户端广播消息 |
| `SendToAsync` | `(string clientId, string message) → Task` | 向指定客户端发送消息 |
| `Count` | `→ int` (属性) | 当前在线客户端数 |

**广播机制**:
- 遍历所有 WebSocket 连接
- 仅向 `State == Open` 的连接发送
- 使用 `SendSafe` 包装，单个客户端失败不影响其他客户端
- 使用 `Task.WhenAll` 并行发送

### 4.4 WindowsServiceInstaller — Windows 服务安装器

**文件**: `SerialPortService/WindowsServiceInstaller.cs` (173 行)

**职责**:
- 将应用程序注册为 Windows 系统服务
- 提供服务启停和状态查询

**支持命令**:

| 命令 | 说明 | 权限要求 |
|------|------|---------|
| `install` | 安装服务（开机自启） | 管理员 |
| `uninstall` | 卸载服务（先自动停止） | 管理员 |
| `start` | 启动服务 | 管理员 |
| `stop` | 停止服务 | 管理员 |
| `status` | 查看服务状态 | 无需 |

**服务配置**:
```
服务名称:     SerialPortService
显示名称:     串口通信服务
描述:         提供 WebSocket + RESTful API 的串口通信服务，监听端口 9600
启动类型:     自动 (start= auto)
失败恢复:     重启/5秒 → 重启/10秒 → 重启/30秒，24小时后重置计数
```

---

## 5. 接口规格

### 5.1 通信协议

| 协议 | 端点 | 数据格式 | 编码 |
|------|------|---------|------|
| HTTP/1.1 | `http://{host}:9600/api/*` | JSON | UTF-8 |
| WebSocket | `ws://{host}:9600/ws` | JSON 文本帧 | UTF-8 |
| 静态文件 | `http://{host}:9600/*` | HTML/CSS/JS | UTF-8 |

### 5.2 RESTful API 端点

#### 5.2.1 通用规范

**请求头**: `Content-Type: application/json` (POST 请求)
**成功响应**: HTTP 200
**失败响应**: HTTP 400
**CORS**: 允许所有来源

**通用成功响应**:
```json
{
  "success": true,
  "message": "操作描述"
}
```

**通用失败响应**:
```json
{
  "success": false,
  "message": "错误描述"
}
```

#### 5.2.2 端点清单

| # | 方法 | 路径 | 请求体 | 说明 |
|---|------|------|--------|------|
| 1 | `GET` | `/api/health` | — | 健康检查 |
| 2 | `GET` | `/api/serial/ports` | — | 获取可用串口列表 |
| 3 | `GET` | `/api/serial/opened` | — | 获取已打开串口列表 |
| 4 | `POST` | `/api/serial/open` | `OpenPortRequest` | 打开串口 |
| 5 | `POST` | `/api/serial/close` | `ClosePortRequest` | 关闭串口 |
| 6 | `POST` | `/api/serial/send` | `SendDataRequest` | 发送数据到串口 |
| 7 | `GET` | `/api/serial/read?portName=` | — | 读取串口缓冲区 |
| 8 | `GET` | `/api/clients` | — | 获取在线客户端数 |
| 9 | `POST` | `/api/broadcast` | `BroadcastRequest` | 广播消息 |

#### 5.2.3 端点详细定义

**1. GET /api/health**

响应:
```json
{
  "status": "running",
  "timestamp": "2026-06-30T23:09:14.869Z",
  "version": "1.0.0",
  "uptime": "1.03:45:12",
  "wsClients": 3,
  "openedPorts": ["COM3", "COM5"]
}
```

| 字段 | 类型 | 说明 |
|------|------|------|
| status | string | 服务运行状态，正常为 `"running"` |
| timestamp | string | UTC 时间戳 (ISO 8601) |
| version | string | 服务版本号 |
| uptime | string | 进程运行时长，格式 `d.hh:mm:ss` |
| wsClients | int | 当前 WebSocket 在线客户端数 |
| openedPorts | string[] | 当前已打开的串口名称列表 |

**2. GET /api/serial/ports**

响应:
```json
[
  {
    "name": "COM1",
    "description": "通信端口",
    "isOpen": false
  }
]
```

**3. GET /api/serial/opened**

响应:
```json
[
  {
    "portName": "COM3",
    "baudRate": 9600,
    "isOpen": true
  }
]
```

**4. POST /api/serial/open**

请求体:
```json
{
  "portName": "COM3",
  "baudRate": 115200,
  "parity": 0,
  "dataBits": 8,
  "stopBits": 1
}
```

| 字段 | 类型 | 必填 | 默认值 | 说明 |
|------|------|:---:|--------|------|
| portName | string | ✅ | — | 串口名称 |
| baudRate | int | ❌ | 9600 | 波特率 |
| parity | int | ❌ | 0 | 校验位: 0=None 1=Odd 2=Even 3=Mark 4=Space |
| dataBits | int | ❌ | 8 | 数据位: 5/6/7/8 |
| stopBits | int | ❌ | 1 | 停止位: 0=None 1=One 2=Two 3=OnePointFive |

**5. POST /api/serial/close**

请求体:
```json
{ "portName": "COM3" }
```

**6. POST /api/serial/send**

请求体 (ASCII):
```json
{
  "portName": "COM3",
  "data": "Hello",
  "hex": false
}
```

请求体 (HEX):
```json
{
  "portName": "COM3",
  "data": "A55A0102",
  "hex": true
}
```

**7. GET /api/serial/read?portName=COM3**

响应（有数据）:
```json
{
  "success": true,
  "data": "SGVsbG8gZnJvbSBkZXZpY2U=",
  "hex": "48 65 6C 6C 6F 20 66 72 6F 6D 20 64 65 76 69 63 65",
  "byteCount": 15
}
```

> **注意**：`data` 字段为 **Base64 编码** 的原始字节数据，需客户端自行解码。

响应（缓冲区空）:
```json
{
  "success": true,
  "data": "",
  "hex": "",
  "byteCount": 0
}
```

**8. GET /api/clients**

响应:
```json
{ "count": 3 }
```

**9. POST /api/broadcast**

请求体:
```json
{ "message": "通知内容" }
```

响应:
```json
{ "success": true, "clientCount": 3 }
```

### 5.3 WebSocket 协议

#### 5.3.1 连接

```
客户端 → ws://localhost:9600/ws → 服务端
                                  ↓
                              分配 clientId (8位hex)
                                  ↓
                              加入连接管理器
```

#### 5.3.2 消息格式

**客户端 → 服务端**: 任意 UTF-8 文本

**服务端 → 客户端 (回显)**:
```json
{
  "type": "echo",
  "clientId": "a1b2c3d4",
  "message": "原始消息",
  "timestamp": "2026-06-30T23:09:14.869Z"
}
```

**服务端 → 客户端 (广播)**:
```json
{
  "type": "broadcast",
  "message": "广播内容",
  "timestamp": "2026-06-30T23:09:14.869Z"
}
```

#### 5.3.3 消息处理流程

```
客户端A 发送 "Hello"
    │
    ▼
服务端 ReceiveLoop 接收
    │
    ▼
构造 echo 消息 { type:"echo", clientId:"xxx", message:"Hello", timestamp:... }
    │
    ▼
BroadcastAsync → 发送给所有在线客户端 (包括客户端A)
    │
    ├──▶ 客户端A 收到自己的回显
    ├──▶ 客户端B 收到客户端A的消息
    └──▶ 客户端C 收到客户端A的消息
```

#### 5.3.4 心跳机制

- 服务端通过 `WsServiceOptions.KeepAliveSeconds` 配置保活间隔（默认 30s）
- 客户端应主动发送心跳（推荐间隔 ≤ 25s）
- 心跳消息内容任意（如 `"PING"`），会被当作普通 echo 消息处理

---

## 6. 数据模型

### 6.1 请求模型 (C# Record)

```csharp
// 打开串口请求
public record OpenPortRequest(
    string PortName,
    int BaudRate = 9600,
    Parity Parity = Parity.None,
    int DataBits = 8,
    StopBits StopBits = StopBits.One
);

// 关闭串口请求
public record ClosePortRequest(string PortName);

// 发送数据请求
public record SendDataRequest(
    string PortName,
    string Data,
    bool Hex = false
);

// 广播请求
public record BroadcastRequest(string Message);
```

### 6.2 内部状态

**串口实例表** (`SerialPortManager._ports`):
```
ConcurrentDictionary<string, SerialPort>
┌──────────┬────────────────────────────────┐
│ Key      │ Value                          │
├──────────┼────────────────────────────────┤
│ "COM3"   │ SerialPort { PortName="COM3", │
│          │   BaudRate=115200, IsOpen=true }│
├──────────┼────────────────────────────────┤
│ "COM5"   │ SerialPort { PortName="COM5", │
│          │   BaudRate=9600, IsOpen=true }  │
└──────────┴────────────────────────────────┘
```

**WebSocket 连接表** (`WebSocketConnectionManager._sockets`):
```
ConcurrentDictionary<string, WebSocket>
┌────────────┬──────────────────┐
│ Key        │ Value            │
├────────────┼──────────────────┤
│ "a1b2c3d4" │ WebSocket {      │
│            │   State=Open     │
│            │ }                │
├────────────┼──────────────────┤
│ "e5f6g7h8" │ WebSocket {      │
│            │   State=Open     │
│            │ }                │
└────────────┴──────────────────┘
```

---

## 7. 部署与运维

### 7.1 运行环境要求

| 项目 | 要求 |
|------|------|
| 操作系统 | Windows 7 SP1+ / Windows 8+ / Windows 10+ / Windows 11 / Windows Server 2012 R2+ |
| 运行时 | 无需安装（自包含部署，内置 .NET 6.0 Runtime） |
| 权限 | 前台运行：普通用户；服务模式：管理员 |
| 端口 | 9600 (HTTP，可通过 appsettings.json 修改) |
| 磁盘 | < 100 MB |

### 7.2 构建与发布

```cmd
# 构建（双架构：x64 + x86）
build.cmd

# 等价于
cd SerialPortService
dotnet restore
dotnet publish -c Release -r win7-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:PublishReadyToRun=true -o ..\publish\x64
dotnet publish -c Release -r win-x86 --self-contained true ^
  -p:PublishSingleFile=true -p:PublishReadyToRun=true -o ..\publish\x86
```

**发布产物**:
- `publish/x64/SerialPortService.exe` (~89 MB，Win7 x64+)
- `publish/x86/SerialPortService.exe` (~83 MB，Win7 x86+)

### 7.3 运行模式

#### 前台运行（调试/测试）

```cmd
SerialPortService.exe
```

- 控制台窗口显示运行日志
- 关闭窗口即停止服务

#### Windows 服务运行（生产）

```cmd
# 安装（需管理员）
SerialPortService.exe install

# 启动
SerialPortService.exe start

# 查看状态
SerialPortService.exe status

# 停止
SerialPortService.exe stop

# 卸载
SerialPortService.exe uninstall
```

**服务失败恢复策略**:
```
第1次崩溃 → 5秒后重启
第2次崩溃 → 10秒后重启
第3次崩溃 → 30秒后重启
24小时内无崩溃 → 重置失败计数
```

### 7.4 健康监控

| 检查项 | 方式 | 预期结果 |
|--------|------|---------|
| 服务存活 | `GET /api/health` | HTTP 200, `{"status":"running"}` |
| 端口监听 | `netstat -ano \| findstr 9600` | LISTENING 状态 |
| 进程存在 | `tasklist \| findstr SerialPortService` | 进程存在 |

---

## 8. 配置项

配置通过 `appsettings.json` 外部化管理，支持环境变量 `SERIALPORT_` 前缀覆盖。默认值如下：

| 配置路径 | 默认值 | 说明 |
|----------|--------|------|
| `Urls` | `http://0.0.0.0:9600` | 监听地址/端口 |
| `WebSocket:KeepAliveSeconds` | `30` | WS 保活间隔 |
| `WebSocket:MaxConnections` | `100` | 最大 WS 连接数 |
| `WebSocket:ReceiveBufferSize` | `4096` | 接收缓冲区（字节） |
| `SerialPort:DefaultBaudRate` | `9600` | 默认波特率 |
| `SerialPort:ReadTimeoutMs` | `500` | 读超时（毫秒） |
| `SerialPort:WriteTimeoutMs` | `500` | 写超时（毫秒） |
| `SerialPort:NewLine` | `\r\n` | 换行符 |
| `Serilog:MinimumLevel` | `Information` | 日志级别 |
| `Serilog:WriteTo[0].Args.path` | `logs/serialport-.log` | 日志文件路径 |

以下为代码中硬编码的配置项：

| 配置项 | 默认值 | 代码位置 |
|--------|--------|---------|
| WebSocket 路径 | `/ws` | `Program.cs` |
| API 路径前缀 | `/api` | `Program.cs` 路由注册 |
| CORS 策略 | AllowAny | `Program.cs` |
| 服务名称 | SerialPortService | `WindowsServiceInstaller.cs` |
| 服务显示名 | 串口通信服务 | `WindowsServiceInstaller.cs` |
| 服务失败恢复 | 5s/10s/30s 三级 | `WindowsServiceInstaller.cs` |
| 客户端 ID 长度 | 8 位 hex | `Program.cs` |

---

## 9. 安全设计

### 9.1 当前安全措施

| 措施 | 说明 |
|------|------|
| CORS | 完全开放（`AllowAnyOrigin`），适用于内网环境 |
| Windows 服务 | 以 SYSTEM 账户运行，与桌面隔离 |
| 管理员权限 | 服务安装/卸载需要管理员权限 |

### 9.2 安全风险与建议

| 风险 | 级别 | 建议 |
|------|:---:|------|
| 无身份认证 | 🔴 高 | 生产环境建议添加 API Key 或 JWT 认证 |
| HTTP 明文传输 | 🟡 中 | 内网环境可接受，公网部署需加 HTTPS |
| 无速率限制 | 🟡 中 | 建议添加请求频率限制防止滥用 |
| CORS 全开放 | 🟢 低 | 内网环境可接受 |
| 串口无权限控制 | 🟡 中 | 任何能访问 API 的客户端都能操作串口 |
| 无日志脱敏 | 🟢 低 | 发送的数据内容会记录到日志 |

### 9.3 推荐部署拓扑

```
┌──────────────────────────────────────────────┐
│              内网 (192.168.x.x)               │
│                                              │
│  ┌──────────┐       ┌──────────────────────┐ │
│  │ 客户端 A  │──HTTP──▶                      │ │
│  └──────────┘       │                      │ │
│                     │  SerialPortService   │ │
│  ┌──────────┐       │  :9600               │ │
│  │ 客户端 B  │──WS───▶                      │ │
│  └──────────┘       └──────────┬───────────┘ │
│                                │             │
│                         物理串口线            │
│                                │             │
│                        ┌───────┴──────┐      │
│                        │  串口设备     │      │
│                        └──────────────┘      │
└──────────────────────────────────────────────┘
```

---

## 10. 非功能需求

### 10.1 性能指标

| 指标 | 目标值 | 说明 |
|------|--------|------|
| 启动时间 | < 3 秒 | 从进程启动到端口监听就绪 |
| API 响应时间 | < 50ms | 不含串口硬件延迟 |
| WebSocket 并发连接 | ≥ 100 | 基于 ConcurrentDictionary 设计 |
| 内存占用 | < 50 MB | 空闲状态 |
| 串口数据吞吐 | 受硬件波特率限制 | 115200bps ≈ 11.5 KB/s |

### 10.2 可靠性

| 指标 | 目标值 |
|------|--------|
| 服务可用性 | 99.9%（Windows 服务自恢复） |
| 崩溃恢复时间 | ≤ 30 秒（三级自动重启） |
| 数据一致性 | 串口操作失败返回明确错误，不影响服务 |

### 10.3 可维护性

| 方面 | 措施 |
|------|------|
| 日志 | 通过 `ILogger` 输出结构化日志，记录串口操作和连接事件 |
| 调试 | 内置 Web 调试面板，可在线测试所有 API |
| 部署 | 单文件 EXE，复制即部署 |
| 监控 | `/api/health` 端点支持健康检查集成 |

### 10.4 兼容性

| 项目 | 兼容范围 |
|------|---------|
| 操作系统 | Windows 7 SP1+ / Windows 8+ / Windows 10+ / Windows 11 / Windows Server 2012 R2+ |
| 架构 | x64 + x86 双版本 |
| .NET 运行时 | .NET 6.0（自包含部署，无需安装） |
| 客户端 | 任何支持 HTTP/WebSocket 的平台（浏览器/Node.js/Python/Java...） |
| 串口硬件 | Windows 识别的所有 COM 端口 |

---

## 附录

### A. 项目文件清单

```
seriport-cs/
├── build.cmd                          # 构建脚本（双架构：x64+x86）
├── README.md                          # 项目说明
├── CHANGELOG.md                       # 版本变更日志
├── SerialPortService/                 # 服务源码
│   ├── Program.cs                     # 主入口 + 路由 (420行)
│   ├── SerialPortManager.cs           # 串口管理（并发写入保护）
│   ├── WebSocketConnectionManager.cs  # WS 连接管理
│   ├── WindowsServiceInstaller.cs     # 服务安装器 (173行)
│   ├── appsettings.json               # 外部配置文件
│   ├── SerialPortService.csproj       # 项目配置 (.NET 6.0)
│   └── wwwroot/
│       ├── index.html                 # 调试面板
│       └── serial-debug.html          # 串口调试工具
├── publish/                           # 发布产物
│   ├── x64/                           # Win7 x64 版本
│   └── x86/                           # Win7 x86 版本
└── docs/                              # 设计文档
    ├── DESIGN-SPEC.md                 # 本文档
    ├── API-REFERENCE.md               # API 访问说明
    ├── ANTI-THUNDERING-HERD.md        # 防雪崩重连原理
    ├── PRODUCTION-GAP-ANALYSIS.md     # 生产级缺口诊断
    ├── 系统兼容性说明.md               # 系统兼容性矩阵
    └── serialport-ws-client.js        # WebSocket 客户端
```

### B. 依赖项

| NuGet 包 | 版本 | 用途 |
|----------|------|------|
| System.IO.Ports | 6.0.0 | 串口通信 API |
| System.ServiceProcess.ServiceController | 6.0.0 | Windows 服务控制 |
| Microsoft.Extensions.Hosting.WindowsServices | 6.0.0 | Windows 服务宿主集成 |
| System.Management | 6.0.0 | WMI 查询（获取串口描述） |
| Serilog.AspNetCore | 6.1.0 | 结构化日志框架 |
| Serilog.Sinks.Console | 4.1.0 | 控制台日志输出 |
| Serilog.Sinks.File | 5.0.0 | 文件滚动日志 |

### C. 变更历史

| 版本 | 日期 | 变更内容 | 作者 |
|------|------|---------|------|
| 1.0.0 | 2026-07-01 | 初始版本：串口管理 REST API、WebSocket 实时通信、Windows 服务宿主、健康检查端点、双架构自包含部署 | MesTeam |
