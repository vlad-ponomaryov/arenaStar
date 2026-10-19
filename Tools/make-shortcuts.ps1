# Creates desktop shortcuts for Tools/desk.ps1 actions (install on phone, build+install, phone log, server start/stop).
# Run once: powershell -ExecutionPolicy Bypass -File Tools\make-shortcuts.ps1   (re-run after moving the project).
# PowerShell 5.1. Saved as UTF-8 with BOM (Russian shortcut names).
$ErrorActionPreference = 'Stop'
$desk = Join-Path $PSScriptRoot 'desk.ps1'
$desktop = [Environment]::GetFolderPath('Desktop')
$ps = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$shell32 = Join-Path $env:SystemRoot 'System32\shell32.dll'
$wsh = New-Object -ComObject WScript.Shell

# Name, action, icon index in shell32.dll, description
$items = @(
    @('Arena - на телефон', 'Phone', 41, 'Install the newest built game on the phone and launch it'),
    @('Arena - собрать и на телефон', 'PhoneBuild', 162, 'Build a fresh APK, install it on the phone and launch it'),
    @('Arena - лог телефона', 'PhoneLog', 70, 'Save the game log from the phone and open it'),
    @('Arena - сервер старт', 'ServerStart', 137, 'Start or restart the LAN game server'),
    @('Arena - сервер стоп', 'ServerStop', 131, 'Stop the LAN game server')
)
foreach ($it in $items) {
    $lnk = $wsh.CreateShortcut((Join-Path $desktop ($it[0] + '.lnk')))
    $lnk.TargetPath = $ps
    $lnk.Arguments = ('-NoProfile -ExecutionPolicy Bypass -File "{0}" -Action {1}' -f $desk, $it[1])
    $lnk.WorkingDirectory = (Split-Path $PSScriptRoot)
    $lnk.IconLocation = ('{0},{1}' -f $shell32, $it[2])
    $lnk.Description = $it[3]
    $lnk.Save()
    Write-Host ('SHORTCUT: ' + $it[0])
}
Write-Host ('SHORTCUTS: OK, {0} on {1}' -f $items.Count, $desktop)
