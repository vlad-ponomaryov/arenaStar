# Turns the frames of AnimCapture (Tools/out/art/<Out>/frames/f_000.png ...) into <Out>/<Name>.gif with ffmpeg (palette pass).
# Windows PowerShell 5.1. Exit: 0 ok, 1 ffmpeg failed, 2 no frames / no ffmpeg.
param(
    [string]$Out = 'theo-anim',
    [string]$Name = 'theo_anim',
    [int]$Width = 480,
    [int]$Fps = 30,
    [int]$Colors = 96,
    # bayer (smoother gradients, bigger file) or none (flat colours, smaller file)
    [string]$Dither = 'none'
)

$root = Split-Path -Parent $PSScriptRoot
$dir = Join-Path $root ("Tools\out\art\" + $Out)
$frames = Join-Path $dir 'frames'
if (-not (Test-Path (Join-Path $frames 'f_000.png'))) { Write-Output "GIF: no frames in $frames"; exit 2 }

$ffmpeg = (Get-Command ffmpeg -ErrorAction SilentlyContinue)
if ($null -eq $ffmpeg) {
    $link = Join-Path $env:LOCALAPPDATA 'Microsoft\WinGet\Links\ffmpeg.exe'
    if (Test-Path $link) { $ffmpegPath = $link } else { Write-Output 'GIF: ffmpeg not found'; exit 2 }
} else { $ffmpegPath = $ffmpeg.Source }

$gif = Join-Path $dir ($Name + '.gif')
$filter = "scale=${Width}:-1:flags=lanczos,split[a][b];[a]palettegen=max_colors=${Colors}:stats_mode=diff[p];[b][p]paletteuse=dither=${Dither}:diff_mode=rectangle"
& $ffmpegPath -y -loglevel error -framerate $Fps -i (Join-Path $frames 'f_%03d.png') -vf $filter $gif
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $gif)) { Write-Output 'GIF: ffmpeg failed'; exit 1 }
$mb = [math]::Round((Get-Item $gif).Length / 1MB, 2)
Write-Output "GIF: $gif ($mb MB)"
exit 0
