# Run a Blender script headless (spike image-to-3D, Docs/Spike-ImageTo3D.md). Windows PowerShell 5.1.
#   Tools/blender.ps1 -Script Tools/Blender/prepare_fighter.py -ScriptArgs @('Art/Source/3D/pix/trellis2_001_1024.glb','Tools/Blender/fighters/pix.json','Tools/out/spike3d/prep','--tris','4000','--snap')
#   Tools/blender.ps1 -Script Tools/Blender/render_preview.py -ScriptArgs @('Art/Source/3D/pix/pix_game4k_rigged.glb','Tools/out/spike3d/pix.png')
# Blender: $env:ARENA_BLENDER or the newest C:\Tools\Blender\blender-*\blender.exe (portable LTS zip from blender.org).
# Exit codes: 0 ok, 1 script error (Python traceback in the log), 2 Blender missing or timed out.
param(
    [Parameter(Mandatory = $true)][string]$Script,
    [string[]]$ScriptArgs = @(),
    [int]$TimeoutMinutes = 20
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$blender = $env:ARENA_BLENDER
if (-not $blender) {
    $found = Get-ChildItem 'C:\Tools\Blender' -Filter 'blender.exe' -Recurse -ErrorAction SilentlyContinue | Sort-Object FullName -Descending | Select-Object -First 1
    if ($found) { $blender = $found.FullName }
}
if (-not $blender -or -not (Test-Path $blender)) { Write-Output 'BLENDER: not found (set ARENA_BLENDER)'; exit 2 }
$outDir = Join-Path $root 'Tools\out'
New-Item -ItemType Directory -Force $outDir | Out-Null
$log = Join-Path $outDir ('blender-' + [IO.Path]::GetFileNameWithoutExtension($Script) + '.log')
$argList = @('-b', '--factory-startup', '--python', (Join-Path $root $Script), '--') + $ScriptArgs
$quoted = $argList | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }
$p = Start-Process -FilePath $blender -ArgumentList $quoted -WorkingDirectory $root -NoNewWindow -PassThru `
    -RedirectStandardOutput $log -RedirectStandardError ($log + '.err')
if (-not $p.WaitForExit($TimeoutMinutes * 60000)) { $p.Kill(); Write-Output "BLENDER: timeout after $TimeoutMinutes min, log $log"; exit 2 }
$text = (Get-Content $log -Raw) + (Get-Content ($log + '.err') -Raw -ErrorAction SilentlyContinue)
if ($text -match 'Traceback \(most recent call last\)') {
    Write-Output ($text -split "`n" | Select-String -Pattern 'Error|line \d+' | Select-Object -Last 6)
    Write-Output "BLENDER: FAILED, log $log"; exit 1
}
$last = ($text -split "`n" | Where-Object { $_ -match '^\[prepare\] RESULT|^PREVIEW:' } | Select-Object -Last 1)
Write-Output "BLENDER: OK $last"
exit 0
