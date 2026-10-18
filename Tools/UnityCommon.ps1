# Shared helpers for Arena Unity batchmode scripts. Dot-source: . "$PSScriptRoot\UnityCommon.ps1"
# Windows PowerShell 5.1 compatible (no &&, ?:, ??). ASCII only.

$ErrorActionPreference = 'Stop'

$script:ArenaDefaultUnity = 'C:\Unity\Hub\Editor\6000.3.26f1\Editor\Unity.exe'
$ProjectPath = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path.TrimEnd('\')
$OutDir = Join-Path $ProjectPath 'Tools\out'
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Force -Path $OutDir | Out-Null }

function Get-ArenaUnityPath {
    $p = $env:ARENA_UNITY
    if ([string]::IsNullOrEmpty($p)) { $p = $script:ArenaDefaultUnity }
    return $p
}

function ConvertTo-ArenaNormalizedPath([string]$Path) {
    if ($null -eq $Path) { return '' }
    return $Path.Replace('/', '\').TrimEnd('\').ToLowerInvariant()
}

# Returns $true if a Unity.exe process has this project open. Removes a stale Temp\UnityLockfile otherwise.
function Test-ArenaUnityRunning {
    $needle = ConvertTo-ArenaNormalizedPath $ProjectPath
    $procs = @(Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" -ErrorAction SilentlyContinue)
    foreach ($proc in $procs) {
        $cmd = ConvertTo-ArenaNormalizedPath $proc.CommandLine
        if ($cmd.Contains($needle)) { return $true }
    }
    $lock = Join-Path $ProjectPath 'Temp\UnityLockfile'
    if (Test-Path $lock) {
        try { Remove-Item -Force $lock -ErrorAction Stop; Write-Host "Removed stale $lock" } catch { Write-Host "WARN: cannot remove $lock : $_" }
    }
    return $false
}

function ConvertTo-ArenaArgString([string[]]$ArgList) {
    $parts = @()
    foreach ($a in $ArgList) {
        if ($a -match '[\s"]') { $parts += '"' + $a.Replace('"', '\"') + '"' } else { $parts += $a }
    }
    return ($parts -join ' ')
}

# Runs Unity in batchmode. Returns Unity's exit code, or 2 on timeout / start failure.
# Always adds: -batchmode -nographics -projectPath <ProjectPath> -buildTarget Android -logFile <LogFile>
function Invoke-ArenaUnity {
    param(
        [string[]]$UnityArgs = @(),
        [Parameter(Mandatory = $true)][string]$LogFile,
        [int]$TimeoutMinutes = 15,
        [switch]$NoProject
    )
    $unity = Get-ArenaUnityPath
    if (-not (Test-Path $unity)) { Write-Host "ERROR: Unity not found at $unity (set ARENA_UNITY)"; return 2 }
    if (Test-Path $LogFile) { Remove-Item -Force $LogFile }
    $all = @('-batchmode', '-nographics')
    if (-not $NoProject) { $all += @('-projectPath', $ProjectPath) }
    $all += @('-buildTarget', 'Android', '-logFile', $LogFile)
    $all += $UnityArgs
    $argString = ConvertTo-ArenaArgString $all
    Write-Host "Unity: $unity $argString"
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        $p = Start-Process -FilePath $unity -ArgumentList $argString -PassThru -NoNewWindow
    } catch {
        Write-Host "ERROR: failed to start Unity: $_"; return 2
    }
    $null = $p.Handle  # needed so ExitCode is available in PS 5.1
    $ms = [int]([Math]::Min([double]$TimeoutMinutes * 60000, [double][int]::MaxValue))
    if (-not $p.WaitForExit($ms)) {
        Write-Host "ERROR: Unity timed out after $TimeoutMinutes min, killing"
        try { Stop-Process -Id $p.Id -Force -ErrorAction Stop } catch { }
        Start-Sleep -Seconds 2
        return 2
    }
    $p.WaitForExit()
    $code = $p.ExitCode
    if ($null -eq $code) { $code = 0 }
    Write-Host ("Unity exited with code {0} in {1:N0} s" -f $code, $sw.Elapsed.TotalSeconds)
    return [int]$code
}

# Unique 'error CS####' lines from the log (plus a marker if Unity reports compiler errors without CS lines).
function Get-ArenaCsErrors {
    param([Parameter(Mandatory = $true)][string]$LogFile)
    $result = New-Object System.Collections.Generic.List[string]
    if (-not (Test-Path $LogFile)) { return ,$result }
    $seen = @{}
    foreach ($line in (Get-Content -Path $LogFile -ErrorAction SilentlyContinue)) {
        if ($line -match 'error CS\d{4}') {
            $t = $line.Trim()
            if (-not $seen.ContainsKey($t)) { $seen[$t] = $true; $result.Add($t) }
        }
    }
    if ($result.Count -eq 0) {
        $hit = Select-String -Path $LogFile -Pattern 'Scripts have compiler errors' -SimpleMatch -Quiet
        if ($hit) { $result.Add('Scripts have compiler errors (see log)') }
    }
    return ,$result
}

# Returns a human readable license problem string, or $null.
function Test-ArenaLicenseError {
    param([Parameter(Mandatory = $true)][string]$LogFile)
    if (-not (Test-Path $LogFile)) { return $null }
    $m = Select-String -Path $LogFile -Pattern 'No valid Unity Editor license', 'No ULF license found', 'License is not active', 'Licensing.*[Ee]rror.*(entitlement|no valid)' -List
    if ($m) { return ("LICENSE: Unity license problem: " + $m[0].Line.Trim() + " -> activate Personal license in Unity Hub") }
    $m2 = Select-String -Path $LogFile -Pattern 'license' -AllMatches | Where-Object { $_.Line -match '(?i)error' -and $_.Line -notmatch '(?i)licensing client|LicensingClient has' }
    if ($m2) { return ("LICENSE: possible license error: " + @($m2)[0].Line.Trim()) }
    return $null
}

function Write-ArenaTail {
    param([string]$LogFile, [int]$Lines = 30)
    if (Test-Path $LogFile) {
        Write-Host "---- last $Lines lines of $LogFile ----"
        Get-Content -Path $LogFile -Tail $Lines | ForEach-Object { Write-Host $_ }
        Write-Host "----"
    }
}
