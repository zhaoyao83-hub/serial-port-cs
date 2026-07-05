/**
 * SerialPortService WebSocket 客户端 — 生产级
 * 
 * 特性:
 * - 主动建立连接，维持心跳
 * - 指数退避自动重连（防止雪崩）
 * - 事件驱动 + Promise 风格 API
 * - 零依赖，浏览器 / Node.js 通用
 * 
 * @version 2.0.0
 */

class SerialPortWS {
  /**
   * @param {object} opts
   * @param {string}  opts.url              - WebSocket 地址，默认 ws://localhost:9600/ws
   * @param {number}  [opts.heartbeatInterval=25000] - 心跳间隔(ms)，应小于服务端 KeepAlive(30s)
   * @param {string}  [opts.heartbeatMsg='PING']     - 心跳消息内容
   * @param {boolean} [opts.autoReconnect=true]       - 是否自动重连
   * @param {number}  [opts.reconnectBaseMs=1000]     - 重连基础延迟(ms)
   * @param {number}  [opts.reconnectMaxMs=30000]     - 重连最大延迟(ms)
   * @param {number}  [opts.reconnectJitter=0.3]      - 重连抖动系数(0~1)，防止惊群
   * @param {number}  [opts.maxReconnectAttempts=0]   - 最大重连次数，0=无限
   */
  constructor(opts = {}) {
    this.url = opts.url || 'ws://localhost:9600/ws';
    this._heartbeatInterval = opts.heartbeatInterval ?? 25000;
    this._heartbeatMsg = opts.heartbeatMsg ?? 'PING';
    this._autoReconnect = opts.autoReconnect ?? true;
    this._reconnectBaseMs = opts.reconnectBaseMs ?? 1000;
    this._reconnectMaxMs = opts.reconnectMaxMs ?? 30000;
    this._reconnectJitter = opts.reconnectJitter ?? 0.3;
    this._maxReconnectAttempts = opts.maxReconnectAttempts ?? 0;

    // 内部状态
    this._ws = null;
    this._clientId = null;
    this._heartbeatTimer = null;
    this._reconnectTimer = null;
    this._reconnectAttempt = 0;
    this._intentionalClose = false;  // 区分主动断开 vs 异常断开
    this._listeners = {};

    // 绑定方法，方便直接作为回调传递
    this.connect = this.connect.bind(this);
    this.disconnect = this.disconnect.bind(this);
    this.send = this.send.bind(this);
  }

  // ==================== 公开 API ====================

  /** 建立连接，返回 Promise */
  connect() {
    this._intentionalClose = false;
    return new Promise((resolve, reject) => {
      if (this._ws && this._ws.readyState === WebSocket.OPEN) {
        return resolve();
      }
      this._cleanup();
      try {
        this._ws = new WebSocket(this.url);
      } catch (e) {
        reject(e);
        return;
      }

      const onOpen = () => {
        this._ws.removeEventListener('open', onOpen);
        this._ws.removeEventListener('error', onError);
        this._reconnectAttempt = 0;
        this._startHeartbeat();
        this._emit('open');
        resolve();
      };

      const onError = (e) => {
        this._ws.removeEventListener('open', onOpen);
        this._ws.removeEventListener('error', onError);
        reject(e);
      };

      this._ws.addEventListener('open', onOpen);
      this._ws.addEventListener('error', onError);

      this._ws.addEventListener('close', (e) => {
        this._stopHeartbeat();
        this._emit('close', { code: e.code, reason: e.reason });
        if (!this._intentionalClose && this._autoReconnect) {
          this._scheduleReconnect();
        }
      });

      this._ws.addEventListener('message', (e) => {
        try {
          const msg = JSON.parse(e.data);
          this._emit('message', msg);
          // 按类型分发
          if (msg.type === 'echo' && !this._clientId) {
            this._clientId = msg.clientId;
          }
          if (msg.type) {
            this._emit(msg.type, msg);
          }
        } catch {
          this._emit('message', { type: 'text', data: e.data });
        }
      });
    });
  }

  /** 主动断开连接，不会触发自动重连 */
  disconnect() {
    this._intentionalClose = true;
    this._stopHeartbeat();
    this._cancelReconnect();
    if (this._ws) {
      this._ws.close(1000, '客户端主动断开');
      this._ws = null;
    }
    this._reconnectAttempt = 0;
  }

  /** 发送消息 */
  send(data) {
    if (!this._ws || this._ws.readyState !== WebSocket.OPEN) {
      throw new Error('WebSocket 未连接');
    }
    const payload = typeof data === 'string' ? data : JSON.stringify(data);
    this._ws.send(payload);
  }

  /** 注册事件监听，返回 this 支持链式调用 */
  on(event, callback) {
    if (!this._listeners[event]) this._listeners[event] = [];
    this._listeners[event].push(callback);
    return this;
  }

  /** 移除事件监听 */
  off(event, callback) {
    if (this._listeners[event]) {
      this._listeners[event] = this._listeners[event].filter(cb => cb !== callback);
    }
    return this;
  }

  /** 连接状态 */
  get isConnected() {
    return this._ws && this._ws.readyState === WebSocket.OPEN;
  }

  /** 当前客户端 ID（由服务端分配） */
  get clientId() {
    return this._clientId;
  }

  /** 当前重连次数 */
  get reconnectAttempt() {
    return this._reconnectAttempt;
  }

  // ==================== 内部方法 ====================

  _emit(event, data) {
    (this._listeners[event] || []).forEach(cb => {
      try { cb(data); } catch (e) { console.error(`[SerialPortWS] 事件处理错误(${event}):`, e); }
    });
  }

  _startHeartbeat() {
    this._stopHeartbeat();
    this._heartbeatTimer = setInterval(() => {
      if (this.isConnected) {
        try { this.send(this._heartbeatMsg); } catch { /* 忽略发送失败，等待 close 事件处理 */ }
      }
    }, this._heartbeatInterval);
  }

  _stopHeartbeat() {
    if (this._heartbeatTimer) {
      clearInterval(this._heartbeatTimer);
      this._heartbeatTimer = null;
    }
  }

  _cancelReconnect() {
    if (this._reconnectTimer) {
      clearTimeout(this._reconnectTimer);
      this._reconnectTimer = null;
    }
  }

  /**
   * 指数退避 + 随机抖动 — 防止雪崩式重连
   * 
   * 重连间隔公式:
   *   delay = min(base * 2^attempt, max) * (1 + jitter * (random - 0.5))
   * 
   * 示例 (base=1000, max=30000, jitter=0.3):
   *   第1次: ~1000ms
   *   第2次: ~2000ms
   *   第3次: ~4000ms
   *   第4次: ~8000ms
   *   第5次: ~16000ms
   *   第6次及以后: ~30000ms (达到上限)
   */
  _scheduleReconnect() {
    // 检查是否超过最大重连次数
    if (this._maxReconnectAttempts > 0 && this._reconnectAttempt >= this._maxReconnectAttempts) {
      this._emit('reconnect_failed', { attempts: this._reconnectAttempt });
      console.warn(`[SerialPortWS] 已达最大重连次数 (${this._maxReconnectAttempts})，停止重连`);
      return;
    }

    this._reconnectAttempt++;
    const base = this._reconnectBaseMs * Math.pow(2, this._reconnectAttempt - 1);
    const capped = Math.min(base, this._reconnectMaxMs);
    const jitter = capped * this._reconnectJitter * (Math.random() - 0.5);
    const delay = Math.round(capped + jitter);

    this._emit('reconnecting', {
      attempt: this._reconnectAttempt,
      delay,
      maxAttempts: this._maxReconnectAttempts,
    });

    console.log(
      `[SerialPortWS] 将在 ${(delay / 1000).toFixed(1)}s 后第 ${this._reconnectAttempt} 次重连...`
    );

    this._reconnectTimer = setTimeout(() => {
      this._reconnectTimer = null;
      this.connect().catch(() => {
        // connect 失败会触发 close 事件，close 中会再次调用 _scheduleReconnect
      });
    }, delay);
  }

  _cleanup() {
    this._stopHeartbeat();
    this._cancelReconnect();
    if (this._ws) {
      try { this._ws.close(1000); } catch { /* 忽略 */ }
      this._ws = null;
    }
  }
}

// ==================== 使用示例 ====================
if (typeof module !== 'undefined' && module.exports) {
  module.exports = SerialPortWS;
}

/*
// ─── 浏览器 / Node.js 使用方式 ───

const ws = new SerialPortWS({
  url: 'ws://localhost:9600/ws',
  heartbeatInterval: 25000,       // 25s 心跳
  autoReconnect: true,            // 自动重连
  reconnectBaseMs: 1000,          // 重连基础延迟 1s
  reconnectMaxMs: 30000,          // 重连最大延迟 30s
  reconnectJitter: 0.3,           // 30% 随机抖动
  maxReconnectAttempts: 0,        // 0=无限重连
});

// 注册事件
ws.on('open', () => {
  console.log('已连接, ID:', ws.clientId);
  ws.send('Hello SerialPortService');
})
.on('echo', (msg) => {
  console.log('回显:', msg.clientId, msg.message);
})
.on('broadcast', (msg) => {
  console.log('广播:', msg.message);
})
.on('message', (msg) => {
  console.log('消息:', msg);
})
.on('reconnecting', ({ attempt, delay }) => {
  console.log(`重连中... 第${attempt}次, ${delay}ms后`);
})
.on('reconnect_failed', ({ attempts }) => {
  console.log(`重连失败, 已尝试${attempts}次`);
})
.on('close', ({ code, reason }) => {
  console.log('断开:', code, reason);
});

// 连接
ws.connect().then(() => {
  console.log('连接成功');
}).catch(err => {
  console.error('首次连接失败:', err.message);
  // 如果 autoReconnect=true，会自动开始重连
});

// 主动断开
// ws.disconnect();
*/
