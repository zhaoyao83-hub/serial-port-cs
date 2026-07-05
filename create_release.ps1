param(
    [Parameter(Mandatory=$true)]
    [string]$Token,
    [string]$Version = "v1.0.0",
    [string]$ZipFile = "SerialPortService-win64.zip"
)

if (-not (Test-Path $ZipFile)) {
    Write-Error "找不到文件: $ZipFile"
    exit 1
}

$body = @{
    tag_name = $Version
    name = "$Version - SerialPortService 自包含构建"
    body = "## 构建信息`n`n- **目标框架**: .NET 6.0`n- **平台**: win-x64`n- **类型**: 自包含 (无需安装 .NET 运行时)`n- **文件**: $ZipFile`n`n## 功能`n- WebSocket 实时数据接收`n- RESTful API 轮询接收`n- 定时发送`n- 串口管理`n`n## 使用方式`n解压后运行 SerialPortService.exe，访问 http://localhost:9600"
    draft = $false
    prerelease = $false
} | ConvertTo-Json

$headers = @{
    Authorization = "Bearer $Token"
    Accept = "application/vnd.github+json"
}

$response = Invoke-RestMethod -Uri "https://api.github.com/repos/zhaoyao83-hub/serial-port-cs/releases" -Method Post -Headers $headers -Body $body -ContentType "application/json"
Write-Host "Release created: $($response.html_url)"

# Upload zip
$uploadUrl = $response.upload_url -replace '\{.*\}', "?name=$ZipFile"
$zipContent = [System.IO.File]::ReadAllBytes((Resolve-Path $ZipFile))
Invoke-RestMethod -Uri $uploadUrl -Method Post -Headers @{Authorization="Bearer $Token"; Accept="application/vnd.github+json"; "Content-Type"="application/zip"} -Body $zipContent
Write-Host "Uploaded: $ZipFile"
