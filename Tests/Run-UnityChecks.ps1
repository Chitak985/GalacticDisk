param(
    [string]$UnityEditor = 'C:\Program Files\Unity\Hub\Editor\2019.4.18f1\Editor\Unity.exe',
    [string]$HarnessPath = (Join-Path ([IO.Path]::GetTempPath()) ('GalacticDiskChecks-' + [guid]::NewGuid().ToString('N'))),
    [switch]$PlayMode,
    [switch]$Scale,
    [switch]$CameraFrames,
    [switch]$Rings,
    [switch]$RingPlayMode,
    [switch]$Editor
)
$ErrorActionPreference = 'Stop'
if (([int]$PlayMode.IsPresent + [int]$Scale.IsPresent + [int]$CameraFrames.IsPresent + [int]$Rings.IsPresent + [int]$RingPlayMode.IsPresent + [int]$Editor.IsPresent) -gt 1) { throw 'Run each check mode separately.' }
$repoRoot = Split-Path $PSScriptRoot -Parent
if (Test-Path -LiteralPath $HarnessPath) { throw 'Choose a new, empty harness directory.' }
foreach ($directory in @('Assets\Runtime','Assets\Checks','Packages','ProjectSettings','TestGame\GameData\GalacticDisk\Assets')) {
    New-Item -ItemType Directory -Path (Join-Path $HarnessPath $directory) -Force | Out-Null
}
Copy-Item -LiteralPath (Join-Path $repoRoot 'GalacticDiskDLL\main.cs') -Destination (Join-Path $HarnessPath 'Assets\Runtime\main.cs')
foreach ($source in Get-ChildItem -LiteralPath (Join-Path $repoRoot 'GalacticDiskDLL\Rendering') -Filter '*.cs') {
    Copy-Item -LiteralPath $source.FullName -Destination (Join-Path $HarnessPath 'Assets\Runtime')
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'KspStubs.cs') -Destination (Join-Path $HarnessPath 'Assets\Checks')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'IntegrationChecks.cs') -Destination (Join-Path $HarnessPath 'Assets\Checks')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'PlayModeChecks.cs') -Destination (Join-Path $HarnessPath 'Assets\Checks')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ScaleChecks.cs') -Destination (Join-Path $HarnessPath 'Assets\Checks')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'CameraFrameChecks.cs') -Destination (Join-Path $HarnessPath 'Assets\Checks')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'RingChecks.cs') -Destination (Join-Path $HarnessPath 'Assets\Checks')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'EditorChecks.cs') -Destination (Join-Path $HarnessPath 'Assets\Checks')
Copy-Item -LiteralPath (Join-Path $repoRoot 'GalacticDisk\ExampleRingConfig.txt') -Destination (Join-Path $HarnessPath 'RingExample.txt')
Copy-Item -LiteralPath (Join-Path $repoRoot 'UnityAssets\Packages\manifest.json') -Destination (Join-Path $HarnessPath 'Packages\manifest.json')
Copy-Item -LiteralPath (Join-Path $repoRoot 'UnityAssets\ProjectSettings\ProjectVersion.txt') -Destination (Join-Path $HarnessPath 'ProjectSettings\ProjectVersion.txt')
Copy-Item -LiteralPath (Join-Path $repoRoot 'GalacticDisk\Assets\galacticdisk-windows.bundle') -Destination (Join-Path $HarnessPath 'TestGame\GameData\GalacticDisk\Assets')
$logPath = Join-Path $HarnessPath 'unity-checks.log'
$entry = if ($Editor) { 'EditorChecks.Run' } elseif ($PlayMode) { 'PlayModeChecks.Begin' } elseif ($RingPlayMode) { 'PlayModeChecks.BeginRing' } elseif ($Rings) { 'RingChecks.Run' } elseif ($Scale) { 'ScaleChecks.Diagnose' } elseif ($CameraFrames) { 'CameraFrameChecks.Run' } else { 'IntegrationChecks.Run' }
$arguments = @('-batchmode','-projectPath',('"' + $HarnessPath + '"'),'-executeMethod',$entry,'-force-d3d11','-logFile',('"' + $logPath + '"'))
if (-not ($PlayMode -or $RingPlayMode)) { $arguments += '-quit' }
$unityProcess = Start-Process -FilePath $UnityEditor -ArgumentList $arguments -WindowStyle Hidden -PassThru
$unityProcess.WaitForExit()
Write-Output "Harness: $HarnessPath"
Write-Output "Log: $logPath"
if ($unityProcess.ExitCode -ne 0) { throw "Unity checks failed with exit code $($unityProcess.ExitCode). See $logPath" }
$report = if ($Editor) { 'EditorVerification\Editor.txt' } elseif ($PlayMode) { 'Verification\PlayMode.txt' } elseif ($RingPlayMode) { 'Verification\RingPlayMode.txt' } elseif ($Rings) { 'RingVerification\Rings.txt' } elseif ($Scale) { 'ScaleVerification\ScaleChecks.txt' } elseif ($CameraFrames) { 'FrameVerification\CameraFrames.txt' } else { 'Verification\Integration.txt' }
Get-Content -LiteralPath (Join-Path $HarnessPath $report)
