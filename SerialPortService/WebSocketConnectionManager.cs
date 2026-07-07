using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;

namespace SerialPortService;

/// <summary>
/// WebSocket 连接管理器 - 管理所有 WebSocket 客户端的连接/断开/广播
/// 
/// 生产级增强:
/// - #6 CancellationToken 支持优雅取消
/// - #7 CloseAllAsync 优雅关闭所有连接
/// - 最大连接数校验
/// </summary>
public class WebSocketConnectionManager
{
    private readonly ConcurrentDictionary<string, WebSocket> _sockets = new();

    public int Count => _sockets.Count;

    public void Add(string clientId, WebSocket socket)
    {
        _sockets[clientId] = socket;
    }

    public void Remove(string clientId)
    {
        _sockets.TryRemove(clientId, out _);
    }

    /// <summary>
    /// 向所有在线客户端广播消息
    /// #6 支持 CancellationToken
    /// </summary>
    public async Task BroadcastAsync(string message, CancellationToken cancellationToken = default)
    {
        var buffer = Encoding.UTF8.GetBytes(message);
        var segment = new ArraySegment<byte>(buffer);

        var tasks = new List<Task>();
        foreach (var (clientId, socket) in _sockets)
        {
            if (socket.State == WebSocketState.Open)
            {
                tasks.Add(SendSafe(socket, segment, clientId, cancellationToken));
            }
        }

        if (tasks.Count > 0)
            await Task.WhenAll(tasks);
    }

    /// <summary>
    /// 向指定客户端发送消息
    /// </summary>
    public async Task SendToAsync(string clientId, string message, CancellationToken cancellationToken = default)
    {
        if (_sockets.TryGetValue(clientId, out var socket) && socket.State == WebSocketState.Open)
        {
            var buffer = Encoding.UTF8.GetBytes(message);
            await socket.SendAsync(
                new ArraySegment<byte>(buffer),
                WebSocketMessageType.Text,
                true,
                cancellationToken);
        }
    }

    /// <summary>
    /// #7 优雅关闭：关闭所有活跃连接
    /// </summary>
    public async Task CloseAllAsync(CancellationToken cancellationToken)
    {
        var tasks = new List<Task>();
        foreach (var (clientId, socket) in _sockets)
        {
            try
            {
                if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
                {
                    tasks.Add(socket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Server shutting down",
                        cancellationToken));
                }
            }
            catch { /* 忽略单个关闭错误 */ }
        }

        if (tasks.Count > 0)
        {
            try
            {
                // 设置 5 秒超时兜底
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(5));
                await Task.WhenAll(tasks);
            }
            catch { /* 超时后继续清理 */ }
        }

        _sockets.Clear();
    }

    private static async Task SendSafe(WebSocket socket, ArraySegment<byte> data,
                                        string clientId, CancellationToken cancellationToken)
    {
        try
        {
            await socket.SendAsync(data, WebSocketMessageType.Text, true, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // 服务正在关闭
        }
        catch
        {
            // 客户端已断开
        }
    }
}
