param(
    [string]$UnityEditor = 'C:\Program Files\Unity\Hub\Editor\2019.4.18f1\Editor\Unity.exe',
    [string]$HarnessPath = (Join-Path ([IO.Path]::GetTempPath()) ('GalacticDiskChecks-' + [guid]::NewGuid().ToString('N'))),
    [switch]$PlayMode,
    [switch]$Scale,
    [switch]$CameraFrames,
    [switch]$Rings,
    [switch]$RingPlayMode,
    [switch]$Editor,
    [switch]$Distribution,
    [switch]$Particles,
    [switch]$ParticlePlayMode,
    [switch]$Opacity,
    [switch]$Ordering,
    [switch]$Performance,
    [switch]$AnalyticBulges,
    [switch]$DefinitionBenchmark,
    [switch]$SceneCache,
    [switch]$Distant,
    [switch]$MapRendering,
    [switch]$Singularity,
    [switch]$MissingBody,
    [switch]$RotationInheritance,
    [switch]$MainMenu,
    [string]$SingularityShaderBundle,
    [string]$DefinitionsRoot,
    [string]$ProductionAssembly
)
$ErrorActionPreference = 'Stop'
if (([int]$PlayMode.IsPresent + [int]$Scale.IsPresent + [int]$CameraFrames.IsPresent + [int]$Rings.IsPresent + [int]$RingPlayMode.IsPresent + [int]$Editor.IsPresent + [int]$Distribution.IsPresent + [int]$Particles.IsPresent + [int]$ParticlePlayMode.IsPresent + [int]$Opacity.IsPresent + [int]$Ordering.IsPresent + [int]$Performance.IsPresent + [int]$AnalyticBulges.IsPresent + [int]$DefinitionBenchmark.IsPresent + [int]$SceneCache.IsPresent + [int]$Distant.IsPresent + [int]$MapRendering.IsPresent + [int]$Singularity.IsPresent + [int]$MissingBody.IsPresent + [int]$RotationInheritance.IsPresent) -gt 1) { throw 'Run each check mode separately.' }
$repoRoot = Split-Path $PSScriptRoot -Parent
if ($MainMenu -and ($PlayMode -or $Scale -or $CameraFrames -or $Rings -or $RingPlayMode -or $Editor -or $Distribution -or $Particles -or $ParticlePlayMode -or $Opacity -or $Ordering -or $Performance -or $AnalyticBulges -or $DefinitionBenchmark -or $SceneCache -or $Distant -or $MapRendering -or $Singularity -or $MissingBody -or $RotationInheritance)) { throw 'Run each check mode separately.' }
if (Test-Path -LiteralPath $HarnessPath) { throw 'Choose a new, empty harness directory.' }
foreach ($directory in @('Assets\Runtime','Assets\Checks','Packages','ProjectSettings','TestGame\GameData\GalacticDisk\Assets')) {
    New-Item -ItemType Directory -Path (Join-Path $HarnessPath $directory) -Force | Out-Null
}
foreach ($source in Get-ChildItem -LiteralPath (Join-Path $repoRoot 'GalacticDiskDLL') -Filter '*.cs') {
    Copy-Item -LiteralPath $source.FullName -Destination (Join-Path $HarnessPath 'Assets\Runtime')
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'KspStubs.cs') -Destination (Join-Path $HarnessPath 'Assets\Checks')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'IntegrationChecks.cs') -Destination (Join-Path $HarnessPath 'Assets\Checks')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'PlayModeChecks.cs') -Destination (Join-Path $HarnessPath 'Assets\Checks')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ScaleChecks.cs') -Destination (Join-Path $HarnessPath 'Assets\Checks')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'CameraFrameChecks.cs') -Destination (Join-Path $HarnessPath 'Assets\Checks')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'RingChecks.cs') -Destination (Join-Path $HarnessPath 'Assets\Checks')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'EditorChecks.cs') -Destination (Join-Path $HarnessPath 'Assets\Checks')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'MainMenuChecks.cs') -Destination (Join-Path $HarnessPath 'Assets\Checks')
foreach ($check in @('DistributionChecks.cs','ParticleRenderingChecks.cs','ParticlePlayModeChecks.cs','OpacityChecks.cs','OrderingChecks.cs','PerformanceChecks.cs','AnalyticBulgeChecks.cs','DefinitionBenchmarkChecks.cs','SceneCacheChecks.cs','DistantGalaxyChecks.cs','MapRenderingChecks.cs','SingularityCompatibilityChecks.cs','MissingBodyChecks.cs','RotationInheritanceChecks.cs')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $check) -Destination (Join-Path $HarnessPath 'Assets\Checks')
}
if ($Distribution) {
    if (-not $ProductionAssembly) { $ProductionAssembly = Join-Path $repoRoot 'GalacticDisk\GalacticDisk.dll' }
    Copy-Item -LiteralPath $ProductionAssembly -Destination (Join-Path $HarnessPath 'PackagedMod.bin')
}
Copy-Item -LiteralPath (Join-Path $repoRoot 'GalacticDisk\ExampleRingConfig.txt') -Destination (Join-Path $HarnessPath 'RingExample.txt')
Copy-Item -LiteralPath (Join-Path $repoRoot 'UnityAssets\Packages\manifest.json') -Destination (Join-Path $HarnessPath 'Packages\manifest.json')
Copy-Item -LiteralPath (Join-Path $repoRoot 'UnityAssets\ProjectSettings\ProjectVersion.txt') -Destination (Join-Path $HarnessPath 'ProjectSettings\ProjectVersion.txt')
Copy-Item -LiteralPath (Join-Path $repoRoot 'GalacticDisk\Assets\galacticdisk-windows.bundle') -Destination (Join-Path $HarnessPath 'TestGame\GameData\GalacticDisk\Assets')
if ($DefinitionBenchmark) {
    if (-not $DefinitionsRoot -or -not (Test-Path -LiteralPath $DefinitionsRoot -PathType Container)) { throw 'Specify -DefinitionsRoot with the read-only config directory to benchmark.' }
    $snapshot = [Text.StringBuilder]::new()
    foreach ($file in Get-ChildItem -LiteralPath $DefinitionsRoot -Filter '*.cfg' -File -Recurse) {
        foreach ($match in [regex]::Matches([IO.File]::ReadAllText($file.FullName),'(?ms)^\s*(GalacticDiskDefinition|GalacticDiskRingDefinition)\s*\{[^{}]*\}')) { [void]$snapshot.AppendLine($match.Value) }
    }
    [IO.File]::WriteAllText((Join-Path $HarnessPath 'DefinitionBenchmark.cfg'),$snapshot.ToString())
}
if ($Singularity) {
    if (-not $SingularityShaderBundle -or -not (Test-Path -LiteralPath $SingularityShaderBundle -PathType Leaf)) { throw 'Specify -SingularityShaderBundle with the installed Singularity Windows shader bundle.' }
    Copy-Item -LiteralPath $SingularityShaderBundle -Destination (Join-Path $HarnessPath 'SingularityShaders.bin')
}
$logPath = Join-Path $HarnessPath 'unity-checks.log'
$entry = if ($RotationInheritance) { 'RotationInheritanceChecks.Run' } elseif ($MissingBody) { 'MissingBodyChecks.Run' } elseif ($Singularity) { 'SingularityCompatibilityChecks.Run' } elseif ($MapRendering) { 'MapRenderingChecks.Run' } elseif ($Distant) { 'DistantGalaxyChecks.Run' } elseif ($SceneCache) { 'SceneCacheChecks.Begin' } elseif ($AnalyticBulges) { 'AnalyticBulgeChecks.Run' } elseif ($DefinitionBenchmark) { 'DefinitionBenchmarkChecks.Run' } elseif ($Performance) { 'PerformanceChecks.Run' } elseif ($Ordering) { 'OrderingChecks.Run' } elseif ($Opacity) { 'OpacityChecks.Run' } elseif ($Distribution) { 'DistributionChecks.Run' } elseif ($Particles) { 'ParticleRenderingChecks.Run' } elseif ($ParticlePlayMode) { 'ParticlePlayModeChecks.Begin' } elseif ($Editor) { 'EditorChecks.Run' } elseif ($PlayMode) { 'PlayModeChecks.Begin' } elseif ($RingPlayMode) { 'PlayModeChecks.BeginRing' } elseif ($Rings) { 'RingChecks.Run' } elseif ($Scale) { 'ScaleChecks.Diagnose' } elseif ($CameraFrames) { 'CameraFrameChecks.Run' } else { 'IntegrationChecks.Run' }
if ($MainMenu) { $entry = 'MainMenuChecks.Begin' }
$arguments = @('-batchmode','-projectPath',('"' + $HarnessPath + '"'),'-executeMethod',$entry,'-force-d3d11','-logFile',('"' + $logPath + '"'))
if (-not ($PlayMode -or $RingPlayMode -or $ParticlePlayMode -or $SceneCache -or $MainMenu)) { $arguments += '-quit' }
$unityProcess = Start-Process -FilePath $UnityEditor -ArgumentList $arguments -WindowStyle Hidden -PassThru
$unityProcess.WaitForExit()
Write-Output "Harness: $HarnessPath"
Write-Output "Log: $logPath"
if ($unityProcess.ExitCode -ne 0) { throw "Unity checks failed with exit code $($unityProcess.ExitCode). See $logPath" }
$report = if ($RotationInheritance) { 'RotationVerification\Rotation.txt' } elseif ($MissingBody) { 'MissingBodyVerification\MissingBody.txt' } elseif ($Singularity) { 'SingularityVerification\Singularity.txt' } elseif ($MapRendering) { 'MapRenderingVerification\MapRendering.txt' } elseif ($Distant) { 'DistantVerification\Distant.txt' } elseif ($SceneCache) { 'SceneCacheVerification\SceneCache.txt' } elseif ($AnalyticBulges) { 'AnalyticVerification\Analytic.txt' } elseif ($DefinitionBenchmark) { 'DefinitionVerification\Definitions.txt' } elseif ($Performance) { 'PerformanceVerification\Performance.txt' } elseif ($Ordering) { 'OrderingVerification\Ordering.txt' } elseif ($Opacity) { 'OpacityVerification\Opacity.txt' } elseif ($Distribution) { 'DistributionVerification\Distribution.txt' } elseif ($Particles) { 'ParticleVerification\Particles.txt' } elseif ($ParticlePlayMode) { 'ParticleVerification\ParticlePlayMode.txt' } elseif ($Editor) { 'EditorVerification\Editor.txt' } elseif ($PlayMode) { 'Verification\PlayMode.txt' } elseif ($RingPlayMode) { 'Verification\RingPlayMode.txt' } elseif ($Rings) { 'RingVerification\Rings.txt' } elseif ($Scale) { 'ScaleVerification\ScaleChecks.txt' } elseif ($CameraFrames) { 'FrameVerification\CameraFrames.txt' } else { 'Verification\Integration.txt' }
if ($MainMenu) { $report = 'MainMenuVerification\MainMenu.txt' }
Get-Content -LiteralPath (Join-Path $HarnessPath $report)
