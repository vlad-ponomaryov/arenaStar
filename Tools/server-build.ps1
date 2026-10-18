# Builds the dedicated server outside Unity (Server-Spec 5.2) into Tools/out/server/Arena.Server.dll.
#   Sdk       - dotnet build Server/Arena.Server (needs the .NET SDK, Q-012).
#   Bootstrap - Roslyn (csc.dll) and .NET 6 bundled with the Unity editor: no SDK, no downloads.
#   Auto      - Sdk if a .NET SDK is installed, otherwise Bootstrap.
# Writes Tools/out/server/host.txt (the dotnet.exe that runs the dll). Last line: SERVER BUILD: OK|FAILED ...
# Exit: 0 ok, 2 build failed. Windows PowerShell 5.1 compatible, ASCII only.
param(
    [ValidateSet('Auto', 'Sdk', 'Bootstrap')][string]$Mode = 'Auto'
)
. "$PSScriptRoot\UnityCommon.ps1"

$serverOut = Join-Path $OutDir 'server'
$hostFile = Join-Path $serverOut 'host.txt'
$dllPath = Join-Path $serverOut 'Arena.Server.dll'
$started = Get-Date

# Source folders compiled into the server. Keep in sync with Server/Arena.Shared/Arena.Shared.csproj (+ Server/Arena.Server).
$sourceDirs = @(
    'Assets\_Project\Scripts\Core',
    'Assets\_Project\Scripts\Bots',
    'Assets\_Project\Scripts\Net',
    'Server\Arena.Server'
)

function Test-ArenaDotnetSdk {
    $cmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -eq $cmd) { return $false }
    $old = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $sdks = @(& $cmd.Source --list-sdks 2>$null)
    $ErrorActionPreference = $old
    return ($sdks.Count -gt 0 -and -not [string]::IsNullOrEmpty($sdks[0]))
}

function Write-ArenaBuildErrors([string[]]$Lines) {
    $errs = @($Lines | Where-Object { $_ -match 'error CS\d{4}' } | Select-Object -Unique)
    $errs | Select-Object -First 20 | ForEach-Object { Write-Host $_ }
    return $errs.Count
}

if ($Mode -eq 'Auto') {
    if (Test-ArenaDotnetSdk) { $Mode = 'Sdk' } else { $Mode = 'Bootstrap' }
}
if (-not (Test-Path $serverOut)) { New-Item -ItemType Directory -Force -Path $serverOut | Out-Null }
if (Test-Path $dllPath) { Remove-Item -Force $dllPath }
if (Test-Path $hostFile) { Remove-Item -Force $hostFile }

if ($Mode -eq 'Sdk') {
    if (-not (Test-ArenaDotnetSdk)) { Write-Host 'SERVER BUILD: FAILED, .NET SDK not found (use -Mode Bootstrap)'; exit 2 }
    $dotnet = (Get-Command dotnet).Source
    $proj = Join-Path $ProjectPath 'Server\Arena.Server\Arena.Server.csproj'
    $old = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $output = @(& $dotnet build $proj -c Release -o $serverOut -nologo 2>&1 | ForEach-Object { "$_" })
    $code = $LASTEXITCODE
    $ErrorActionPreference = $old
    $output | Set-Content -Path (Join-Path $OutDir 'server-build.log') -Encoding UTF8
    $n = Write-ArenaBuildErrors $output
    if ($code -ne 0 -or -not (Test-Path $dllPath)) {
        if ($n -eq 0) { $output | Select-Object -Last 15 | ForEach-Object { Write-Host $_ } }
        Write-Host ("SERVER BUILD: FAILED (sdk), CS errors: {0}" -f $n)
        exit 2
    }
    Set-Content -Path $hostFile -Value $dotnet -Encoding ASCII
} else {
    $unity = Get-ArenaUnityPath
    $data = Join-Path (Split-Path $unity) 'Data'
    $dotnet = Join-Path $data 'NetCoreRuntime\dotnet.exe'
    $csc = Join-Path $data 'DotNetSdkRoslyn\csc.dll'
    $fwRoot = Join-Path $data 'NetCoreRuntime\shared\Microsoft.NETCore.App'
    if (-not (Test-Path $dotnet) -or -not (Test-Path $csc) -or -not (Test-Path $fwRoot)) {
        Write-Host "SERVER BUILD: FAILED, Unity's bundled .NET/Roslyn not found under $data (set ARENA_UNITY)"
        exit 2
    }
    $fw = Get-ChildItem -Path $fwRoot -Directory | Sort-Object { [version]($_.Name -replace '[^0-9.].*$', '') } | Select-Object -Last 1
    $fwVersion = $fw.Name

    # References: managed assemblies of the framework only; native dlls (coreclr, msquic, ...) would give CS0009.
    $refs = @()
    foreach ($f in (Get-ChildItem -Path $fw.FullName -Filter '*.dll')) {
        try {
            [void][System.Reflection.AssemblyName]::GetAssemblyName($f.FullName)
            $refs += $f.FullName
        } catch { }
    }
    $sources = @()
    foreach ($d in $sourceDirs) {
        $full = Join-Path $ProjectPath $d
        $sources += @(Get-ChildItem -Path $full -Recurse -Filter '*.cs' |
            Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' } | ForEach-Object { $_.FullName })
    }

    $rsp = Join-Path $serverOut 'bootstrap.rsp'
    $lines = @('-nologo', '-preferreduilang:en-US', '-langversion:9.0', '-nostdlib', '-optimize+',
        '-deterministic', '-target:exe', ('-out:"' + $dllPath + '"'))
    foreach ($r in $refs) { $lines += ('-r:"' + $r + '"') }
    foreach ($s in $sources) { $lines += ('"' + $s + '"') }
    [System.IO.File]::WriteAllLines($rsp, [string[]]$lines, (New-Object System.Text.UTF8Encoding($false)))

    $old = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $output = @(& $dotnet $csc -noconfig "@$rsp" 2>&1 | ForEach-Object { "$_" })
    $code = $LASTEXITCODE
    $ErrorActionPreference = $old
    $output | Set-Content -Path (Join-Path $OutDir 'server-build.log') -Encoding UTF8
    $n = Write-ArenaBuildErrors $output
    if ($code -ne 0 -or -not (Test-Path $dllPath)) {
        if ($n -eq 0) { $output | Select-Object -Last 15 | ForEach-Object { Write-Host $_ } }
        Write-Host ("SERVER BUILD: FAILED (bootstrap), CS errors: {0}" -f $n)
        exit 2
    }
    $warnings = @($output | Where-Object { $_ -match 'warning CS\d{4}' } | Select-Object -Unique).Count
    if ($warnings -gt 0) { Write-Host ("warnings: {0} (see Tools/out/server-build.log)" -f $warnings) }

    # rollForward Major: the same dll runs on any installed .NET >= 6 (e.g. on a VPS), Server-Spec 5.2.
    $runtimeConfig = @"
{
  "runtimeOptions": {
    "tfm": "net6.0",
    "rollForward": "Major",
    "framework": { "name": "Microsoft.NETCore.App", "version": "$fwVersion" },
    "configProperties": { "System.Globalization.Invariant": true }
  }
}
"@
    [System.IO.File]::WriteAllText((Join-Path $serverOut 'Arena.Server.runtimeconfig.json'), $runtimeConfig,
        (New-Object System.Text.UTF8Encoding($false)))
    Set-Content -Path $hostFile -Value $dotnet -Encoding ASCII
    Write-Host ("bootstrap: {0} sources, {1} references, .NET {2}" -f $sources.Count, $refs.Count, $fwVersion)
}

$size = [math]::Round((Get-Item $dllPath).Length / 1KB, 1)
$secs = [math]::Round(((Get-Date) - $started).TotalSeconds, 1)
Write-Host ([string]::Format([System.Globalization.CultureInfo]::InvariantCulture, 'SERVER BUILD: OK ({0}), {1}, {2} KB, {3} s',
    $Mode.ToLowerInvariant(), $dllPath, $size, $secs))
exit 0
