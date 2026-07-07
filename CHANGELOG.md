# CHANGELOG

> SerialPortService 版本变更日志，记录所有值得关注的功能变更、修复和文档更新。

格式遵循 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.0.0/)，版本号遵循 [语义化版本](https://semver.org/lang/zh-CN/)。

---

## [1.0.0] — 2026-07-01

### 新增
- **串口管理 REST API**：提供串口列表查询、打开/关闭、发送/读取数据的 HTTP 接口
- **WebSocket 实时通信**：支持多客户端双向消息转发与广播，8位 hex clientId 标识
- **Windows 服务宿主**：支持 install/uninstall/start/stop/status 命令，开机自启
- **三级崩溃恢复**：5s → 10s → 30s 自动重启策略，24小时无崩溃重置计数
- **健康检查端点**：`GET /api/health` 返回 status/timestamp/version/uptime/wsClients/openedPorts
- **内置调试面板**：`wwwroot/index.html` 和 `wwwroot/serial-debug.html` 提供可视化调试
- **防雪崩重连机制**：客户端指数退避 + 随机抖动，防止重连风暴（见 `docs/ANTI-THUNDERING-HERD.md`）
- **双架构构建**：x64 + x86 自包含单文件 EXE 发布，兼容 Win7 SP1+
- **结构化日志**：基于 Serilog 的控制台 + 文件滚动日志（保留30天）
- **配置外部化**：`appsettings.json` 支持环境变量 `SERIALPORT_` 前缀覆盖

### 技术栈
| 组件 | 版本 | 用途 |
|------|------|------|
| .NET Runtime | 6.0 | 自包含部署，无需安装 |
| ASP.NET Core | 6.0 (Minimal API) | Web 框架 |
| Kestrel | 内嵌 | Web 服务器，监听 :9600 |
| System.IO.Ports | 6.0.0 | 串口通信 |
| Serilog.AspNetCore | 6.1.0 | 结构化日志 |
| Microsoft.Extensions.Hosting.WindowsServices | 6.0.0 | 服务宿主 |

### 已知限制
- 无身份认证机制（见 `docs/PRODUCTION-GAP-ANALYSIS.md`）
- HTTP 明文传输，内网环境可接受
- CORS 全开放（`AllowAnyOrigin`）
- 串口操作无权限粒度控制
- 发送/读取的数据内容明文记录在日志中

### 文档
- `README.md` — 项目概览与快速开始
- `docs/DESIGN-SPEC.md` — 完整设计规格文档
- `docs/API-REFERENCE.md` — API 接口文档与客户端示例
- `docs/ANTI-THUNDERING-HERD.md` — 防雪崩重连原理
- `docs/PRODUCTION-GAP-ANALYSIS.md` — 生产级缺口诊断
- `docs/系统兼容性说明.md` — 系统兼容性矩阵
- `docs/serialport-ws-client.js` — WebSocket 客户端 SDK

---

## 版本说明

### 版本号规则
- **主版本号 (MAJOR)**：不兼容的 API 变更
- **次版本号 (MINOR)**：向后兼容的功能新增
- **修订号 (PATCH)**：向后兼容的问题修复

### 变更分类
| 标签 | 说明 |
|------|------|
| `新增` | 新功能 |
| `变更` | 现有功能的变更 |
| `废弃` | 即将移除的功能 |
| `移除` | 已移除的功能 |
| `修复` | 问题修复 |
| `安全` | 安全性修复 |
