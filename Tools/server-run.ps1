# Builds (Tools/server-build.ps1) and runs the dedicated server in this console (Server-Spec 10.1): UDP 7777, metrics on
# http://127.0.0.1:7778/metrics. Ctrl+C drains (running matches play on up to DrainSeconds), a second Ctrl+C stops at once.
#   Tools\server-run.ps1                       # config: Server/config/server.json (copied next to the dll)
#   Tools\server-run.ps1 -Config my.json -NoBuild
# The first run on a PC may show a Windows firewall prompt; the rule for phones in the LAN is in Server-Spec 10.1 (a person adds it).
# Exit: the server's exit code (0 ok, 3 content pack, 4 config or port busy, 5 crash), 2 build failed. PowerShell 5.1, ASCII only.
param(
    [string]$Config = '',
    [switch]$NoBuild,
    [ValidateSet('Auto', 'Sdk', 'Bootstrap')][string]$Mode = 'Auto'
)
. "$PSScriptRoot\UnityCommon.ps1"

if (-not $NoBuild) {
    & (Join-Path $PSScriptRoot 'server-build.ps1') -Mode $Mode
    if ($LASTEXITCODE -ne 0) { Write-Host 'SERVER RUN: FAILED, build failed'; exit 2 }
}
$serverOut = Join-Path $OutDir 'server'
$dll = Join-Path $serverOut 'Arena.Server.dll'
$hostFile = Join-Path $serverOut 'host.txt'
if (-not (Test-Path $dll) -or -not (Test-Path $hostFile)) { Write-Host 'SERVER RUN: FAILED, no build in Tools/out/server (run without -NoBuild)'; exit 2 }
$dotnet = (Get-Content -Path $hostFile -TotalCount 1).Trim()

$argList = @($dll)
if ($Config -ne '') { $argList += @('--config', (Resolve-Path $Config).Path) }
Write-Host ('SERVER RUN: ' + $dotnet + ' ' + ($argList -join ' '))
Push-Location $serverOut
try {
    & $dotnet @argList
    $code = $LASTEXITCODE
} finally {
    Pop-Location
}
Write-Host ('SERVER RUN: exited with code ' + $code)
exit $code
