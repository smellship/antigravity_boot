param(
    [string]$OutDir = (Join-Path (Split-Path -Parent $PSScriptRoot) "bin")
)

$ErrorActionPreference = "Stop"

$fw = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
if (-not (Test-Path "$fw\csc.exe")) {
    $fw = "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319"
}
$csc = "$fw\csc.exe"
if (-not (Test-Path $csc)) {
    throw "未找到 Windows 自带 C# 编译器 ($csc)，需要 Windows 10/11 (.NET Framework 4.x)。"
}

$wpf = "$fw\WPF"
$engineDir = Join-Path (Split-Path -Parent $PSScriptRoot) "engine"
$sources = Get-ChildItem -Path $engineDir -Filter "*.cs" | Sort-Object Name | ForEach-Object { $_.FullName }
if (-not $sources -or $sources.Count -eq 0) {
    throw "未找到 engine 源码文件 ($engineDir\*.cs)。"
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$outExe = Join-Path $OutDir "AntigravityBootOverlay.exe"

$refs = @(
    "$wpf\PresentationFramework.dll",
    "$wpf\PresentationCore.dll",
    "$wpf\WindowsBase.dll",
    "$fw\System.Xaml.dll",
    "$fw\System.Windows.Forms.dll",
    "$fw\System.Drawing.dll",
    "$fw\System.Web.Extensions.dll",
    "$fw\System.Core.dll",
    "$fw\System.Management.dll"
) | ForEach-Object { "/r:$_" }

$cscArgs = @(
    "/nologo",
    "/target:winexe",
    "/optimize+",
    "/codepage:65001",
    "/platform:anycpu",
    "/out:$outExe"
) + $refs + $sources

Write-Host "正在编译 AntigravityBootOverlay.exe ..."
& $csc @cscArgs
if ($LASTEXITCODE -ne 0) {
    throw "csc.exe 编译失败 (exit=$LASTEXITCODE)"
}
if (-not (Test-Path $outExe)) {
    throw "编译未生成目标文件: $outExe"
}

Write-Host "编译成功: $outExe"
