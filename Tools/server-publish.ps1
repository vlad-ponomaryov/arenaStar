# Packs the dedicated server into Builds/Server/ for another PC or a VPS (Server-Spec 10.1, 10.3):
#   Sdk       - dotnet publish Server/Arena.Server -c Release (portable, needs the .NET SDK, Q-012).
#   Bootstrap - Tools/server-build.ps1 -Mode Bootstrap (Unity's Roslyn + .NET 6, no downloads); runs on any .NET >= 6.
#   Auto      - Sdk if a .NET SDK is installed, otherwise Bootstrap.
# Contents: Arena.Server.dll (+ runtimeconfig), ArenaContent.bytes, config/server.json, deploy/arena-server.service,
# run.cmd / run.sh. Last line: SERVER PUBLISH: OK|FAILED ... Exit: 0 ok, 2 failed. PowerShell 5.1, ASCII only.
param(
    [ValidateSet('Auto', 'Sdk', 'Bootstrap')][string]$Mode = 'Auto'
)
. "$PSScriptRoot\UnityCommon.ps1"

$publish = Join-Path $ProjectPath 'Builds\Server'
$started = Get-Date

function Test-ArenaDotnetSdkPublish {
    $cmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -eq $cmd) { return $false }
    $old = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $sdks = @(& $cmd.Source --list-sdks 2>$null)
    $ErrorActionPreference = $old
    return ($sdks.Count -gt 0 -and -not [string]::IsNullOrEmpty($sdks[0]))
}

if ($Mode -eq 'Auto') {
    if (Test-ArenaDotnetSdkPublish) { $Mode = 'Sdk' } else { $Mode = 'Bootstrap' }
}
if (Test-Path $publish) { Remove-Item -Recurse -Force $publish }
New-Item -ItemType Directory -Force -Path $publish | Out-Null

if ($Mode -eq 'Sdk') {
    $dotnet = (Get-Command dotnet).Source
    $proj = Join-Path $ProjectPath 'Server\Arena.Server\Arena.Server.csproj'
    $old = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $output = @(& $dotnet publish $proj -c Release -o $publish -nologo 2>&1 | ForEach-Object { "$_" })
    $code = $LASTEXITCODE
    $ErrorActionPreference = $old
    $output | Set-Content -Path (Join-Path $OutDir 'server-publish.log') -Encoding UTF8
    if ($code -ne 0 -or -not (Test-Path (Join-Path $publish 'Arena.Server.dll'))) {
        $output | Select-Object -Last 15 | ForEach-Object { Write-Host $_ }
        Write-Host 'SERVER PUBLISH: FAILED (sdk)'
        exit 2
    }
} else {
    & (Join-Path $PSScriptRoot 'server-build.ps1') -Mode Bootstrap
    if ($LASTEXITCODE -ne 0) { Write-Host 'SERVER PUBLISH: FAILED, bootstrap build failed'; exit 2 }
    $serverOut = Join-Path $OutDir 'server'
    foreach ($f in @('Arena.Server.dll', 'Arena.Server.runtimeconfig.json', 'ArenaContent.bytes')) {
        Copy-Item -Path (Join-Path $serverOut $f) -Destination (Join-Path $publish $f) -Force
    }
    Copy-Item -Path (Join-Path $serverOut 'config') -Destination (Join-Path $publish 'config') -Recurse -Force
}

$deploy = Join-Path $publish 'deploy'
New-Item -ItemType Directory -Force -Path $deploy | Out-Null
Copy-Item -Path (Join-Path $ProjectPath 'Server\deploy\arena-server.service') -Destination $deploy -Force
$utf8 = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText((Join-Path $publish 'run.cmd'),
    "@echo off`r`nrem Arena server: needs a .NET runtime >= 6 (dotnet on PATH). Ctrl+C drains, a second Ctrl+C stops.`r`ncd /d %~dp0`r`ndotnet Arena.Server.dll %*`r`n", $utf8)
[System.IO.File]::WriteAllText((Join-Path $publish 'run.sh'),
    "#!/bin/sh`n# Arena server: needs a .NET runtime >= 6 (dotnet on PATH). Ctrl+C / SIGTERM drains.`ncd `"`$(dirname `"`$0`")`"`nexec dotnet Arena.Server.dll `"`$@`"`n", $utf8)

$files = @(Get-ChildItem -Path $publish -Recurse -File)
$size = [math]::Round((($files | Measure-Object -Property Length -Sum).Sum) / 1KB, 1)
$secs = [math]::Round(((Get-Date) - $started).TotalSeconds, 1)
Write-Host ([string]::Format([System.Globalization.CultureInfo]::InvariantCulture, 'SERVER PUBLISH: OK ({0}), {1}, {2} files, {3} KB, {4} s',
    $Mode.ToLowerInvariant(), $publish, $files.Count, $size, $secs))
exit 0
