@echo off
echo ============================================
echo   SerialPortService - 构建脚本
echo ============================================
echo.

cd /d "%~dp0SerialPortService"

echo [1/3] 恢复 NuGet 包...
dotnet restore

echo.
echo [2/3] 发布 x64 版本 (win7-x64)...
dotnet publish -c Release -r win7-x64 --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=true -o ..\publish\x64

echo.
echo [3/3] 发布 x86 版本 (win-x86)...
dotnet publish -c Release -r win-x86 --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=true -o ..\publish\x86

echo.
echo ============================================
echo   构建完成！
echo   x64 输出: ..\publish\x64\SerialPortService.exe
echo   x86 输出: ..\publish\x86\SerialPortService.exe
echo ============================================
echo.
echo 使用方法:
echo   SerialPortService.exe              - 直接运行（前台）
echo   SerialPortService.exe install      - 安装为 Windows 服务
echo   SerialPortService.exe uninstall    - 卸载 Windows 服务
echo   SerialPortService.exe start        - 启动服务
echo   SerialPortService.exe stop         - 停止服务
echo   SerialPortService.exe status       - 查看服务状态
echo.
pause
