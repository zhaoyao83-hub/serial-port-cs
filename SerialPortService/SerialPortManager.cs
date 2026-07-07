using System.Collections.Concurrent;
using System.IO.Ports;

namespace SerialPortService;

/// <summary>
/// 串口数据接收事件参数
/// </summary>
public class SerialPortDataReceivedEventArgs : EventArgs
{
    public string PortName { get; init; } = string.Empty;
    public byte[] Data { get; init; } = Array.Empty<byte>();
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// 串口管理器 - 管理串口的打开/关闭/读写操作
/// 
/// 生产级增强:
/// - #11 并发写入保护 (SemaphoreSlim 每串口独立锁)
/// - #8  byte[] 直接读取 (保证二进制数据完整性)
/// - #7  优雅关闭 (Dispose 关闭所有串口)
/// - #12 后台监听线程 + DataReceived 事件 (实时推送)
/// - 配置化串口参数 (超时/换行符可外部配置)
/// </summary>
public class SerialPortManager : IDisposable
{
    private readonly ConcurrentDictionary<string, SerialPort> _ports = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _writeLocks = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _listenerCts = new();
    private readonly ConcurrentDictionary<string, Task> _listenerTasks = new();
    private readonly ILogger<SerialPortManager> _logger;
    private bool _disposed;

    /// <summary>
    /// #12 串口数据到达事件 — 当后台监听读取到数据时触发
    /// </summary>
    public event EventHandler<SerialPortDataReceivedEventArgs>? DataReceived;

    public SerialPortManager(ILogger<SerialPortManager> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 打开串口
    /// </summary>
    public void Open(string portName, int baudRate = 9600, Parity parity = Parity.None,
                     int dataBits = 8, StopBits stopBits = StopBits.One,
                     int readTimeout = 500, int writeTimeout = 500, string newLine = "\r\n")
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SerialPortManager));

        if (_ports.ContainsKey(portName))
        {
            _logger.LogWarning("串口 {PortName} 已经打开", portName);
            return;
        }

        var port = new SerialPort(portName, baudRate, parity, dataBits, stopBits)
        {
            ReadTimeout = readTimeout,
            WriteTimeout = writeTimeout,
            NewLine = newLine
        };

        port.Open();
        _ports[portName] = port;

        // #11 为每个串口创建独立的写入锁
        _writeLocks.TryAdd(portName, new SemaphoreSlim(1, 1));

        // #12 启动后台监听线程，持续读取串口数据
        StartListener(portName, port);

        _logger.LogInformation(
            "串口 {PortName} 已打开 (波特率:{BaudRate}, 数据位:{DataBits}, 停止位:{StopBits}, 校验:{Parity})",
            portName, baudRate, dataBits, stopBits, parity);
    }

    /// <summary>
    /// 关闭串口并释放资源
    /// </summary>
    public void Close(string portName)
    {
        // #12 先停止后台监听线程
        StopListener(portName);

        if (_ports.TryRemove(portName, out var port))
        {
            try
            {
                if (port.IsOpen) port.Close();
                port.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "关闭串口 {PortName} 时发生异常", portName);
            }

            // 清理写入锁
            if (_writeLocks.TryRemove(portName, out var semaphore))
            {
                semaphore.Dispose();
            }

            _logger.LogInformation("串口 {PortName} 已关闭", portName);
        }
    }

    /// <summary>
    /// 检查串口是否已打开
    /// </summary>
    public bool IsPortOpen(string portName) => _ports.ContainsKey(portName);

    /// <summary>
    /// 获取所有已打开的串口实例
    /// </summary>
    public IEnumerable<SerialPort> GetOpenedPorts() => _ports.Values;

    /// <summary>
    /// #11 并发写入保护：使用 SemaphoreSlim 保证同一串口的写入严格串行化
    /// </summary>
    public async Task SendAsync(string portName, byte[] data)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SerialPortManager));

        if (!_ports.TryGetValue(portName, out var port) || !port.IsOpen)
            throw new InvalidOperationException($"串口 {portName} 未打开");

        var semaphore = _writeLocks.GetOrAdd(portName, _ => new SemaphoreSlim(1, 1));

        await semaphore.WaitAsync();
        try
        {
            await port.BaseStream.WriteAsync(data);
            await port.BaseStream.FlushAsync();
            _logger.LogInformation("已发送 {ByteCount} 字节到 {PortName}", data.Length, portName);
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <summary>
    /// 异步读取指定字节数
    /// </summary>
    public async Task<byte[]> ReadAsync(string portName, int count)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SerialPortManager));

        if (!_ports.TryGetValue(portName, out var port) || !port.IsOpen)
            throw new InvalidOperationException($"串口 {portName} 未打开");

        var buffer = new byte[count];
        var read = await port.BaseStream.ReadAsync(buffer);
        var result = new byte[read];
        Array.Copy(buffer, result, read);
        return result;
    }

    /// <summary>
    /// #8 直接读取 byte[] — 保证二进制数据完整性
    /// 替代原来 ReadExisting() 返回 string 再 UTF8.GetBytes 的错误方式
    /// </summary>
    public async Task<byte[]?> ReadAllBytesAsync(string portName)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SerialPortManager));

        if (!_ports.TryGetValue(portName, out var port) || !port.IsOpen)
            throw new InvalidOperationException($"串口 {portName} 未打开");

        var bytesToRead = port.BytesToRead;
        if (bytesToRead <= 0)
            return Array.Empty<byte>();

        var buffer = new byte[bytesToRead];
        var totalRead = 0;
        while (totalRead < bytesToRead)
        {
            var read = await port.BaseStream.ReadAsync(buffer.AsMemory(totalRead, bytesToRead - totalRead));
            if (read == 0) break;
            totalRead += read;
        }

        if (totalRead < bytesToRead)
        {
            var result = new byte[totalRead];
            Array.Copy(buffer, result, totalRead);
            return result;
        }

        return buffer;
    }

    /// <summary>
    /// 同步读取已有数据（保留兼容，但建议使用 ReadAllBytesAsync）
    /// </summary>
    public string? ReadExisting(string portName)
    {
        if (_ports.TryGetValue(portName, out var port) && port.IsOpen)
            return port.ReadExisting();
        return null;
    }

    /// <summary>
    /// #7 优雅关闭：释放所有串口资源
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // #12 停止所有后台监听线程
        foreach (var portName in _listenerCts.Keys.ToList())
        {
            StopListener(portName);
        }

        foreach (var kvp in _ports)
        {
            try
            {
                if (kvp.Value.IsOpen) kvp.Value.Close();
                kvp.Value.Dispose();
                _logger.LogInformation("已释放串口: {PortName}", kvp.Key);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "释放串口 {PortName} 时发生异常", kvp.Key);
            }
        }
        _ports.Clear();

        foreach (var kvp in _writeLocks)
        {
            try { kvp.Value.Dispose(); } catch { }
        }
        _writeLocks.Clear();

        // #12 确保所有监听 Task 已完成
        if (_listenerTasks.Count > 0)
        {
            try
            {
                Task.WaitAll(_listenerTasks.Values.ToArray(), TimeSpan.FromSeconds(5));
            }
            catch { }
        }
    }

    #region #12 后台监听线程

    /// <summary>
    /// #12 启动后台监听线程，持续从串口读取数据并通过 DataReceived 事件推送
    /// </summary>
    private void StartListener(string portName, SerialPort port)
    {
        var cts = new CancellationTokenSource();
        _listenerCts[portName] = cts;

        var task = Task.Run(() => ListenerLoop(portName, port, cts.Token), cts.Token);
        _listenerTasks[portName] = task;

        _logger.LogInformation("串口 {PortName} 后台监听已启动", portName);
    }

    /// <summary>
    /// #12 停止后台监听线程
    /// </summary>
    private void StopListener(string portName)
    {
        if (_listenerCts.TryRemove(portName, out var cts))
        {
            try
            {
                cts.Cancel();
                cts.Dispose();
            }
            catch { }
        }

        if (_listenerTasks.TryRemove(portName, out var task))
        {
            try
            {
                task.Wait(TimeSpan.FromSeconds(3));
            }
            catch { }
        }

        _logger.LogInformation("串口 {PortName} 后台监听已停止", portName);
    }

    /// <summary>
    /// #12 监听循环：持续轮询串口缓冲区，读到数据后通过事件推送
    /// 使用轮询而非 DataReceived 事件，避免跨线程问题，且可控性更好
    /// </summary>
    private async Task ListenerLoop(string portName, SerialPort port, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // 检查串口是否仍然打开
                if (!port.IsOpen)
                {
                    _logger.LogWarning("串口 {PortName} 已关闭，监听线程退出", portName);
                    break;
                }

                // 读取缓冲区中的全部数据
                var bytesToRead = port.BytesToRead;
                if (bytesToRead > 0)
                {
                    var readBuffer = new byte[bytesToRead];
                    var totalRead = 0;
                    while (totalRead < bytesToRead)
                    {
                        var read = await port.BaseStream.ReadAsync(
                            readBuffer.AsMemory(totalRead, bytesToRead - totalRead), cancellationToken);
                        if (read == 0) break;
                        totalRead += read;
                    }

                    if (totalRead > 0)
                    {
                        var data = new byte[totalRead];
                        Array.Copy(readBuffer, data, totalRead);

                        _logger.LogInformation(
                            "串口 {PortName} 收到 {ByteCount} 字节: {Hex}",
                            portName, data.Length, BitConverter.ToString(data).Replace("-", " "));

                        // #12 触发事件，通知所有订阅者
                        OnDataReceived(new SerialPortDataReceivedEventArgs
                        {
                            PortName = portName,
                            Data = data,
                            Timestamp = DateTime.UtcNow
                        });
                    }
                }
                else
                {
                    // 没有数据时短暂休眠，避免 CPU 空转
                    await Task.Delay(10, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("串口 {PortName} 监听任务被取消", portName);
                break;
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "串口 {PortName} IO 异常，监听线程退出", portName);
                break;
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "串口 {PortName} 操作无效，监听线程退出", portName);
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "串口 {PortName} 监听异常", portName);
                // 异常后短暂等待再重试
                await Task.Delay(100, cancellationToken);
            }
        }

        _logger.LogInformation("串口 {PortName} 监听循环已退出", portName);
    }

    /// <summary>
    /// #12 触发 DataReceived 事件
    /// </summary>
    protected virtual void OnDataReceived(SerialPortDataReceivedEventArgs e)
    {
        DataReceived?.Invoke(this, e);
    }

    #endregion
}
