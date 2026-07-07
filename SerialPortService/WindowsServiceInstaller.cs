using System.ServiceProcess;

namespace SerialPortService;

/// <summary>
/// Windows 服务安装/卸载工具
/// 用法: SerialPortService.exe install   → 安装 Windows 服务
///       SerialPortService.exe uninstall → 卸载 Windows 服务
///       SerialPortService.exe start    → 启动服务
///       SerialPortService.exe stop     → 停止服务
///       SerialPortService.exe status   → 查看服务状态
/// </summary>
public static class WindowsServiceInstaller
{
    private const string ServiceName = "SerialPortService";
    private const string DisplayName = "串口通信服务";
    private const string Description = "提供 WebSocket + RESTful API 的串口通信服务，监听端口 9600";

    public static void HandleCommand(string[] args)
    {
        if (args.Length == 0) return;

        var cmd = args[0].ToLowerInvariant();

        switch (cmd)
        {
            case "install":
                InstallService();
                break;
            case "uninstall":
                UninstallService();
                break;
            case "start":
                StartService();
                break;
            case "stop":
                StopService();
                break;
            case "status":
                ShowStatus();
                break;
            default:
                Console.WriteLine($"未知命令: {cmd}");
                Console.WriteLine("可用命令: install, uninstall, start, stop, status");
                break;
        }
    }

    private static void InstallService()
    {
        if (IsServiceInstalled())
        {
            Console.WriteLine("服务已安装，正在重新配置...");
            UninstallService();
            System.Threading.Thread.Sleep(1000);
        }

        var exePath = Environment.ProcessPath!;
        Console.WriteLine($"正在安装服务: {DisplayName}");
        Console.WriteLine($"可执行文件: {exePath}");

        // 使用 sc.exe 创建服务
        var args = $"create \"{ServiceName}\" binPath= \"\\\"{exePath}\\\"\" start= auto DisplayName= \"{DisplayName}\"";
        RunScCommand(args);

        // 设置描述
        RunScCommand($"description \"{ServiceName}\" \"{Description}\"");

        // 设置失败后自动重启
        RunScCommand($"failure \"{ServiceName}\" reset= 86400 actions= restart/5000/restart/10000/restart/30000");

        Console.WriteLine($"服务 {ServiceName} 安装成功！");
        Console.WriteLine("服务将在系统启动时自动运行。");
        Console.WriteLine($"使用 'net start {ServiceName}' 或 'sc start {ServiceName}' 启动服务。");
    }

    private static void UninstallService()
    {
        if (!IsServiceInstalled())
        {
            Console.WriteLine("服务未安装。");
            return;
        }

        // 先停止服务
        try
        {
            using var sc = new ServiceController(ServiceName);
            if (sc.Status == ServiceControllerStatus.Running)
            {
                sc.Stop();
                sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
                Console.WriteLine("服务已停止。");
            }
        }
        catch { }

        RunScCommand($"delete \"{ServiceName}\"");
        Console.WriteLine($"服务 {ServiceName} 已卸载。");
    }

    private static void StartService()
    {
        using var sc = new ServiceController(ServiceName);
        if (sc.Status == ServiceControllerStatus.Running)
        {
            Console.WriteLine("服务已在运行中。");
            return;
        }
        sc.Start();
        sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(10));
        Console.WriteLine("服务已启动。");
    }

    private static void StopService()
    {
        using var sc = new ServiceController(ServiceName);
        if (sc.Status == ServiceControllerStatus.Stopped)
        {
            Console.WriteLine("服务已停止。");
            return;
        }
        sc.Stop();
        sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
        Console.WriteLine("服务已停止。");
    }

    private static void ShowStatus()
    {
        if (!IsServiceInstalled())
        {
            Console.WriteLine("服务未安装。");
            return;
        }
        using var sc = new ServiceController(ServiceName);
        Console.WriteLine($"服务名称: {ServiceName}");
        Console.WriteLine($"显示名称: {DisplayName}");
        Console.WriteLine($"状态: {sc.Status}");
        Console.WriteLine($"启动类型: {sc.StartType}");
    }

    private static bool IsServiceInstalled()
    {
        return ServiceController.GetServices().Any(s => s.ServiceName == ServiceName);
    }

    private static void RunScCommand(string arguments)
    {
        var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (!string.IsNullOrWhiteSpace(output))
            Console.WriteLine(output.Trim());

        if (process.ExitCode != 0 && !string.IsNullOrWhiteSpace(error))
            Console.Error.WriteLine($"错误: {error.Trim()}");
    }
}
