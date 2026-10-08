<#
.SYNOPSIS  Builds volumetricblackholefx.shaderbundle with Unity 2019.4.18f1 (KSP 1.12's Unity) in batchmode.
.NOTES     Exit codes: 0 success, 1 failure, 2 Unity licence problem. Set UNITY_EXE to point at your Unity.exe.
#>
param(
    [string]$UnityExe = $(if ($env:UNITY_EXE) { $env:UNITY_EXE } else { 'E:\Tools\Unity\2019.4.18f1\Editor\Unity.exe' }),
    [int]$TimeoutMinutes = 30
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'unity\VolumetricBlackholeFxShaders'
$outDir = Join-Path $repo 'out\shaders'

if (-not (Test-Path $UnityExe)) { Write-Host "Unity not found: $UnityExe"; exit 1 }
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$outDir = (Resolve-Path $outDir).Path
$log = Join-Path $outDir 'unity-build.log'
$bundle = Join-Path $outDir 'volumetricblackholefx.shaderbundle'
$contents = "$bundle.contents.txt"
Remove-Item -ErrorAction SilentlyContinue $bundle, $contents, $log

# No -nographics: Unity compiles the D3D11 shader variants against a real device.
$argList = @('-batchmode', '-quit',
    '-projectPath', "`"$project`"",
    '-executeMethod', 'VolumetricBlackholeFx.Build.BuildBundles.BuildAll',
    '-out', "`"$outDir`"",
    '-logFile', "`"$log`"")
Write-Host "Running Unity (timeout $TimeoutMinutes min)..."
$p = Start-Process -FilePath $UnityExe -ArgumentList $argList -PassThru
$timedOut = $false
if (-not $p.WaitForExit($TimeoutMinutes * 60 * 1000)) {
    try { $p.Kill() } catch {}
    Write-Host "Unity timed out after $TimeoutMinutes minutes."
    $timedOut = $true
}
$code = -1
if (-not $timedOut) { $code = $p.ExitCode }

if ((-not $timedOut) -and ($code -eq 0) -and (Test-Path $bundle)) {
    Write-Host "Shader bundle OK: $bundle ($((Get-Item $bundle).Length) bytes)"
    if (Test-Path $contents) { Get-Content $contents }
    exit 0
}

Write-Host "Shader build FAILED (Unity exit code $code)."
$logText = ''
if (Test-Path $log) {
    Write-Host "Last 40 log lines ($log):"
    Get-Content $log -Tail 40
    $logText = Get-Content $log -Raw
}
if ($logText -match 'No valid Unity Editor license|not been activated with a valid License|Failed to activate/update license|LICENSE SYSTEM') {
    Write-Host 'Unity needs its free Personal licence activated: sign in once via Unity Hub.'
    exit 2
}
exit 1
