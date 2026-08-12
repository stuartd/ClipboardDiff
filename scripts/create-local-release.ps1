[CmdletBinding()]
param(
    [switch]$Launch
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$outputDirectory = Join-Path $repositoryRoot 'releases/win-x64'
$applicationProject = Join-Path $repositoryRoot 'src/ClipDiff.Windows/ClipDiff.Windows.csproj'
$coreTests = Join-Path $repositoryRoot 'tests/ClipDiff.Core.Tests/ClipDiff.Core.Tests.csproj'
$privacyTests = Join-Path $repositoryRoot 'tests/ClipDiff.Windows.Tests/ClipDiff.Windows.Tests.csproj'

Push-Location $repositoryRoot
try {
    dotnet test $coreTests --configuration Release
    if ($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }

    dotnet test $privacyTests --configuration Release
    if ($LASTEXITCODE -ne 0) { throw 'Privacy inspector tests failed.' }

    dotnet publish $applicationProject `
        --configuration Release `
        --runtime win-x64 `
        --self-contained true `
        --output $outputDirectory `
        -p:PublishSingleFile=true `
        -p:PublishTrimmed=false `
        -p:IncludeNativeLibrariesForSelfExtract=true
    if ($LASTEXITCODE -ne 0) { throw 'ClipDiff publish failed.' }

    $executable = Join-Path $outputDirectory 'ClipDiff.exe'
    Write-Host "Published ClipDiff to $executable"

    if ($Launch) {
        Start-Process $executable
    }
}
finally {
    Pop-Location
}
