# record.ps1: record the whole screen in real time for the demo video (demo\README.md), with ffmpeg's gdigrab: the
# mouse pointer is in the picture, sound is not. It stops after -Seconds, or when you press q in its window.
#   powershell -ExecutionPolicy Bypass -File demo\record.ps1 [-Seconds 120] [-Fps 30] [-Out <file.mkv>] [-Ffmpeg <ffmpeg.exe>] [-DryRun]
# It installs nothing: ffmpeg comes from PATH or -Ffmpeg. -DryRun prints the ffmpeg command without recording.
param([int]$Seconds = 120, [int]$Fps = 30, [string]$Out = '', [string]$Ffmpeg = '', [switch]$DryRun)
$ErrorActionPreference = 'Stop'
if (-not $Ffmpeg) {
    $found = Get-Command ffmpeg.exe -ErrorAction SilentlyContinue
    if ($found) { $Ffmpeg = $found.Source }
}
if (-not $Ffmpeg -or -not (Test-Path -LiteralPath $Ffmpeg)) {
    Write-Host "ffmpeg not found ($(if ($Ffmpeg) { $Ffmpeg } else { 'not on PATH' })). This script doesn't install it."
    Write-Host 'Get a Windows build from https://ffmpeg.org/download.html#build-windows, for example'
    Write-Host 'ffmpeg-release-essentials.zip from https://www.gyan.dev/ffmpeg/builds/, unzip it, then run again with'
    Write-Host '  -Ffmpeg <unzipped folder>\bin\ffmpeg.exe'
    exit 2
}
if (-not $Out) { $Out = Join-Path ([Environment]::GetFolderPath('MyVideos')) ('oriel-demo-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.mkv') }
# x264's fastest preset keeps up with a 4K screen on the CPU. Matroska stays playable if the recording is cut off.
$ffArgs = '-hide_banner', '-f', 'gdigrab', '-framerate', $Fps, '-draw_mouse', '1', '-i', 'desktop', '-t', $Seconds,
    '-c:v', 'libx264', '-preset', 'ultrafast', '-crf', '18', '-pix_fmt', 'yuv420p', $Out
if ($DryRun) { "`"$Ffmpeg`" $($ffArgs -join ' ')"; exit 0 }
"recording $Seconds s of the screen to $Out (press q here to stop early)"
& $Ffmpeg @ffArgs
if ($LASTEXITCODE -ne 0) { throw "ffmpeg failed ($LASTEXITCODE)" }
# gdigrab drops frames when it can't keep up, which shows as a jerky video: count what was recorded.
$ffprobe = Join-Path (Split-Path $Ffmpeg) 'ffprobe.exe'
if (Test-Path -LiteralPath $ffprobe) {
    $frames = [int](& $ffprobe -v error -select_streams v:0 -count_packets -show_entries stream=nb_read_packets -of csv=p=0 $Out)
    $length = [double](& $ffprobe -v error -show_entries format=duration -of csv=p=0 $Out)
    $rate = [math]::Round($frames / $length, 1)
    "$frames frames in $([math]::Round($length, 1)) s: $rate frames a second (asked for $Fps)"
    if ($rate -lt 0.9 * $Fps) { "the capture didn't keep up: record again with -Fps 24, or close what is busy on the PC" }
}
"saved $Out"
