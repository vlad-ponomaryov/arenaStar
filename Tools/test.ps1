# Runs Unity tests in batchmode (-runTests, no -quit) and parses the NUnit XML.
# -Category: Unity -testCategory (';'-separated, '!' excludes). Default '!Long;!Perf;!Capture' leaves out the long and load tests
# (Server-Spec 11.1, D-106) and the screenshot tests (Art-Spec 6.5): run them with -Category Long / Perf / Capture; -Category all
# runs every test. -Graphics: run with a GPU device (no -nographics), needed by the Capture tests.
# Exit: 0 all green (or none found), 1 failures, 2 compile errors / no XML / Unity failure, 3 Unity already running.
param(
    [string]$Filter = '',
    [string]$Category = '!Long;!Perf;!Capture',
    [ValidateSet('EditMode', 'PlayMode')][string]$Platform = 'EditMode',
    [int]$TimeoutMinutes = 20,
    [switch]$Graphics
)
. "$PSScriptRoot\UnityCommon.ps1"

if (Test-ArenaUnityRunning) { Write-Host 'TESTS: Unity is already running for this project'; exit 3 }
$log = Join-Path $OutDir 'test.log'
$xmlPath = Join-Path $OutDir 'test-results.xml'
if (Test-Path $xmlPath) { Remove-Item -Force $xmlPath }

$uargs = @('-runTests', '-testPlatform', $Platform, '-testResults', $xmlPath)
if (-not [string]::IsNullOrEmpty($Filter)) { $uargs += @('-testFilter', $Filter) }
if (-not [string]::IsNullOrEmpty($Category) -and $Category -ne 'all') { $uargs += @('-testCategory', $Category) }
$code = Invoke-ArenaUnity -UnityArgs $uargs -LogFile $log -TimeoutMinutes $TimeoutMinutes -Graphics:$Graphics

if (-not (Test-Path $xmlPath)) {
    $errors = Get-ArenaCsErrors -LogFile $log
    if ($errors.Count -gt 0) {
        $errors | Select-Object -First 20 | ForEach-Object { Write-Host $_ }
        Write-Host ("TESTS: compile errors, CS/shader errors: {0}" -f $errors.Count)
        exit 2
    }
    if ($code -eq 0 -and (Select-String -Path $log -Pattern 'No tests? (were )?found|0 tests' -Quiet)) {
        Write-Host 'TESTS: none found'
        exit 0
    }
    $lic = Test-ArenaLicenseError -LogFile $log
    if ($lic) { Write-Host $lic }
    Write-ArenaTail -LogFile $log
    Write-Host "TESTS: no results XML (Unity exit $code), log: $log"
    exit 2
}

[xml]$xml = Get-Content -Path $xmlPath -Raw
$run = $xml.SelectSingleNode('/test-run')
if ($null -eq $run) { Write-Host "TESTS: malformed XML $xmlPath"; exit 2 }
$total = [int]$run.GetAttribute('total')
$passed = [int]$run.GetAttribute('passed')
$failed = [int]$run.GetAttribute('failed')
$skipped = [int]$run.GetAttribute('skipped')

$failedCases = $xml.SelectNodes("//test-case[@result='Failed']")
foreach ($tc in $failedCases) {
    $msgNode = $tc.SelectSingleNode('failure/message')
    $msg = ''
    if ($null -ne $msgNode) { $msg = ($msgNode.InnerText -split "`n")[0].Trim() }
    Write-Host ("FAILED: {0} :: {1}" -f $tc.GetAttribute('fullname'), $msg)
}

if ($total -eq 0) {
    Write-Host 'TESTS: none found (passed 0, failed 0, skipped 0)'
    exit 0
}
Write-Host ("TESTS: passed {0}, failed {1}, skipped {2}" -f $passed, $failed, $skipped)
if ($failed -gt 0 -or $failedCases.Count -gt 0) { exit 1 }
exit 0
