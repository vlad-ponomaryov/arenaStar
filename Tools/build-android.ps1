# Builds the Android APK: -quit -executeMethod Arena.Editor.Build.BuildScript.BuildAndroid.
# Output: Builds/Android/Arena.apk (Arena-dev.apk with -Development). -Install: adb install -r + launch, if a phone is connected.
# Exit: 0 ok, 1 build failed, 2 Unity failure (license, timeout, crash), 3 Unity already running.
param(
    [switch]$Development,
    [switch]$Install,
    [int]$TimeoutMinutes = 60
)
. "$PSScriptRoot\UnityCommon.ps1"

$PackageId = 'com.arena.prototype'

if (Test-ArenaUnityRunning) { Write-Host 'BUILD: Unity is already running for this project'; exit 3 }
$log = Join-Path $OutDir 'build-android.log'
$uargs = @('-quit', '-executeMethod', 'Arena.Editor.Build.BuildScript.BuildAndroid')
if ($Development) { $uargs += '-arenaDevelopment' }
$code = Invoke-ArenaUnity -UnityArgs $uargs -LogFile $log -TimeoutMinutes $TimeoutMinutes

if (-not (Test-Path $log)) { Write-Host "BUILD: UNITY FAILED (exit $code), no log"; exit 2 }
$errors = Get-ArenaCsErrors -LogFile $log
if ($errors.Count -gt 0) {
    $errors | Select-Object -First 20 | ForEach-Object { Write-Host $_ }
    Write-Host ("BUILD: FAILED, CS errors: {0}" -f $errors.Count)
    exit 1
}

$m = Select-String -Path $log -Pattern 'ARENA_BUILD_RESULT result=(\w+) size=(\d+) seconds=(\d+) path=(.*)$' | Select-Object -Last 1
if (-not $m) {
    $lic = Test-ArenaLicenseError -LogFile $log
    if ($lic) { Write-Host $lic }
    Write-ArenaTail -LogFile $log -Lines 40
    Write-Host "BUILD: UNITY FAILED (exit $code, no ARENA_BUILD_RESULT), log: $log"
    if ($code -eq 2 -or $lic) { exit 2 }
    exit 1
}
$g = $m.Matches[0].Groups
$result = $g[1].Value
$size = [long]$g[2].Value
$seconds = [int]$g[3].Value
$apk = $g[4].Value.Trim()

if ($result -ne 'Succeeded' -or $code -ne 0 -or -not (Test-Path $apk)) {
    $bad = @(Select-String -Path $log -Pattern '(?i)\berror\b' | Where-Object { $_.Line -notmatch '(?i)licens|0 error' } | Select-Object -First 15)
    $bad | ForEach-Object { Write-Host $_.Line.Trim() }
    Write-ArenaTail -LogFile $log -Lines 30
    Write-Host "BUILD: FAILED (result=$result, exit $code), log: $log"
    exit 1
}

$rel = $apk
$root = $ProjectPath.Replace('\', '/') + '/'
if ($rel.StartsWith($root, [System.StringComparison]::OrdinalIgnoreCase)) { $rel = $rel.Substring($root.Length) }
$summary = ('BUILD: OK {0} ({1:N1} MB, {2} s)' -f $rel, ($size / 1MB), $seconds)

if ($Install) {
    $unityDir = Split-Path (Get-ArenaUnityPath)
    $adb = Join-Path $unityDir 'Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe'
    if (-not (Test-Path $adb)) {
        Write-Host "INSTALL: adb not found at $adb"
    } else {
        $devices = @(& $adb devices | Select-Object -Skip 1 | Where-Object { $_ -match '\tdevice$' })
        if ($devices.Count -eq 0) {
            Write-Host 'INSTALL: no Android device connected, skipped'
        } else {
            Write-Host ('INSTALL: device ' + ($devices[0] -split "`t")[0])
            & $adb install -r $apk
            if ($LASTEXITCODE -ne 0) { Write-Host "INSTALL: adb install failed ($LASTEXITCODE)"; Write-Host $summary; exit 1 }
            & $adb shell monkey -p $PackageId 1 | Out-Null
            Write-Host "INSTALL: installed and launched $PackageId"
        }
    }
}

Write-Host $summary
exit 0
