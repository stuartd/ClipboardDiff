[CmdletBinding()]
param(
    [switch]$Launch,
    [switch]$SkipNativeTests,
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$releaseRoot = Join-Path $repositoryRoot 'releases'
$publishDirectory = Join-Path $repositoryRoot 'artifacts/release-publish/win-x64'
$packageStagingRoot = Join-Path $repositoryRoot 'artifacts/release-package'
$applicationProject = Join-Path $repositoryRoot 'src/ClipDiff.Windows/ClipDiff.Windows.csproj'
$coreTests = Join-Path $repositoryRoot 'tests/ClipDiff.Core.Tests/ClipDiff.Core.Tests.csproj'
$privacyTests = Join-Path $repositoryRoot 'tests/ClipDiff.Windows.Tests/ClipDiff.Windows.Tests.csproj'
$nativeDll = Join-Path $repositoryRoot 'artifacts/native/Release/ClipDiff.ShellExtension.dll'
$requiredPayload = @('ClipDiff.exe', 'ClipDiff.ShellExtension.dll')

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'The .NET 10 SDK is required. Install it from https://dotnet.microsoft.com/download/dotnet/10.0 and open a new PowerShell window.'
}

$sdkVersion = & dotnet --version
if ($LASTEXITCODE -ne 0 -or -not $sdkVersion.StartsWith('10.')) {
    throw "The .NET 10 SDK is required, but the active SDK is '$sdkVersion'. Run 'dotnet --list-sdks' to see installed SDKs."
}

Push-Location $repositoryRoot
try {
    if ([string]::IsNullOrWhiteSpace($Version)) {
        $versionOutput = & dotnet msbuild $applicationProject -nologo -getProperty:Version
        if ($LASTEXITCODE -ne 0) { throw 'Could not read the ClipDiff version from MSBuild.' }
        $Version = ($versionOutput | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Last 1).Trim()
    }

    if ($Version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?$') {
        throw "Release version '$Version' is invalid. Use a version such as 1.0.0 or 1.0.0-preview.1."
    }

    $packageName = "ClipDiff-$Version-win-x64"
    $packageDirectory = Join-Path $releaseRoot $packageName
    $archivePath = Join-Path $releaseRoot "$packageName.zip"
    $packageStagingDirectory = Join-Path $packageStagingRoot $packageName
    $archiveStagingPath = Join-Path $packageStagingRoot "$packageName.zip"

    # dotnet publish does not clean a custom output directory. Always publish to a
    # private, empty staging directory so stale files can never enter a release.
    if (Test-Path $publishDirectory) { Remove-Item $publishDirectory -Recurse -Force }
    if (Test-Path $packageStagingDirectory) { Remove-Item $packageStagingDirectory -Recurse -Force }
    if (Test-Path $archiveStagingPath) { Remove-Item $archiveStagingPath -Force }
    New-Item $publishDirectory -ItemType Directory -Force | Out-Null
    New-Item $packageStagingDirectory -ItemType Directory -Force | Out-Null

    if ($SkipNativeTests) {
        Write-Host 'Skipping native Explorer integration tests; the extension DLL will still be built.'
    }
    & (Join-Path $PSScriptRoot 'build-shell-extension.ps1') -Configuration Release -Test:(-not $SkipNativeTests)

    dotnet test $coreTests --configuration Release
    if ($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }

    dotnet test $privacyTests --configuration Release
    if ($LASTEXITCODE -ne 0) { throw 'Privacy inspector tests failed.' }

    dotnet publish $applicationProject `
        --configuration Release `
        --runtime win-x64 `
        --self-contained true `
        --output $publishDirectory `
        -p:Version=$Version `
        -p:PublishSingleFile=true `
        -p:PublishTrimmed=false `
        -p:IncludeNativeLibrariesForSelfExtract=true
    if ($LASTEXITCODE -ne 0) { throw 'ClipDiff publish failed.' }

    foreach ($file in $requiredPayload) {
        $source = if ($file -eq 'ClipDiff.ShellExtension.dll') { $nativeDll } else { Join-Path $publishDirectory $file }
        if (-not (Test-Path $source -PathType Leaf)) { throw "Release payload is missing required file: $file" }
        Copy-Item $source $packageStagingDirectory
    }

    $actualPayload = @(Get-ChildItem $packageStagingDirectory -File | Select-Object -ExpandProperty Name | Sort-Object)
    $unexpectedPayload = @(Compare-Object ($requiredPayload | Sort-Object) $actualPayload)
    if ($unexpectedPayload.Count -ne 0) {
        throw "Release payload validation failed. Expected only: $($requiredPayload -join ', ')."
    }

    Compress-Archive -Path (Join-Path $packageStagingDirectory '*') -DestinationPath $archiveStagingPath -CompressionLevel Optimal

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($archiveStagingPath)
    try {
        $archivePayload = @($archive.Entries | Where-Object { -not [string]::IsNullOrEmpty($_.Name) } | Select-Object -ExpandProperty FullName | Sort-Object)
        $unexpectedArchivePayload = @(Compare-Object ($requiredPayload | Sort-Object) $archivePayload)
        if ($unexpectedArchivePayload.Count -ne 0) {
            throw "Release archive validation failed. Expected only: $($requiredPayload -join ', ')."
        }
    }
    finally {
        $archive.Dispose()
    }

    # Replace the public output only after the staged folder and ZIP have both
    # passed validation, so a failed build cannot leave a partial release behind.
    New-Item $releaseRoot -ItemType Directory -Force | Out-Null
    if (Test-Path $packageDirectory) { Remove-Item $packageDirectory -Recurse -Force }
    if (Test-Path $archivePath) { Remove-Item $archivePath -Force }
    Move-Item $packageStagingDirectory $packageDirectory
    Move-Item $archiveStagingPath $archivePath
    Remove-Item $publishDirectory -Recurse -Force

    $executable = Join-Path $packageDirectory 'ClipDiff.exe'
    Write-Host ''
    Write-Host "Release directory: $packageDirectory"
    Write-Host "Release archive:   $archivePath"
    Write-Host "Payload:           $($requiredPayload -join ', ')"
    Write-Host 'Install:           Extract both files into the same directory, then run ClipDiff.exe.'

    if ($Launch) {
        Start-Process $executable
    }
}
finally {
    Pop-Location
}
