# One-click actions for desktop shortcuts (created by Tools/make-shortcuts.ps1). Runs in its own console window and waits
# for Enter at the end so the result can be read. See Docs/Phone-Testing.md.
#   -Action Phone        install the newest built APK on the phone and launch it (phone.ps1)
#   -Action PhoneBuild   build a fresh APK first (if Unity is busy - installs the last one), then install and launch
#   -Action PhoneLog     save the game log from the phone to Tools/out/phone-log.txt and open it
#   -Action ServerStart  (re)start the LAN game server in this window (stops a running one first); close window = stop
#   -Action ServerStop   stop the running game server
# PowerShell 5.1, ASCII only.
param(
    [ValidateSet('Phone', 'PhoneBuild', 'PhoneLog', 'ServerStart', 'ServerStop')][string]$Action = 'Phone'
)
. "$PSScriptRoot\UnityCommon.ps1"
$ErrorActionPreference = 'Continue'

function Get-ServerProcesses {
    return @(Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -match 'Arena\.Server\.dll' })
}

function Stop-Server {
    $procs = @(Get-ServerProcesses)
    if ($procs.Count -eq 0) { Write-Host 'SERVER: not running'; return }
    foreach ($p in $procs) {
        Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue
        Write-Host ('SERVER: stopped (pid ' + $p.ProcessId + ')')
    }
    Start-Sleep -Seconds 1
}

function Wait-Enter {
    Write-Host ''
    Read-Host 'Press Enter to close' | Out-Null
}

$host.UI.RawUI.WindowTitle = 'Arena - ' + $Action
switch ($Action) {
    'Phone' {
        & "$PSScriptRoot\phone.ps1"
        Wait-Enter
    }
    'PhoneBuild' {
        Write-Host 'Building a fresh APK (several minutes)...'
        & "$PSScriptRoot\phone.ps1" -Build
        Wait-Enter
    }
    'PhoneLog' {
        & "$PSScriptRoot\phone.ps1" -Log
        if ($LASTEXITCODE -eq 0) { Start-Process notepad.exe (Join-Path $OutDir 'phone-log.txt') }
        Wait-Enter
    }
    'ServerStart' {
        Stop-Server
        if (-not (Get-NetFirewallRule -DisplayName 'Arena server' -ErrorAction SilentlyContinue)) {
            Write-Host 'WARNING: no firewall rule "Arena server" (UDP 7777) - phones may not reach the server. See Docs/Phone-Testing.md'
        }
        Write-Host 'SERVER: starting. Keep this window open while playing; close it (or Ctrl+C) to stop the server.'
        & "$PSScriptRoot\server-run.ps1"
        Wait-Enter
    }
    'ServerStop' {
        Stop-Server
        Start-Sleep -Seconds 2
    }
}
