# Runs an Editor static method: -quit -executeMethod <Method> (or without -quit if -NoQuit: method must call EditorApplication.Exit).
# -Graphics: render with a GPU device (no -nographics), for ArtCapture. -Extra: more Unity arguments, space-separated
# (e.g. -Extra '-artStep A.1a').  Exit: 0 ok, 1 method failed / exception in log, 2 Unity failure, 3 Unity already running.
param(
    [Parameter(Mandatory = $true)][string]$Method,
    [int]$TimeoutMinutes = 15,
    [switch]$NoQuit,
    [switch]$Graphics,
    [string]$Extra = ''
)
. "$PSScriptRoot\UnityCommon.ps1"

if (Test-ArenaUnityRunning) { Write-Host 'METHOD: Unity is already running for this project'; exit 3 }
$log = Join-Path $OutDir ("method-{0}.log" -f $Method)
$uargs = @('-executeMethod', $Method)
if (-not $NoQuit) { $uargs = @('-quit') + $uargs }
if (-not [string]::IsNullOrEmpty($Extra)) { $uargs += @($Extra.Split(' ') | Where-Object { $_ -ne '' }) }
$code = Invoke-ArenaUnity -UnityArgs $uargs -LogFile $log -TimeoutMinutes $TimeoutMinutes -Graphics:$Graphics

if (-not (Test-Path $log)) { Write-Host "METHOD: UNITY FAILED (exit $code), no log"; exit 2 }
$errors = Get-ArenaCsErrors -LogFile $log
if ($errors.Count -gt 0) {
    $errors | Select-Object -First 20 | ForEach-Object { Write-Host $_ }
    Write-Host ("METHOD: FAILED {0}, CS/shader errors: {1}" -f $Method, $errors.Count)
    exit 1
}
$lic = Test-ArenaLicenseError -LogFile $log
if ($code -ne 0 -and $lic) { Write-Host $lic; Write-Host "METHOD: UNITY FAILED (license), log: $log"; exit 2 }

$bad = @(Select-String -Path $log -Pattern 'executeMethod method .* threw exception', 'executeMethod class .* could not be found', 'executeMethod method .* could not be found', 'Exception.*\bArena\.', '^\s*at Arena\.' )
if ($bad.Count -gt 0) {
    $bad | Select-Object -First 10 | ForEach-Object { Write-Host $_.Line.Trim() }
    Write-ArenaTail -LogFile $log
    Write-Host "METHOD: FAILED $Method (exception), log: $log"
    exit 1
}
if ($code -ne 0) {
    Write-ArenaTail -LogFile $log
    Write-Host "METHOD: FAILED $Method (Unity exit $code), log: $log"
    if ($code -eq 2) { exit 2 }
    exit 1
}
Write-Host "METHOD: OK $Method"
exit 0
