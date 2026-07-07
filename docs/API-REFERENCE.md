# SerialPortService 串口通信服务 — 访问说明文档

> 版本: 1.0.0 | 服务地址: `http://localhost:9600` | 协议: HTTP + WebSocket

---

## 目录

- [1. 服务概览](#1-服务概览)
- [2. RESTful API 接口文档](#2-restful-api-接口文档)
  - [2.1 健康检查](#21-健康检查)
  - [2.2 串口管理](#22-串口管理)
  - [2.3 客户端与广播](#23-客户端与广播)
- [3. WebSocket 通信协议](#3-websocket-通信协议)
- [4. JavaScript 客户端示例代码](#4-javascript-客户端示例代码)
  - [4.1 HTTP 版本](#41-http-版本)
  - [4.2 WebSocket 版本](#42-websocket-版本)
- [5. curl 命令行示例](#5-curl-命令行示例)

---

## 1. 服务概览

| 项目 | 说明 |
|------|------|
| 协议 | HTTP (RESTful API) + WebSocket |
| 监听地址 | `http://0.0.0.0:9600` |
| CORS | 已开放，允许所有来源跨域访问 |
| 数据格式 | JSON (请求/响应均为 `application/json`) |
| 编码 | UTF-8 |
| 调试页面 | `http://localhost:9600/` 或 `http://localhost:9600/serial-debug.html` |

### 串口参数说明

| 参数 | 类型 | 可选值 | 默认值 |
|------|------|--------|--------|
| `baudRate` | int | 300 / 600 / 1200 / 2400 / 4800 / 9600 / 14400 / 19200 / 38400 / 57600 / 115200 / 128000 / 230400 / 256000 / 460800 / 921600 | `9600` |
| `dataBits` | int | 5 / 6 / 7 / 8 | `8` |
| `stopBits` | string/int | `"None"`(0) / `"One"`(1) / `"Two"`(2) / `"OnePointFive"`(3) | `"One"`(1) |
| `parity` | string/int | `"None"`(0) / `"Odd"`(1) / `"Even"`(2) / `"Mark"`(3) / `"Space"`(4) | `"None"`(0) |
| `hex` | bool | 是否 HEX 模式发送 | `false` |

> 注意：`stopBits` 和 `parity` 在 HTTP 请求中需要传**数字**（对应 `System.IO.Ports.StopBits` / `System.IO.Ports.Parity` 枚举值）。

---

## 2. RESTful API 接口文档

### 通用响应格式

成功响应：
```json
{ "status": "running", ... }
// 或
{ "success": true, "message": "...", ... }
```

失败响应：
```json
{ "success": false, "message": "错误描述" }
```

---

### 2.1 健康检查

#### `GET /api/health`

**响应示例：**
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
| `status` | string | 服务运行状态，正常为 `"running"` |
| `timestamp` | string | UTC 时间戳 (ISO 8601) |
| `version` | string | 服务版本号 |
| `uptime` | string | 进程运行时长，格式 `d.hh:mm:ss` |
| `wsClients` | int | 当前 WebSocket 在线客户端数 |
| `openedPorts` | string[] | 当前已打开的串口名称列表 |

---

### 2.2 串口管理

#### `GET /api/serial/ports` — 获取可用串口列表

**响应示例：**
```json
[
  {
    "name": "COM1",
    "description": "通信端口",
    "isOpen": false
  },
  {
    "name": "COM3",
    "description": "USB Serial Port",
    "isOpen": true
  }
]
```

#### `GET /api/serial/opened` — 获取已打开的串口列表

**响应示例：**
```json
[
  {
    "portName": "COM3",
    "baudRate": 9600,
    "isOpen": true
  }
]
```

#### `POST /api/serial/open` — 打开串口

**请求体：**
```json
{
  "portName": "COM3",
  "baudRate": 115200,
  "parity": 0,
  "dataBits": 8,
  "stopBits": 1
}
```

| 字段 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `portName` | string | ✅ | 串口名称，如 `COM3` |
| `baudRate` | int | ❌ | 波特率，默认 `9600` |
| `parity` | int | ❌ | 校验位: 0=None, 1=Odd, 2=Even, 3=Mark, 4=Space |
| `dataBits` | int | ❌ | 数据位: 5/6/7/8，默认 `8` |
| `stopBits` | int | ❌ | 停止位: 0=None, 1=One, 2=Two, 3=OnePointFive |

**响应示例：**
```json
{ "success": true, "message": "串口 COM3 已打开" }
```

#### `POST /api/serial/close` — 关闭串口

**请求体：**
```json
{ "portName": "COM3" }
```

**响应示例：**
```json
{ "success": true, "message": "串口 COM3 已关闭" }
```

#### `POST /api/serial/send` — 发送数据到串口

**请求体 (ASCII 模式)：**
```json
{
  "portName": "COM3",
  "data": "Hello Serial",
  "hex": false
}
```

**请求体 (HEX 模式)：**
```json
{
  "portName": "COM3",
  "data": "A55A0102",
  "hex": true
}
```

| 字段 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `portName` | string | ✅ | 目标串口名称 |
| `data` | string | ✅ | 发送数据：ASCII 文本 或 HEX 字符串 |
| `hex` | bool | ❌ | 是否 HEX 模式，默认 `false` |

**响应示例：**
```json
{ "success": true, "message": "数据已发送" }
```

#### `GET /api/serial/read?portName=COM3` — 读取串口缓冲区数据

**响应示例（有数据）：**
```json
{
  "success": true,
  "data": "SGVsbG8gZnJvbSBkZXZpY2U=",
  "hex": "48 65 6C 6C 6F 20 66 72 6F 6D 20 64 65 76 69 63 65",
  "byteCount": 15
}
```

> **注意**：`data` 字段为 **Base64 编码** 的原始字节数据。客户端需自行解码，例如：
> - JavaScript: `atob(data)` → `"Hello from device"`
> - C#: `Convert.FromBase64String(data)` → `byte[]`
> - Python: `base64.b64decode(data)` → `bytes`

**响应示例（缓冲区空）：**
```json
{
  "success": true,
  "data": "",
  "hex": "",
  "byteCount": 0
}
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `data` | string | Base64 编码的原始字节数据（空字符串表示无数据） |
| `hex` | string | 十六进制空格分隔表示 |
| `byteCount` | int | 读取到的字节数 |

---

### 2.3 客户端与广播

#### `GET /api/clients` — 获取在线 WebSocket 客户端数

**响应示例：**
```json
{ "count": 3 }
```

#### `POST /api/broadcast` — 向所有 WebSocket 客户端广播消息

**请求体：**
```json
{ "message": "服务即将重启" }
```

**响应示例：**
```json
{ "success": true, "clientCount": 3 }
```

---

## 3. WebSocket 通信协议

| 项目 | 说明 |
|------|------|
| 端点 | `ws://localhost:9600/ws` |
| 连接方式 | 标准 WebSocket 协议 |
| KeepAlive | 30 秒 |
| 消息格式 | JSON 字符串 |
| 客户端 ID | 服务端自动分配 8 位 hex 字符串 |

### 客户端 → 服务端

发送任意文本消息，服务端会回显给所有客户端（包括发送者）：

```json
"任意文本消息"
```

### 服务端 → 客户端

**回显消息（echo）：**
```json
{
  "type": "echo",
  "clientId": "a1b2c3d4",
  "message": "原始消息内容",
  "timestamp": "2026-06-30T23:09:14.869Z"
}
```

**广播消息（broadcast）：**
```json
{
  "type": "broadcast",
  "message": "广播内容",
  "timestamp": "2026-06-30T23:09:14.869Z"
}
```

---

## 4. JavaScript 客户端示例代码

### 4.1 HTTP 版本

完整的串口操作客户端，基于 `fetch` API：

```javascript
/**
 * SerialPortService HTTP 客户端
 * 适用于 Node.js (18+) / 浏览器环境
 */
class SerialPortHttpClient {
  constructor(baseUrl = 'http://localhost:9600') {
    this.baseUrl = baseUrl;
  }

  // ─── 通用请求方法 ───
  async _get(path) {
    const res = await fetch(`${this.baseUrl}${path}`);
    if (!res.ok) throw new Error(`HTTP ${res.status}: ${res.statusText}`);
    return res.json();
  }

  async _post(path, body) {
    const res = await fetch(`${this.baseUrl}${path}`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    });
    if (!res.ok) {
      const err = await res.json().catch(() => ({}));
      throw new Error(err.message || `HTTP ${res.status}`);
    }
    return res.json();
  }

  // ─── 健康检查 ───
  async health() {
    return this._get('/api/health');
  }

  // ─── 获取可用串口列表 ───
  async getPorts() {
    return this._get('/api/serial/ports');
  }

  // ─── 获取已打开串口 ───
  async getOpenedPorts() {
    return this._get('/api/serial/opened');
  }

  // ─── 打开串口 ───
  /**
   * @param {string}  portName  - 串口名称，如 "COM3"
   * @param {object}  [options]
   * @param {number}  [options.baudRate=9600]   - 波特率
   * @param {number}  [options.parity=0]        - 校验位: 0=None 1=Odd 2=Even 3=Mark 4=Space
   * @param {number}  [options.dataBits=8]      - 数据位
   * @param {number}  [options.stopBits=1]      - 停止位: 0=None 1=One 2=Two 3=OnePointFive
   */
  async openPort(portName, options = {}) {
    return this._post('/api/serial/open', {
      portName,
      baudRate: options.baudRate ?? 9600,
      parity: options.parity ?? 0,
      dataBits: options.dataBits ?? 8,
      stopBits: options.stopBits ?? 1,
    });
  }

  // ─── 关闭串口 ───
  async closePort(portName) {
    return this._post('/api/serial/close', { portName });
  }

  // ─── 发送数据 ───
  /**
   * @param {string}  portName  - 目标串口
   * @param {string}  data      - 数据内容
   * @param {boolean} [hex=false] - 是否 HEX 模式
   */
  async sendData(portName, data, hex = false) {
    return this._post('/api/serial/send', { portName, data, hex });
  }

  // ─── 读取缓冲区 ───
  async readData(portName) {
    return this._get(`/api/serial/read?portName=${encodeURIComponent(portName)}`);
  }

  // ─── 获取在线客户端数 ───
  async getClientCount() {
    return this._get('/api/clients');
  }

  // ─── 广播消息 ───
  async broadcast(message) {
    return this._post('/api/broadcast', { message });
  }
}

// ==================== 使用示例 ====================
async function main() {
  const client = new SerialPortHttpClient('http://localhost:9600');

  try {
    // 1. 健康检查
    const health = await client.health();
    console.log('服务状态:', health);

    // 2. 获取串口列表
    const ports = await client.getPorts();
    console.log('可用串口:', ports);

    // 3. 打开串口 (假设 COM3 存在)
    const openResult = await client.openPort('COM3', {
      baudRate: 115200,
      parity: 0,    // None
      dataBits: 8,
      stopBits: 1,  // One
    });
    console.log('打开结果:', openResult);

    // 4. 发送 ASCII 数据
    await client.sendData('COM3', 'Hello Device');

    // 5. 发送 HEX 数据
    await client.sendData('COM3', 'A55A0102', true);

    // 6. 读取缓冲区
    const readResult = await client.readData('COM3');
    console.log('读取数据:', readResult.data);
    console.log('HEX:', readResult.hex);

    // 7. 查看已打开串口
    const opened = await client.getOpenedPorts();
    console.log('已打开:', opened);

    // 8. 关闭串口
    await client.closePort('COM3');

    // 9. 广播消息
    const bc = await client.broadcast('通知: 串口操作已完成');
    console.log('广播已发送, 在线客户端:', bc.clientCount);

  } catch (err) {
    console.error('错误:', err.message);
  }
}

main();
```

### 4.2 WebSocket 版本

基于 WebSocket 的实时双向通信客户端：

```javascript
/**
 * SerialPortService WebSocket 客户端
 * 适用于浏览器 / Node.js (需 ws 库) 环境
 */
class SerialPortWebSocketClient {
  constructor(url = 'ws://localhost:9600/ws') {
    this.url = url;
    this.ws = null;
    this.clientId = null;
    this.listeners = {
      open: [],
      close: [],
      error: [],
      message: [],   // 所有消息
      echo: [],      // echo 类型消息
      broadcast: [], // broadcast 类型消息
    };
  }

  // ─── 连接 ───
  connect() {
    return new Promise((resolve, reject) => {
      this.ws = new WebSocket(this.url);

      this.ws.onopen = () => {
        console.log('[WS] 已连接');
        this._emit('open');
        resolve();
      };

      this.ws.onclose = (e) => {
        console.log(`[WS] 已断开 (code: ${e.code})`);
        this._emit('close', e);
      };

      this.ws.onerror = (e) => {
        console.error('[WS] 连接错误');
        this._emit('error', e);
        reject(e);
      };

      this.ws.onmessage = (e) => {
        try {
          const msg = JSON.parse(e.data);
          this._emit('message', msg);

          // 按类型分发
          if (msg.type === 'echo') {
            if (!this.clientId) this.clientId = msg.clientId;
            this._emit('echo', msg);
          } else if (msg.type === 'broadcast') {
            this._emit('broadcast', msg);
          }
        } catch {
          // 非 JSON 消息，当作纯文本
          this._emit('message', { type: 'text', data: e.data });
        }
      };
    });
  }

  // ─── 发送消息 ───
  send(data) {
    if (!this.ws || this.ws.readyState !== WebSocket.OPEN) {
      throw new Error('WebSocket 未连接');
    }
    const payload = typeof data === 'string' ? data : JSON.stringify(data);
    this.ws.send(payload);
  }

  // ─── 断开连接 ───
  disconnect() {
    if (this.ws) {
      this.ws.close(1000, '客户端主动断开');
      this.ws = null;
    }
  }

  // ─── 事件监听 ───
  on(event, callback) {
    if (this.listeners[event]) {
      this.listeners[event].push(callback);
    }
    return this; // 支持链式调用
  }

  off(event, callback) {
    if (this.listeners[event]) {
      this.listeners[event] = this.listeners[event].filter(cb => cb !== callback);
    }
    return this;
  }

  _emit(event, data) {
    (this.listeners[event] || []).forEach(cb => {
      try { cb(data); } catch (e) { console.error(`[WS] 事件处理错误 (${event}):`, e); }
    });
  }

  // ─── 状态查询 ───
  get isConnected() {
    return this.ws && this.ws.readyState === WebSocket.OPEN;
  }
}

// ==================== 使用示例 ====================
async function main() {
  const wsClient = new SerialPortWebSocketClient('ws://localhost:9600/ws');

  // 注册事件监听
  wsClient
    .on('open', () => {
      console.log('连接成功, 发送测试消息...');
      wsClient.send('Hello from WebSocket client!');
    })
    .on('echo', (msg) => {
      console.log(`[回显] 来自 ${msg.clientId}:`, msg.message);
    })
    .on('broadcast', (msg) => {
      console.log('[广播]', msg.message, `(${msg.timestamp})`);
    })
    .on('message', (msg) => {
      // 接收所有消息
      console.log('[消息]', msg);
    })
    .on('close', (e) => {
      console.log('连接已关闭, code:', e.code);
    })
    .on('error', (e) => {
      console.error('连接错误:', e);
    });

  // 连接
  try {
    await wsClient.connect();
    console.log('客户端 ID:', wsClient.clientId);

    // 5 秒后发送另一条消息
    setTimeout(() => {
      if (wsClient.isConnected) {
        wsClient.send('5 秒后的定时消息');
      }
    }, 5000);

    // 30 秒后断开
    setTimeout(() => {
      wsClient.disconnect();
    }, 30000);

  } catch (err) {
    console.error('连接失败:', err);
  }
}

main();
```

---

## 5. curl 命令行示例

```bash
# ─── 健康检查 ───
curl http://localhost:9600/api/health

# ─── 获取串口列表 ───
curl http://localhost:9600/api/serial/ports

# ─── 获取已打开串口 ───
curl http://localhost:9600/api/serial/opened

# ─── 打开串口 (完整参数) ───
curl -X POST http://localhost:9600/api/serial/open \
  -H "Content-Type: application/json" \
  -d '{"portName":"COM3","baudRate":115200,"parity":0,"dataBits":8,"stopBits":1}'

# ─── 打开串口 (最简参数) ───
curl -X POST http://localhost:9600/api/serial/open \
  -H "Content-Type: application/json" \
  -d '{"portName":"COM3"}'

# ─── 发送 ASCII 数据 ───
curl -X POST http://localhost:9600/api/serial/send \
  -H "Content-Type: application/json" \
  -d '{"portName":"COM3","data":"Hello Serial"}'

# ─── 发送 HEX 数据 ───
curl -X POST http://localhost:9600/api/serial/send \
  -H "Content-Type: application/json" \
  -d '{"portName":"COM3","data":"A55A0102","hex":true}'

# ─── 读取串口数据 ───
curl "http://localhost:9600/api/serial/read?portName=COM3"

# ─── 关闭串口 ───
curl -X POST http://localhost:9600/api/serial/close \
  -H "Content-Type: application/json" \
  -d '{"portName":"COM3"}'

# ─── 获取在线客户端数 ───
curl http://localhost:9600/api/clients

# ─── 广播消息 ───
curl -X POST http://localhost:9600/api/broadcast \
  -H "Content-Type: application/json" \
  -d '{"message":"服务通知: 测试广播"}'

# ─── WebSocket 测试 (需要 wscat 工具) ───
# npm install -g wscat
# wscat -c ws://localhost:9600/ws
```
