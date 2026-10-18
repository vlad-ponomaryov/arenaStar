# Builds the server (Tools/server-build.ps1) and runs its console self-test (Arena.Server --selftest), Server-Spec 11.2.
# Last line: SERVER TEST: OK ... | SERVER TEST: FAILED ...
# Exit: 0 ok, 1 self-test failed, 2 build failed. Windows PowerShell 5.1 compatible, ASCII only.
param(
    [ValidateSet('Auto', 'Sdk', 'Bootstrap')][string]$Mode = 'Auto',
    [int]$TimeoutSeconds = 300
)
. "$PSScriptRoot\UnityCommon.ps1"

& (Join-Path $PSScriptRoot 'server-build.ps1') -Mode $Mode
if ($LASTEXITCODE -ne 0) { Write-Host 'SERVER TEST: FAILED, build failed'; exit 2 }

$serverOut = Join-Path $OutDir 'server'
$dll = Join-Path $serverOut 'Arena.Server.dll'
$dotnet = (Get-Content -Path (Join-Path $serverOut 'host.txt') -TotalCount 1).Trim()
$log = Join-Path $OutDir 'server-selftest.log'
$errLog = Join-Path $OutDir 'server-selftest.err.log'

$p = Start-Process -FilePath $dotnet -ArgumentList @(('"' + $dll + '"'), '--selftest') -NoNewWindow -PassThru `
    -RedirectStandardOutput $log -RedirectStandardError $errLog -WorkingDirectory $serverOut
$null = $p.Handle  # PS 5.1: without touching Handle, ExitCode of a -NoNewWindow process stays empty
if (-not $p.WaitForExit($TimeoutSeconds * 1000)) {
    try { $p.Kill() } catch { }
    Write-Host "SERVER TEST: FAILED, self-test timed out after $TimeoutSeconds s"
    exit 1
}
$p.WaitForExit()
$code = $p.ExitCode
Get-Content -Path $log | ForEach-Object { Write-Host $_ }
if ((Test-Path $errLog) -and (Get-Item $errLog).Length -gt 0) { Get-Content -Path $errLog | ForEach-Object { Write-Host $_ } }

$summary = @(Get-Content -Path $log | Where-Object { $_ -match '^SELFTEST: ' } | Select-Object -Last 1)
if ($code -eq 0 -and $summary.Count -gt 0 -and $summary[0] -match '^SELFTEST: OK') {
    Write-Host ('SERVER TEST: OK, ' + ($summary[0] -replace '^SELFTEST: OK, ', ''))
    exit 0
}
Write-Host "SERVER TEST: FAILED (exit $code), log: $log"
exit 1
