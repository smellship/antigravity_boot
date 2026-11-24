param(
    [switch]$PurgeData,
    [switch]$KeepPluginFiles,
    [switch]$Quiet
)

$ErrorActionPreference = "SilentlyContinue"

# 1. 停止浮层进程
Get-Process -Name "AntigravityBootOverlay" -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 300

# 2. 清理开机自启项
Remove-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "AntigravityBootStudio" -ErrorAction SilentlyContinue

# 3. 清理自动写入的显卡高性能偏好项
$gpuKey = "HKCU:\Software\Microsoft\DirectX\UserGpuPreferences"
if (Test-Path $gpuKey) {
    $props = (Get-Item $gpuKey).Property
    foreach ($p in $props) {
        if ($p -match "AntigravityBootOverlay\.exe") {
            Remove-ItemProperty -Path $gpuKey -Name $p -ErrorAction SilentlyContinue
        }
    }
}

# 4. 清理运行时遗留状态文件
$dataDir = Join-Path $env:LOCALAPPDATA "AntigravityBootStudio"
$runtimeFile = Join-Path $dataDir "runtime.json"
if (Test-Path $runtimeFile) { Remove-Item $runtimeFile -Force }

if ($PurgeData -and (Test-Path $dataDir)) {
    Remove-Item -Path $dataDir -Recurse -Force
}

if (-not $KeepPluginFiles) {
    $pluginDir = Join-Path $env:USERPROFILE ".gemini\config\plugins\antigravity-boot"
    if (Test-Path $pluginDir) {
        Remove-Item -Path $pluginDir -Recurse -Force
    }
}

if (-not $Quiet) {
    Write-Host "已完成 Antigravity Boot Studio 卸载清理。Antigravity 本体文件未被修改。" -ForegroundColor Green
}
