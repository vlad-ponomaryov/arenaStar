# Install the latest built APK on an Android phone via adb and launch the game. Unity is not needed
# (except with -Build). See Docs/Phone-Testing.md.
#   phone.ps1                         install newest Builds/Android/*.apk, clear old logs, launch
#   phone.ps1 -Build [-Dev]           build first (build-android.ps1); if Unity is busy (code 3) - install the last APK
#   phone.ps1 -Dev                    prefer Arena-dev.apk over Arena.apk
#   phone.ps1 -Log                    save the phone's game log to Tools/out/phone-log.txt (no install)
#   phone.ps1 -Watch                  live game log in the console and Tools/out/phone-log.txt (Ctrl+C to stop)
#   phone.ps1 -Pair IP:PORT -Code N   Wi-Fi pairing (Android 11+, "Pair device with pairing code")
#   phone.ps1 -Connect IP:PORT        Wi-Fi connect; the address is remembered and reused automatically
#   phone.ps1 -WifiFromUsb            Android 10: phone on USB -> switch to Wi-Fi (adb tcpip 5555) and remember it
#   phone.ps1 -Reinstall             uninstall the game first (fixes INSTALL_FAILED_UPDATE_INCOMPATIBLE; wipes its data)
#   phone.ps1 -Apk <path> / -NoLaunch
# Exit: 0 ok, 1 install/launch/adb failed, 2 no phone or no APK, 3 build failed.
param(
    [switch]$Build,
    [switch]$Dev,
    [switch]$Log,
    [switch]$Watch,
    [string]$Pair,
    [string]$Code,
    [string]$Connect,
    [switch]$WifiFromUsb,
    [switch]$Reinstall,
    [switch]$NoLaunch,
    [string]$Apk
)
. "$PSScriptRoot\UnityCommon.ps1"
$ErrorActionPreference = 'Continue'

$PackageId = 'com.arena.prototype'
$LogTags = @('Unity:V', 'AndroidRuntime:E', 'DEBUG:V', 'CRASH:V', 'libc:F', '*:S')
$LogFile = Join-Path $OutDir 'phone-log.txt'
$WifiFile = Join-Path $OutDir 'phone-wifi.txt'

function Get-Adb {
    $unityDir = Split-Path (Get-ArenaUnityPath)
    $p = Join-Path $unityDir 'Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe'
    if (Test-Path $p) { return $p }
    $cmd = Get-Command adb.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    return $null
}

# Returns @{ Serial; State } for each line of 'adb devices'.
function Get-Devices {
    $list = @()
    foreach ($line in @(& $script:Adb devices | Select-Object -Skip 1)) {
        $parts = $line -split '\s+'
        if ($parts.Count -ge 2 -and $parts[0] -ne '') { $list += @{ Serial = $parts[0]; State = $parts[1] } }
    }
    return $list
}

function Get-Phone {
    $devs = @(Get-Devices)
    if ($devs.Count -eq 0 -and (Test-Path $WifiFile)) {
        $addr = (Get-Content $WifiFile -TotalCount 1).Trim()
        if ($addr) {
            Write-Host "PHONE: no device, trying Wi-Fi $addr"
            & $script:Adb connect $addr | Out-Host
            Start-Sleep -Seconds 1
            $devs = @(Get-Devices)
        }
    }
    if (@($devs | Where-Object { $_.State -eq 'device' }).Count -eq 0) {
        # The Wireless debugging port changes after a reboot / toggle: find the paired phone on the network (mDNS).
        $svc = (& $script:Adb mdns services | Out-String)
        if ($svc -match '_adb-tls-connect\._tcp\s+(\d+\.\d+\.\d+\.\d+:\d+)') {
            $addr = $Matches[1]
            Write-Host "PHONE: found on Wi-Fi at $addr"
            & $script:Adb connect $addr | Out-Host
            Start-Sleep -Seconds 1
            Set-Content -Path $WifiFile -Value $addr -Encoding ASCII
            $devs = @(Get-Devices)
        }
    }
    $ok = @($devs | Where-Object { $_.State -eq 'device' })
    if ($ok.Count -gt 0) {
        # Prefer USB (serial without ':') when the same phone is also connected over Wi-Fi.
        $usb = @($ok | Where-Object { $_.Serial -notmatch ':' })
        if ($usb.Count -gt 0) { return $usb[0].Serial }
        return $ok[0].Serial
    }
    if (@($devs | Where-Object { $_.State -eq 'unauthorized' }).Count -gt 0) {
        Write-Host 'PHONE: unauthorized - unlock the phone and tap "Allow" in the "Allow USB debugging?" dialog, then run again'
    } elseif (@($devs | Where-Object { $_.State -eq 'offline' }).Count -gt 0) {
        Write-Host 'PHONE: offline - replug the cable (or toggle USB/Wireless debugging off and on), then run again'
    } else {
        Write-Host 'PHONE: no phone found - check the cable / USB debugging, or use -Connect IP:PORT for Wi-Fi'
    }
    return $null
}

function Find-Apk {
    $dir = Join-Path $ProjectPath 'Builds\Android'
    $names = @('Arena.apk', 'Arena-dev.apk')
    if ($Dev) { $names = @('Arena-dev.apk', 'Arena.apk') }
    $files = @($names | ForEach-Object { Join-Path $dir $_ } | Where-Object { Test-Path $_ } | ForEach-Object { Get-Item $_ })
    if ($files.Count -eq 0) { return $null }
    if ($Dev) { return $files[0].FullName }
    return ($files | Sort-Object LastWriteTime -Descending | Select-Object -First 1).FullName
}

function Save-Log([string]$Serial) {
    $p = Start-Process -FilePath $script:Adb -ArgumentList (@('-s', $Serial, 'logcat', '-d', '-v', 'time') + $LogTags) `
        -NoNewWindow -Wait -PassThru -RedirectStandardOutput $LogFile
    $lines = @(Get-Content $LogFile).Count
    $errs = @(Select-String -Path $LogFile -Pattern '(?i)exception|error|crash|fatal').Count
    Write-Host ("LOG: {0} ({1} lines, {2} with error/exception)" -f $LogFile, $lines, $errs)
    return $p.ExitCode
}

$script:Adb = Get-Adb
if (-not $script:Adb) { Write-Host 'PHONE: adb.exe not found (Unity Android SDK platform-tools or PATH)'; exit 1 }

# --- Wi-Fi pairing / connecting ---
if ($Pair) {
    if (-not $Code) { Write-Host 'PAIR: add -Code <6 digits from the phone>'; exit 1 }
    $out = (& $script:Adb pair $Pair $Code) | Out-String
    Write-Host $out.Trim()
    if ($out -notmatch 'Successfully paired') { Write-Host 'PAIR: FAILED (code expires fast - open the pairing dialog again)'; exit 1 }
    if (-not $Connect) {
        Write-Host 'PAIR: OK. Now run: phone.ps1 (finds the phone itself) or phone.ps1 -Connect IP:PORT from the "Wireless debugging" screen'
        exit 0
    }
    Write-Host 'PAIR: OK'
}
if ($WifiFromUsb) {
    # Android 10 and older: switch a USB-connected phone to adb over Wi-Fi (port 5555), then the cable can go.
    $serial = Get-Phone
    if (-not $serial) { exit 2 }
    $ipLine = (& $script:Adb -s $serial shell ip -f inet addr show wlan0 | Out-String)
    if ($ipLine -notmatch 'inet (\d+\.\d+\.\d+\.\d+)') { Write-Host 'WIFI: phone has no Wi-Fi address - turn Wi-Fi on'; exit 1 }
    $Connect = $Matches[1] + ':5555'
    & $script:Adb -s $serial tcpip 5555 | Out-Host
    Start-Sleep -Seconds 2
}
if ($Connect) {
    $out = (& $script:Adb connect $Connect) | Out-String
    Write-Host $out.Trim()
    if ($out -notmatch 'connected to') { Write-Host 'CONNECT: FAILED (same Wi-Fi network? Wireless debugging on? port changes after each toggle)'; exit 1 }
    Set-Content -Path $WifiFile -Value $Connect -Encoding ASCII
    Write-Host "CONNECT: OK, remembered $Connect"
    if (-not ($Build -or $Log -or $Watch -or $Apk)) { exit 0 }
}

# --- Logs ---
if ($Log -or $Watch) {
    $serial = Get-Phone
    if (-not $serial) { exit 2 }
    if ($Log) { $null = Save-Log $serial; Write-Host 'PHONE: log saved'; exit 0 }
    Write-Host "WATCH: live log -> $LogFile (Ctrl+C to stop)"
    & $script:Adb -s $serial logcat -v time @LogTags | Tee-Object -FilePath $LogFile
    exit 0
}

# --- Build (optional) ---
if ($Build) {
    $bargs = @{}
    if ($Dev) { $bargs['Development'] = $true }
    & "$PSScriptRoot\build-android.ps1" @bargs
    $bc = $LASTEXITCODE
    if ($bc -eq 3) {
        Write-Host 'BUILD: Unity is busy (another agent) - installing the last built APK instead'
    } elseif ($bc -ne 0) {
        Write-Host "PHONE: build failed (exit $bc), nothing installed"
        exit 3
    }
}

# --- Install + launch ---
if (-not $Apk) { $Apk = Find-Apk }
if (-not $Apk -or -not (Test-Path $Apk)) { Write-Host 'PHONE: no APK in Builds/Android - run with -Build'; exit 2 }
$item = Get-Item $Apk
Write-Host ("APK: {0} ({1:N1} MB, built {2:yyyy-MM-dd HH:mm})" -f $item.Name, ($item.Length / 1MB), $item.LastWriteTime)

$serial = Get-Phone
if (-not $serial) { exit 2 }
$model = (& $script:Adb -s $serial shell getprop ro.product.model | Out-String).Trim()
Write-Host "PHONE: $model ($serial)"

if ($Reinstall) {
    Write-Host 'INSTALL: removing the old version'
    & $script:Adb -s $serial uninstall $PackageId | Out-Host
}
Write-Host 'INSTALL: copying to the phone (confirm on the phone screen if it asks)...'
$out = (& $script:Adb -s $serial install -r $item.FullName 2>&1) | Out-String
Write-Host $out.Trim()
if ($out -notmatch 'Success') {
    if ($out -match 'UPDATE_INCOMPATIBLE|INSTALL_FAILED_VERSION_DOWNGRADE') { Write-Host 'HINT: run again with -Reinstall (old version signed differently)' }
    if ($out -match 'USER_RESTRICTED|INSTALL_FAILED_ABORTED') { Write-Host 'HINT: allow "Install via USB" in Developer options / confirm the dialog on the phone' }
    Write-Host 'PHONE: INSTALL FAILED'
    exit 1
}

if ($NoLaunch) { Write-Host "PHONE: OK installed $($item.Name) on $model (not launched)"; exit 0 }
& $script:Adb -s $serial logcat -c | Out-Null
& $script:Adb -s $serial shell am force-stop $PackageId | Out-Null
& $script:Adb -s $serial shell monkey -p $PackageId -c android.intent.category.LAUNCHER 1 2>$null | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Host 'PHONE: installed, but launch failed - start "Arena" on the phone by hand'; exit 1 }
Write-Host "PHONE: OK installed and launched $($item.Name) on $model. Problem? run: Tools\phone.ps1 -Log"
exit 0
