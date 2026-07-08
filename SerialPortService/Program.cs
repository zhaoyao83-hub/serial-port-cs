using System.IO.Ports;
using System.Management;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Serilog;

namespace SerialPortService;

/// <summary>
/// 串口通信服务主程序
/// - 自宿主 ASP.NET Core Web 服务
/// - WebSocket 双向通信
/// - RESTful API
/// - 支持安装为 Windows 服务
/// - 配置外部化 (appsettings.json)
/// - Serilog 结构化日志 (控制台 + 文件滚动)
/// </summary>
public class Program
{
    public static async Task Main(string[] args)
    {
        // 控制台标题和启动提示
        Console.Title = "SerialPortService v1.0.0";
        Console.WriteLine("╔════════════════════════════════════════════╗");
        Console.WriteLine("║   SerialPortService - 串口通信服务        ║");
        Console.WriteLine("║   WebSocket + RESTful API                 ║");
        Console.WriteLine("╚════════════════════════════════════════════╝");
        Console.WriteLine();

        // ---- 处理命令行：安装/卸载/启停 Windows 服务 ----
        if (args.Length > 0)
        {
            var cmd = args[0].ToLowerInvariant();
            if (cmd is "install" or "uninstall" or "start" or "stop" or "status")
            {
                if (!IsAdministrator())
                {
                    Console.WriteLine("错误: 此操作需要管理员权限，请以管理员身份运行。");
                    Environment.Exit(1);
                }
                WindowsServiceInstaller.HandleCommand(args);
                return;
            }
        }

        // ---- 构建配置 ----
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ApplicationName = "SerialPortService",
            ContentRootPath = AppContext.BaseDirectory,
            WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot")
        });

        // #4 配置外部化：从 appsettings.json 读取配置
        builder.Configuration
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables("SERIALPORT_")
            .AddCommandLine(args);

        // #5 日志持久化：Serilog 替换默认 Logger
        builder.Host.UseSerilog((context, services, configuration) =>
        {
            configuration
                .ReadFrom.Configuration(context.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Application", "SerialPortService");
        });

        // 读取配置值（带默认值兜底）
        var urls = builder.Configuration.GetValue<string>("Urls") ?? "http://0.0.0.0:9600";
        var wsKeepAlive = builder.Configuration.GetValue<int>("WebSocket:KeepAliveSeconds", 30);
        var wsBufferSize = builder.Configuration.GetValue<int>("WebSocket:ReceiveBufferSize", 4096);
        var maxWsConnections = builder.Configuration.GetValue<int>("WebSocket:MaxConnections", 100);

        // ---- 注册服务 ----
        builder.Services.AddSingleton<SerialPortManager>();
        builder.Services.AddSingleton<WebSocketConnectionManager>();
        builder.Services.AddSingleton(new WsServiceOptions
        {
            MaxConnections = maxWsConnections,
            KeepAliveSeconds = wsKeepAlive,
            ReceiveBufferSize = wsBufferSize
        });

        builder.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
            });
        });

        // Windows 服务支持（仅服务模式下启用，控制台模式跳过避免崩溃）
        if (!Environment.UserInteractive)
        {
            builder.Host.UseWindowsService(options =>
            {
                options.ServiceName = "SerialPortService";
            });
        }

        builder.WebHost.UseUrls(urls);

        var app = builder.Build();

        // #7 优雅关闭钩子
        var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
        RegisterShutdownHooks(app, lifetime);

        app.UseCors();
        app.UseWebSockets();

        // 静态文件
        app.UseDefaultFiles();
        app.UseStaticFiles();

        // 注册路由
        RegisterRoutes(app, wsBufferSize);

        Log.Information("SerialPortService 启动完成，监听地址: {Urls}", urls);
        Console.WriteLine($"✓ 服务已启动，监听地址: {urls}");
        Console.WriteLine("  按 Ctrl+C 停止服务");
        Console.WriteLine();
        await app.RunAsync();
    }

    /// <summary>
    /// #7 注册优雅关闭钩子：服务停止时主动关闭所有串口和 WebSocket 连接
    /// </summary>
    private static void RegisterShutdownHooks(WebApplication app, IHostApplicationLifetime lifetime)
    {
        lifetime.ApplicationStopping.Register(() =>
        {
            Console.WriteLine("收到停止信号，正在优雅关闭...");
            Log.Information("收到停止信号，开始优雅关闭...");

            try
            {
                var wsManager = app.Services.GetRequiredService<WebSocketConnectionManager>();
                var wsCount = wsManager.Count;
                if (wsCount > 0)
                {
                    Log.Information("正在关闭 {Count} 个 WebSocket 连接...", wsCount);
                    wsManager.CloseAllAsync(CancellationToken.None).GetAwaiter().GetResult();
                }

                var serialMgr = app.Services.GetRequiredService<SerialPortManager>();
                serialMgr.Dispose();
                Log.Information("所有串口已关闭");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "优雅关闭过程中发生错误");
            }
        });

        lifetime.ApplicationStopped.Register(() =>
        {
            Log.Information("SerialPortService 已停止");
            Log.CloseAndFlush();
        });
    }

    private static void RegisterRoutes(WebApplication app, int wsBufferSize)
    {
        // ==================== WebSocket ====================
        app.Map("/ws", async (HttpContext context, WebSocketConnectionManager wsManager,
                               SerialPortManager serialMgr, WsServiceOptions wsOptions) =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync("WebSocket connection required.");
                return;
            }

            // 检查最大连接数
            if (wsManager.Count >= wsOptions.MaxConnections)
            {
                context.Response.StatusCode = 503;
                await context.Response.WriteAsync($"已达到最大连接数限制 ({wsOptions.MaxConnections})");
                Log.Warning("WebSocket 连接被拒绝: 已达最大连接数 {Max}", wsOptions.MaxConnections);
                return;
            }

            using var ws = await context.WebSockets.AcceptWebSocketAsync();
            var clientId = Guid.NewGuid().ToString("N")[..8];
            wsManager.Add(clientId, ws);
            Log.Information("WebSocket 客户端已连接: {ClientId}, 当前在线: {Count}", clientId, wsManager.Count);

            // #12 该客户端订阅的串口列表
            var subscribedPorts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // #12 注册串口数据到达事件处理器
            void OnDataReceived(object? sender, SerialPortDataReceivedEventArgs e)
            {
                if (subscribedPorts.Contains(e.PortName))
                {
                    // 通过 WebSocket 实时推送串口数据
                    var msg = JsonSerializer.Serialize(new
                    {
                        type = "serial-data",
                        portName = e.PortName,
                        data = Convert.ToBase64String(e.Data),
                        hex = BitConverter.ToString(e.Data).Replace("-", " "),
                        byteCount = e.Data.Length,
                        timestamp = e.Timestamp
                    });
                    // 忽略推送失败（客户端可能已断开）
                    _ = wsManager.SendToAsync(clientId, msg, context.RequestAborted);
                }
            }

            serialMgr.DataReceived += OnDataReceived;

            try
            {
                // #6 CancellationToken: 使用 context.RequestAborted 支持优雅取消
                await ReceiveLoop(ws, clientId, wsManager, serialMgr, subscribedPorts,
                                  context.RequestAborted, wsBufferSize);
            }
            finally
            {
                // 取消事件订阅
                serialMgr.DataReceived -= OnDataReceived;
                wsManager.Remove(clientId);
                Log.Information("WebSocket 客户端已断开: {ClientId}, 当前在线: {Count}", clientId, wsManager.Count);
            }
        });

        // ==================== 健康检查 ====================
        app.MapGet("/api/health", (SerialPortManager mgr, WebSocketConnectionManager wsManager) =>
        {
            return Results.Ok(new
            {
                status = "running",
                timestamp = DateTime.UtcNow,
                version = "1.0.0",
                uptime = (DateTime.UtcNow - System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime()).ToString(@"d\.hh\:mm\:ss"),
                wsClients = wsManager.Count,
                openedPorts = mgr.GetOpenedPorts().Select(p => p.PortName).ToList()
            });
        });

        // ==================== 串口管理 REST API ====================

        // 获取所有可用串口
        app.MapGet("/api/serial/ports", (SerialPortManager mgr) =>
        {
            var ports = SerialPort.GetPortNames()
                .Select(name => new
                {
                    name,
                    description = TryGetPortDescription(name),
                    isOpen = mgr.IsPortOpen(name)
                });
            return Results.Ok(ports);
        });

        // 打开串口
        app.MapPost("/api/serial/open", (SerialPortManager mgr, IConfiguration config, OpenPortRequest req) =>
        {
            try
            {
                var defaultBaudRate = config.GetValue<int>("SerialPort:DefaultBaudRate", 9600);
                var readTimeout = config.GetValue<int>("SerialPort:ReadTimeoutMs", 500);
                var writeTimeout = config.GetValue<int>("SerialPort:WriteTimeoutMs", 500);
                var newLine = config.GetValue<string>("SerialPort:NewLine") ?? "\r\n";

                var baudRate = req.BaudRate > 0 ? req.BaudRate : defaultBaudRate;
                mgr.Open(req.PortName, baudRate, req.Parity, req.DataBits, req.StopBits,
                         readTimeout, writeTimeout, newLine);
                return Results.Ok(new { success = true, message = $"串口 {req.PortName} 已打开" });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { success = false, message = ex.Message });
            }
        });

        // 关闭串口
        app.MapPost("/api/serial/close", (SerialPortManager mgr, ClosePortRequest req) =>
        {
            try
            {
                mgr.Close(req.PortName);
                return Results.Ok(new { success = true, message = $"串口 {req.PortName} 已关闭" });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { success = false, message = ex.Message });
            }
        });

        // 获取已打开串口列表
        app.MapGet("/api/serial/opened", (SerialPortManager mgr) =>
        {
            var opened = mgr.GetOpenedPorts().Select(p => new
            {
                p.PortName,
                p.BaudRate,
                p.IsOpen
            });
            return Results.Ok(opened);
        });

        // 发送数据到串口
        app.MapPost("/api/serial/send", async (SerialPortManager mgr, SendDataRequest req) =>
        {
            try
            {
                var data = req.Hex ? ConvertFromHex(req.Data) : Encoding.UTF8.GetBytes(req.Data);
                await mgr.SendAsync(req.PortName, data);
                return Results.Ok(new { success = true, message = "数据已发送" });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { success = false, message = ex.Message });
            }
        });

        // 读取串口数据
        app.MapGet("/api/serial/read", async (SerialPortManager mgr, string portName) =>
        {
            try
            {
                var bytes = await mgr.ReadAllBytesAsync(portName);
                if (bytes != null && bytes.Length > 0)
                {
                    return Results.Ok(new
                    {
                        success = true,
                        data = Convert.ToBase64String(bytes),
                        hex = BitConverter.ToString(bytes).Replace("-", " "),
                        byteCount = bytes.Length
                    });
                }
                return Results.Ok(new { success = true, data = "", hex = "", byteCount = 0 });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { success = false, message = ex.Message });
            }
        });

        // ==================== 广播消息 API ====================
        app.MapPost("/api/broadcast", async (WebSocketConnectionManager wsManager, BroadcastRequest req) =>
        {
            var msg = JsonSerializer.Serialize(new
            {
                type = "broadcast",
                message = req.Message,
                timestamp = DateTime.UtcNow
            });
            await wsManager.BroadcastAsync(msg);
            return Results.Ok(new { success = true, clientCount = wsManager.Count });
        });

        // 获取在线客户端数
        app.MapGet("/api/clients", (WebSocketConnectionManager wsManager) =>
        {
            return Results.Ok(new { count = wsManager.Count });
        });
    }

    /// <summary>
    /// #6 使用 CancellationToken 支持优雅取消
    /// #12 支持串口订阅/取消订阅命令
    /// </summary>
    private static async Task ReceiveLoop(
        WebSocket ws, string clientId, WebSocketConnectionManager wsManager,
        SerialPortManager serialMgr, HashSet<string> subscribedPorts,
        CancellationToken cancellationToken, int bufferSize)
    {
        var buffer = new byte[bufferSize * 4]; // 增大缓冲区支持 JSON 命令
        while (ws.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                var result = await ws.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var incoming = Encoding.UTF8.GetString(buffer, 0, result.Count);

                    // #12 解析订阅/取消订阅命令
                    await HandleWsCommand(ws, clientId, wsManager, serialMgr, subscribedPorts,
                                          incoming, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                Log.Information("WebSocket 接收循环被取消: {ClientId}", clientId);
                break;
            }
            catch (WebSocketException ex) when (ex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
            {
                Log.Warning("WebSocket 连接异常关闭: {ClientId}", clientId);
                break;
            }
        }
    }

    /// <summary>
    /// #12 处理 WebSocket 客户端命令
    /// </summary>
    private static async Task HandleWsCommand(
        WebSocket ws, string clientId, WebSocketConnectionManager wsManager,
        SerialPortManager serialMgr, HashSet<string> subscribedPorts,
        string json, CancellationToken cancellationToken)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("action", out var actionProp))
            {
                await SendWsResponse(ws, "error", "缺少 action 字段", cancellationToken);
                return;
            }

            var action = actionProp.GetString();

            switch (action)
            {
                case "subscribe":
                    // 订阅串口数据: {"action":"subscribe","port":"COM3"}
                    if (root.TryGetProperty("port", out var portProp))
                    {
                        var portName = portProp.GetString()!;
                        if (serialMgr.IsPortOpen(portName))
                        {
                            subscribedPorts.Add(portName);
                            Log.Information("客户端 {ClientId} 订阅串口 {PortName}", clientId, portName);
                            await SendWsResponse(ws, "subscribed", $"已订阅串口 {portName}", cancellationToken);
                        }
                        else
                        {
                            await SendWsResponse(ws, "error", $"串口 {portName} 未打开", cancellationToken);
                        }
                    }
                    else
                    {
                        await SendWsResponse(ws, "error", "缺少 port 字段", cancellationToken);
                    }
                    break;

                case "unsubscribe":
                    // 取消订阅: {"action":"unsubscribe","port":"COM3"}
                    if (root.TryGetProperty("port", out var unsubPortProp))
                    {
                        var portName = unsubPortProp.GetString()!;
                        subscribedPorts.Remove(portName);
                        Log.Information("客户端 {ClientId} 取消订阅串口 {PortName}", clientId, portName);
                        await SendWsResponse(ws, "unsubscribed", $"已取消订阅串口 {portName}", cancellationToken);
                    }
                    else
                    {
                        await SendWsResponse(ws, "error", "缺少 port 字段", cancellationToken);
                    }
                    break;

                case "list-subscriptions":
                    // 查看当前订阅列表
                    var subs = subscribedPorts.ToArray();
                    var listMsg = JsonSerializer.Serialize(new
                    {
                        type = "subscriptions",
                        ports = subs
                    });
                    await wsManager.SendToAsync(clientId, listMsg, cancellationToken);
                    break;

                default:
                    // 未知命令，回显
                    var echo = JsonSerializer.Serialize(new
                    {
                        type = "echo",
                        clientId,
                        message = json,
                        timestamp = DateTime.UtcNow
                    });
                    await wsManager.SendToAsync(clientId, echo, cancellationToken);
                    break;
            }
        }
        catch (JsonException)
        {
            await SendWsResponse(ws, "error", "无效的 JSON 格式", cancellationToken);
        }
    }

    /// <summary>
    /// 向 WebSocket 客户端发送简单响应
    /// </summary>
    private static async Task SendWsResponse(WebSocket ws, string type, string message,
                                              CancellationToken cancellationToken)
    {
        var response = JsonSerializer.Serialize(new { type, message, timestamp = DateTime.UtcNow });
        var bytes = Encoding.UTF8.GetBytes(response);
        await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);
    }

    private static string? TryGetPortDescription(string portName)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                $"SELECT Description FROM Win32_SerialPort WHERE DeviceID='{portName.Replace("COM", "")}'");
            foreach (var obj in searcher.Get())
            {
                return obj["Description"]?.ToString();
            }
        }
        catch { }
        return null;
    }

    private static bool IsAdministrator()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        var principal = new System.Security.Principal.WindowsPrincipal(identity);
        return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }

    private static byte[] ConvertFromHex(string hex)
    {
        hex = hex.Replace(" ", "").Replace("-", "");
        if (hex.Length % 2 != 0)
            throw new ArgumentException("HEX 字符串长度必须为偶数");
        var bytes = new byte[hex.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        return bytes;
    }
}

// ---- WebSocket 配置模型 ----
public class WsServiceOptions
{
    public int MaxConnections { get; set; } = 100;
    public int KeepAliveSeconds { get; set; } = 30;
    public int ReceiveBufferSize { get; set; } = 4096;
}

// ---- 请求模型 ----
public record OpenPortRequest(string PortName, int BaudRate = 0, Parity Parity = Parity.None, int DataBits = 8, StopBits StopBits = StopBits.One);
public record ClosePortRequest(string PortName);
public record SendDataRequest(string PortName, string Data, bool Hex = false);
public record BroadcastRequest(string Message);
