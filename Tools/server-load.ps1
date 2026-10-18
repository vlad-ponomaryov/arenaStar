# Load test of the dedicated server (Server-Spec 11.2, S3.15): builds the server and Arena.LoadTest (Tools/server-build.ps1),
# then N matches of light clients over UDP on 127.0.0.1 against the real server started as a child process (ports 17777/17778,
# apart from a server on 7777). Report: server CPU (cores), tick work / lateness p99, memory, traffic, loopback loss.
#   Tools\server-load.ps1                       # 100 Acorns matches (600 clients), 60 s of measurement
#   Tools\server-load.ps1 -MatchCount 20 -Seconds 30 -GameMode lastbeast
# Output: Tools/out/server-load.log (+ Tools/out/server/loadtest/server.log). Last line: LOAD TEST: OK ... | LOAD TEST: FAILED ...
# Exit: 0 within the thresholds, 1 failed, 2 build failed. Windows PowerShell 5.1 compatible, ASCII only.
param(
    [int]$MatchCount = 100,
    [int]$Seconds = 60,
    [ValidateSet('acorns', 'lastbeast')][string]$GameMode = 'acorns',
    [int]$ClientThreads = 0,
    [ValidateSet('Auto', 'Sdk', 'Bootstrap')][string]$Mode = 'Auto',
    [int]$TimeoutSeconds = 900
)
. "$PSScriptRoot\UnityCommon.ps1"

& (Join-Path $PSScriptRoot 'server-build.ps1') -Mode $Mode -Target Server
if ($LASTEXITCODE -ne 0) { Write-Host 'LOAD TEST: FAILED, server build failed'; exit 2 }
& (Join-Path $PSScriptRoot 'server-build.ps1') -Mode $Mode -Target LoadTest
if ($LASTEXITCODE -ne 0) { Write-Host 'LOAD TEST: FAILED, load test build failed'; exit 2 }

$serverOut = Join-Path $OutDir 'server'
$dll = Join-Path $serverOut 'Arena.LoadTest.dll'
$dotnet = (Get-Content -Path (Join-Path $serverOut 'host.txt') -TotalCount 1).Trim()
$log = Join-Path $OutDir 'server-load.log'
$errLog = Join-Path $OutDir 'server-load.err.log'

$argList = @(('"' + $dll + '"'), '--matches', "$MatchCount", '--seconds', "$Seconds", '--mode', $GameMode)
if ($ClientThreads -gt 0) { $argList += @('--client-threads', "$ClientThreads") }
$p = Start-Process -FilePath $dotnet -ArgumentList $argList -NoNewWindow -PassThru `
    -RedirectStandardOutput $log -RedirectStandardError $errLog -WorkingDirectory $serverOut
$null = $p.Handle  # PS 5.1: without touching Handle, ExitCode of a -NoNewWindow process stays empty
if (-not $p.WaitForExit($TimeoutSeconds * 1000)) {
    try { $p.Kill() } catch { }
    Write-Host "LOAD TEST: FAILED, timed out after $TimeoutSeconds s"
    exit 1
}
$p.WaitForExit()
$code = $p.ExitCode
Get-Content -Path $log | ForEach-Object { Write-Host $_ }
if ((Test-Path $errLog) -and (Get-Item $errLog).Length -gt 0) { Get-Content -Path $errLog | ForEach-Object { Write-Host $_ } }

$summary = @(Get-Content -Path $log | Where-Object { $_ -match '^LOADTEST: ' } | Select-Object -Last 1)
if ($code -eq 0 -and $summary.Count -gt 0 -and $summary[0] -match '^LOADTEST: OK') {
    Write-Host ('LOAD TEST: OK, ' + ($summary[0] -replace '^LOADTEST: OK, ', ''))
    exit 0
}
if ($summary.Count -gt 0) { Write-Host ('LOAD TEST: FAILED, ' + ($summary[0] -replace '^LOADTEST: FAILED, ', '')) }
else { Write-Host "LOAD TEST: FAILED (exit $code), log: $log" }
exit 1
