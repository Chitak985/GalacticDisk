param(
    [Parameter(Mandatory=$true)][string]$KspRoot,
    [string]$OutputPath = (Join-Path ([IO.Path]::GetTempPath()) ('GalacticDiskNativeEvents-' + [guid]::NewGuid().ToString('N')))
)
$ErrorActionPreference = 'Stop'
if (Test-Path -LiteralPath $OutputPath) { throw 'Choose a new output directory.' }
$managedPath = Join-Path $KspRoot 'KSP_x64_Data\Managed'
New-Item -ItemType Directory -Path $OutputPath | Out-Null
foreach ($name in @('Assembly-CSharp.dll','UnityEngine.dll','UnityEngine.CoreModule.dll')) {
    Copy-Item -LiteralPath (Join-Path $managedPath $name) -Destination $OutputPath
}
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$executable = Join-Path $OutputPath 'NativeSceneEvents.exe'
& $compiler /nologo /target:exe ('/out:'+$executable) ('/reference:'+(Join-Path $managedPath 'Assembly-CSharp.dll')) ('/reference:'+(Join-Path $managedPath 'UnityEngine.CoreModule.dll')) (Join-Path $PSScriptRoot 'Program.cs')
if ($LASTEXITCODE -ne 0) { throw 'Native event check compilation failed.' }
& $executable | Tee-Object -LiteralPath (Join-Path $OutputPath 'NativeSceneEvents.txt')
if ($LASTEXITCODE -ne 0) { throw 'Native event checks failed.' }
