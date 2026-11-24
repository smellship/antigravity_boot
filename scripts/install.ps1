param(
    [switch]$SkipBuild,
    [string]$TargetDir = (Join-Path $env:USERPROFILE ".gemini\config\plugins\antigravity-boot")
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot

Write-Host "=== Antigravity Boot Studio 安装程序 ===" -ForegroundColor Cyan

# 1. 停止正在运行的浮层引擎以免占用 exe（优先通过环回端口优雅退出，支持跨会话）
$rtPath = Join-Path $env:LOCALAPPDATA "AntigravityBootStudio\runtime.json"
if (Test-Path $rtPath) {
    try {
        $rt = Get-Content $rtPath -Encoding UTF8 -Raw | ConvertFrom-Json
        if ($rt.port -and $rt.token) {
            Invoke-RestMethod -Uri "http://127.0.0.1:$($rt.port)/shutdown" -Method Post -Headers @{ Authorization = "Bearer $($rt.token)" } -ContentType "application/json" -Body '{}' -TimeoutSec 2 -ErrorAction SilentlyContinue | Out-Null
            Start-Sleep -Milliseconds 500
        }
    } catch { }
}
Get-Process -Name "AntigravityBootOverlay" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 300

# 2. 编译 C# 浮层引擎
if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot "build_engine.ps1") -OutDir (Join-Path $RepoRoot "bin")
}

$exePath = Join-Path $RepoRoot "bin\AntigravityBootOverlay.exe"
if (-not (Test-Path $exePath)) {
    throw "未找到已编译的引擎：$exePath"
}

# 3. 以真实文件就地覆盖安装到插件目录（绝不使用目录联接，也不删除可能被 Sidecar 占用的目录句柄）
New-Item -ItemType Directory -Force -Path $TargetDir | Out-Null

$topFiles = @("plugin.json", "README.md", "LICENSE")
foreach ($f in $topFiles) {
    $src = Join-Path $RepoRoot $f
    if (Test-Path $src) {
        Copy-Item -Path $src -Destination (Join-Path $TargetDir $f) -Force
    }
}

$subDirs = @("assets", "rules", "skills", "sidecars", "scripts", "engine", "bin")
foreach ($d in $subDirs) {
    $srcDir = Join-Path $RepoRoot $d
    $dstDir = Join-Path $TargetDir $d
    if (Test-Path $srcDir) {
        New-Item -ItemType Directory -Force -Path $dstDir | Out-Null
        Get-ChildItem -Path $srcDir -Recurse -File | ForEach-Object {
            $rel = $_.FullName.Substring($srcDir.Length).TrimStart('\', '/')
            $dstFile = Join-Path $dstDir $rel
            $parent = Split-Path -Parent $dstFile
            if (-not (Test-Path $parent)) { New-Item -ItemType Directory -Force -Path $parent | Out-Null }
            Copy-Item -Path $_.FullName -Destination $dstFile -Force
        }
    }
}

Write-Host ""
Write-Host "安装完成！插件目录：$TargetDir" -ForegroundColor Green
Write-Host "下一步："
Write-Host "  1. 在 Antigravity 中打开 Customizations / Plugins 页面启用 antigravity-boot 与 Boot Studio 侧边栏。"
Write-Host "  2. 即可直接体验内置离线主题「✦ 星穹极光」，或在侧边栏主题画廊一键获取官方双剑少女素材包。"
