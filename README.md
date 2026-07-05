# SerialPortService - 串口通信服务

基于 C# / ASP.NET Core 的自宿主串口通信服务，提供 **WebSocket 双向通信** + **RESTful API** + **调试网页面板**。

## 功能特性

| 功能 | 说明 |
|------|------|
| 🔌 串口管理 | 打开/关闭串口、列出可用串口、设置波特率等参数 |
| 💬 WebSocket | 实时双向通信，支持多客户端广播 |
| 🌐 RESTful API | 完整的串口操作 API，支持 JSON 格式 |
| 🖥️ 调试网页 | 内置调试面板，可视化操作串口和测试 API |
| 🏃 自宿主 | 无需 IIS，单个 EXE 直接运行 |
| ⚙️ Windows 服务 | 支持安装为 Windows 服务，开机自启动 |
| 📦 单文件分发 | 发布为单个 EXE，自包含运行时，复制即运行 |
| 🖥️ 多架构支持 | x64 + x86 双版本，兼容 Win7 SP1 ~ Win11 |

## 快速开始

### 1. 构建

```cmd
build.cmd
```

### 2. 直接运行

```cmd
cd publish
SerialPortService.exe
```

服务启动后：
- 调试网页: http://localhost:9600
- 串口调试: http://localhost:9600/serial-debug.html
- API 地址: http://localhost:9600/api

### 3. 安装为 Windows 服务

以**管理员身份**运行：

```cmd
SerialPortService.exe install    # 安装服务（开机自启）
SerialPortService.exe start      # 启动服务
SerialPortService.exe status     # 查看状态
SerialPortService.exe stop       # 停止服务
SerialPortService.exe uninstall  # 卸载服务
```

## RESTful API 文档

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/api/health` | 健康检查（含 uptime、WS 连接数、已打开串口） |
| GET | `/api/serial/ports` | 获取所有可用串口 |
| GET | `/api/serial/opened` | 获取已打开串口列表 |
| POST | `/api/serial/open` | 打开串口 |
| POST | `/api/serial/close` | 关闭串口 |
| POST | `/api/serial/send` | 发送数据到串口（支持 ASCII/HEX） |
| GET | `/api/serial/read` | 读取串口缓冲区数据（返回 Base64 + HEX） |
| GET | `/api/clients` | 获取 WebSocket 客户端数 |
| POST | `/api/broadcast` | 广播消息到所有 WebSocket 客户端 |
| WS | `/ws` | WebSocket 连接端点 |

### API 示例

```bash
# 健康检查
curl http://localhost:9600/api/health

# 获取串口列表
curl http://localhost:9600/api/serial/ports

# 打开串口 COM3 (9600 波特率)
curl -X POST http://localhost:9600/api/serial/open \
  -H "Content-Type: application/json" \
  -d '{"portName":"COM3","baudRate":9600}'

# 发送数据
curl -X POST http://localhost:9600/api/serial/send \
  -H "Content-Type: application/json" \
  -d '{"portName":"COM3","data":"Hello"}'

# 发送 HEX 数据
curl -X POST http://localhost:9600/api/serial/send \
  -H "Content-Type: application/json" \
  -d '{"portName":"COM3","data":"A55A0102","hex":true}'

# 关闭串口
curl -X POST http://localhost:9600/api/serial/close \
  -H "Content-Type: application/json" \
  -d '{"portName":"COM3"}'
```

## WebSocket 通信

```javascript
const ws = new WebSocket('ws://localhost:9600/ws');

ws.onopen = () => ws.send('Hello from client');
ws.onmessage = (e) => console.log('收到:', e.data);
```

## 项目结构

```
seriport-cs/
├── build.cmd                   # 构建脚本（双架构：x64 + x86）
├── README.md                   # 项目说明
├── SerialPortService/          # 服务源码
│   ├── Program.cs              # 主程序入口 + 路由注册
│   ├── SerialPortManager.cs    # 串口管理器（并发写入保护）
│   ├── WebSocketConnectionManager.cs # WebSocket 连接管理
│   ├── WindowsServiceInstaller.cs    # Windows 服务安装/卸载
│   ├── appsettings.json        # 外部配置文件
│   ├── wwwroot/
│   │   ├── index.html          # 调试面板
│   │   └── serial-debug.html   # 串口调试工具
│   └── SerialPortService.csproj
├── publish/                    # 发布产物
│   ├── x64/                    # Win7 x64 版本
│   └── x86/                    # Win7 x86 版本
└── docs/                       # 文档
    ├── DESIGN-SPEC.md          # 设计规格文档
    ├── API-REFERENCE.md        # API 访问说明 + JS 客户端示例
    ├── ANTI-THUNDERING-HERD.md # 防雪崩重连原理
    ├── PRODUCTION-GAP-ANALYSIS.md # 生产级缺口诊断报告
    ├── 系统兼容性说明.md        # 系统兼容性矩阵
    └── serialport-ws-client.js # WebSocket 客户端（生产级）
```

## 技术栈

- .NET 6.0 (LTS，兼容 Win7 SP1+)
- ASP.NET Core Minimal API (自宿主 Kestrel)
- System.IO.Ports (串口通信)
- WebSocket (实时双向通信)
- Serilog (结构化日志，控制台 + 文件滚动)
- Windows Service (后台服务)
- 自包含部署 (无需安装 .NET Runtime)

## 文档

| 文档 | 说明 |
|------|------|
| [DESIGN-SPEC.md](docs/DESIGN-SPEC.md) | 完整系统设计规格文档 |
| [API-REFERENCE.md](docs/API-REFERENCE.md) | 完整 API 接口文档 + HTTP/WebSocket JS 客户端示例 |
| [ANTI-THUNDERING-HERD.md](docs/ANTI-THUNDERING-HERD.md) | 防雪崩重连原理与实现说明 |
| [PRODUCTION-GAP-ANALYSIS.md](docs/PRODUCTION-GAP-ANALYSIS.md) | 生产级缺口诊断与修复路线图 |
| [系统兼容性说明.md](docs/系统兼容性说明.md) | 操作系统兼容性矩阵 + 部署指南 |
| [serialport-ws-client.js](docs/serialport-ws-client.js) | 生产级 WebSocket 客户端（心跳+指数退避重连） |
