param(
    [Parameter(Mandatory = $true)]
    [string]$InputPath,
    [string]$OutputPath = "",
    [int]$Width = 640,
    [int]$Height = 360,
    [int]$Bitrate = 1200000,
    [int]$Fps = 30
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $InputPath)) {
    throw "输入视频不存在: $InputPath"
}

$fullInput = [System.IO.Path]::GetFullPath($InputPath)
$inDir = [System.IO.Path]::GetDirectoryName($fullInput)
if ([string]::IsNullOrEmpty($OutputPath)) {
    $OutputPath = Join-Path $inDir "loop_widget.mp4"
}
$fullOutput = [System.IO.Path]::GetFullPath($OutputPath)
$outDir = [System.IO.Path]::GetDirectoryName($fullOutput)
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

Add-Type -AssemblyName System.Runtime.WindowsRuntime
$null = [Windows.Storage.StorageFile, Windows.Storage, ContentType = WindowsRuntime]
$null = [Windows.Storage.StorageFolder, Windows.Storage, ContentType = WindowsRuntime]
$null = [Windows.Storage.CreationCollisionOption, Windows.Storage, ContentType = WindowsRuntime]
$null = [Windows.Media.Transcoding.MediaTranscoder, Windows.Media.Transcoding, ContentType = WindowsRuntime]
$null = [Windows.Media.MediaProperties.MediaEncodingProfile, Windows.Media.MediaProperties, ContentType = WindowsRuntime]
$null = [Windows.Media.MediaProperties.VideoEncodingQuality, Windows.Media.MediaProperties, ContentType = WindowsRuntime]

$asTaskGenericOp = @([System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {
    $_.Name -eq 'AsTask' -and
    $_.IsGenericMethod -and
    $_.GetGenericArguments().Count -eq 1 -and
    $_.GetParameters().Count -eq 1 -and
    $_.GetParameters()[0].ParameterType.Name.StartsWith('IAsyncOperation')
})[0]

$asTaskActionProg = @([System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {
    $_.Name -eq 'AsTask' -and
    $_.IsGenericMethod -and
    $_.GetGenericArguments().Count -eq 1 -and
    $_.GetParameters().Count -eq 1 -and
    $_.GetParameters()[0].ParameterType.Name.StartsWith('IAsyncActionWithProgress')
})[0]

function Await-Op($op, [type]$resultType) {
    $task = $asTaskGenericOp.MakeGenericMethod($resultType).Invoke($null, @($op))
    $task.Wait()
    return $task.Result
}

function Await-ActionProg($action, [type]$progType) {
    $task = $asTaskActionProg.MakeGenericMethod($progType).Invoke($null, @($action))
    $task.Wait()
}

$tmpName = "loop_widget_tmp_" + [System.Guid]::NewGuid().ToString("N").Substring(0, 8) + ".mp4"
$tmpPath = Join-Path $outDir $tmpName

try {
    $srcFile = Await-Op ([Windows.Storage.StorageFile]::GetFileFromPathAsync($fullInput)) ([Windows.Storage.StorageFile])
    $dstFolder = Await-Op ([Windows.Storage.StorageFolder]::GetFolderFromPathAsync($outDir)) ([Windows.Storage.StorageFolder])
    $dstFile = Await-Op ($dstFolder.CreateFileAsync($tmpName, [Windows.Storage.CreationCollisionOption]::ReplaceExisting)) ([Windows.Storage.StorageFile])

    $profile = [Windows.Media.MediaProperties.MediaEncodingProfile]::CreateMp4([Windows.Media.MediaProperties.VideoEncodingQuality]::Wvga)
    $profile.Video.Width = [uint32]$Width
    $profile.Video.Height = [uint32]$Height
    $profile.Video.Bitrate = [uint32]$Bitrate
    $profile.Video.FrameRate.Numerator = [uint32]$Fps
    $profile.Video.FrameRate.Denominator = [uint32]1
    $profile.Audio = $null

    $transcoder = New-Object Windows.Media.Transcoding.MediaTranscoder
    $prep = Await-Op ($transcoder.PrepareFileTranscodeAsync($srcFile, $dstFile, $profile)) ([Windows.Media.Transcoding.PrepareTranscodeResult])
    if (-not $prep.CanTranscode) {
        throw "Windows MediaTranscoder 拒绝转码: $($prep.FailureReason)"
    }

    Await-ActionProg ($prep.TranscodeAsync()) ([double])

    $fiOut = Get-Item $tmpPath
    if (-not $fiOut -or $fiOut.Length -lt 4096) {
        throw "转码输出文件异常过小"
    }

    if (Test-Path $fullOutput) { Remove-Item $fullOutput -Force }
    Move-Item -Path $tmpPath -Destination $fullOutput -Force

    $inBytes = (Get-Item $fullInput).Length
    $outBytes = (Get-Item $fullOutput).Length

    @{
        ok = $true
        inputPath = $fullInput
        outputPath = $fullOutput
        width = $Width
        height = $Height
        inputBytes = $inBytes
        outputBytes = $outBytes
    } | ConvertTo-Json -Compress
}
catch {
    if (Test-Path $tmpPath) { Remove-Item $tmpPath -Force -ErrorAction SilentlyContinue }
    throw
}
