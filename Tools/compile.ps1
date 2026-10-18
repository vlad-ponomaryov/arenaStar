# Opens the project in batchmode with -quit and reports C# compile errors.
# Exit: 0 ok, 1 CS errors, 2 Unity failure (license, timeout, crash), 3 Unity already running.
param([int]$TimeoutMinutes = 15)
. "$PSScriptRoot\UnityCommon.ps1"

if (Test-ArenaUnityRunning) { Write-Host 'COMPILE: Unity is already running for this project'; exit 3 }
$log = Join-Path $OutDir 'compile.log'
$code = Invoke-ArenaUnity -UnityArgs @('-quit') -LogFile $log -TimeoutMinutes $TimeoutMinutes

$errors = Get-ArenaCsErrors -LogFile $log
if ($errors.Count -gt 0) {
    $errors | Select-Object -First 20 | ForEach-Object { Write-Host $_ }
    Write-Host ("COMPILE: FAILED, CS errors: {0}" -f $errors.Count)
    exit 1
}
if ($code -ne 0 -or -not (Test-Path $log)) {
    $lic = Test-ArenaLicenseError -LogFile $log
    if ($lic) { Write-Host $lic }
    Write-ArenaTail -LogFile $log
    Write-Host "COMPILE: UNITY FAILED (exit $code), log: $log"
    exit 2
}
Write-Host 'COMPILE: OK, CS errors: 0'
exit 0
