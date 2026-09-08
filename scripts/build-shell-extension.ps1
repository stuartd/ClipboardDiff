[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [switch]$Test
)

$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'The native Explorer extension requires Windows and Visual Studio C++ build tools.' }
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
if (-not (Test-Path $vswhere)) { throw 'Install Visual Studio Build Tools with Desktop development with C++ and a Windows SDK.' }
$msbuild = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (-not $msbuild) { throw 'Visual Studio MSBuild with the C++ toolset was not found.' }
$nativeProject = Join-Path $repositoryRoot 'src/ClipDiff.ShellExtension/ClipDiff.ShellExtension.vcxproj'
if ($Test) { $nativeProject = Join-Path $repositoryRoot 'tests/ClipDiff.ShellExtension.Tests/ClipDiff.ShellExtension.Tests.vcxproj' }
& $msbuild $nativeProject /m /nologo /verbosity:minimal "/p:Configuration=$Configuration" /p:Platform=x64
if ($LASTEXITCODE -ne 0) { throw 'Native Explorer extension build failed.' }

$nativeOutput = Join-Path $repositoryRoot "artifacts/native/$Configuration"
if ($Test) {
    & (Join-Path $nativeOutput 'ClipDiff.ShellExtension.Tests.exe') (Join-Path $nativeOutput 'ClipDiff.ShellExtension.dll')
    if ($LASTEXITCODE -ne 0) { throw 'Native Explorer extension tests failed.' }
}
