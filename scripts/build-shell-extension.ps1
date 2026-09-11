[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [switch]$Test
)

$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'The native Explorer extension requires Windows and Visual Studio C++ build tools.' }
Write-Host "Native build: configuration=$Configuration, target=x64, Windows=$([Environment]::OSVersion.Version), PowerShell=$($PSVersionTable.PSVersion), processBits=$([IntPtr]::Size * 8)"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
if (-not (Test-Path $vswhere)) { throw 'Install Visual Studio Build Tools with Desktop development with C++ and a Windows SDK.' }
$msbuild = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (-not $msbuild) { throw 'Visual Studio MSBuild with the C++ toolset was not found.' }
Write-Host "MSBuild: $msbuild"
$nativeProject = Join-Path $repositoryRoot 'src/ClipDiff.ShellExtension/ClipDiff.ShellExtension.vcxproj'
if ($Test) { $nativeProject = Join-Path $repositoryRoot 'tests/ClipDiff.ShellExtension.Tests/ClipDiff.ShellExtension.Tests.vcxproj' }
& $msbuild $nativeProject /m /nologo /verbosity:minimal "/p:Configuration=$Configuration" /p:Platform=x64
$buildExitCode = $LASTEXITCODE
if ($buildExitCode -ne 0) { throw "Native Explorer extension compilation failed (MSBuild exit code $buildExitCode). See compiler output above." }

$nativeOutput = Join-Path $repositoryRoot "artifacts/native/$Configuration"
if ($Test) {
    $testExecutable = Join-Path $nativeOutput 'ClipDiff.ShellExtension.Tests.exe'
    $extensionDll = Join-Path $nativeOutput 'ClipDiff.ShellExtension.dll'
    Write-Host 'Native compilation succeeded. Running Windows Shell integration tests (ClipDiff must be closed).'
    Write-Host "Test executable: $testExecutable"
    Write-Host "Extension DLL: $extensionDll"
    & $testExecutable $extensionDll
    $testExitCode = $LASTEXITCODE
    if ($testExitCode -ne 0) {
        throw "Native Explorer extension tests failed (exit code $testExitCode). Compilation succeeded; see the [FAIL] test case and diagnostics above. Include the full output starting at 'Native build:' when reporting this failure."
    }
}
