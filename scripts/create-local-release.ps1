[CmdletBinding()]
param(
	[switch]$Launch,
	[switch]$SkipNativeTests,
	[string]$Version
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'release-version.ps1')
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$releaseRoot = Join-Path $repositoryRoot 'releases'
$publishRoot = Join-Path $repositoryRoot 'artifacts/release-publish'
$packageStagingRoot = Join-Path $repositoryRoot 'artifacts/release-package'
$applicationProject = Join-Path $repositoryRoot 'src/ClipDiff.Windows/ClipDiff.Windows.csproj'
$coreTests = Join-Path $repositoryRoot 'tests/ClipDiff.Core.Tests/ClipDiff.Core.Tests.csproj'
$windowsTests = Join-Path $repositoryRoot 'tests/ClipDiff.Windows.Tests/ClipDiff.Windows.Tests.csproj'
$nativeDll = Join-Path $repositoryRoot 'artifacts/native/Release/ClipDiff.ShellExtension.dll'
$requiredPayload = @('ClipDiff.exe', 'ClipDiff.ShellExtension.dll')

function Assert-ReleasePayload([string]$Directory, [string]$Description)
{
	$actualPayload = @(Get-ChildItem $Directory -File | Select-Object -ExpandProperty Name | Sort-Object)
	$unexpectedPayload = @(Compare-Object ($requiredPayload | Sort-Object) $actualPayload)

	if ($unexpectedPayload.Count -ne 0)
	{
		throw "$Description validation failed. Expected only: $($requiredPayload -join ', ')."
	}
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue))
{
	throw 'The .NET 10 SDK is required. Install it from https://dotnet.microsoft.com/download/dotnet/10.0 and open a new PowerShell window.'
}

$sdkVersion = & dotnet --version

if ($LASTEXITCODE -ne 0 -or -not $sdkVersion.StartsWith('10.'))
{
	throw "The .NET 10 SDK is required, but the active SDK is '$sdkVersion'. Run 'dotnet --list-sdks' to see installed SDKs."
}

Push-Location $repositoryRoot

try
{
	if ([string]::IsNullOrWhiteSpace($Version))
	{
		$versionOutput = & dotnet msbuild $applicationProject -nologo -getProperty:Version

		if ($LASTEXITCODE -ne 0)
		{
			throw 'Could not read the ClipDiff version from MSBuild.'
		}

		$Version = ($versionOutput | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Last 1).Trim()
	}

	$Version = Get-ReleaseVersion -Version $Version

	$packages = @(
		[pscustomobject]@{
			Name = Get-ReleasePackageName -Version $Version -SelfContained $true
			SelfContained = $true
			Requirement = 'No separately installed .NET runtime required'
			PublishDirectory = $null
			StagingDirectory = $null
			StagingArchive = $null
			ReleaseDirectory = $null
			ReleaseArchive = $null
		},
		[pscustomobject]@{
			Name = Get-ReleasePackageName -Version $Version -SelfContained $false
			SelfContained = $false
			Requirement = 'Requires the x64 .NET 10 Desktop Runtime'
			PublishDirectory = $null
			StagingDirectory = $null
			StagingArchive = $null
			ReleaseDirectory = $null
			ReleaseArchive = $null
		}
	)

	if ($SkipNativeTests)
	{
		Write-Host 'Skipping native Explorer integration tests; the extension DLL will still be built.'
	}

	& (Join-Path $PSScriptRoot 'build-shell-extension.ps1') -Configuration Release -Test:(-not $SkipNativeTests)

	dotnet test $coreTests --configuration Release

	if ($LASTEXITCODE -ne 0)
	{
		throw 'Core tests failed.'
	}

	dotnet test $windowsTests --configuration Release

	if ($LASTEXITCODE -ne 0)
	{
		throw 'Windows policy tests failed.'
	}

	# dotnet publish does not clean custom output directories. Build both variants
	# privately and validate both before replacing any public release output.

	foreach ($package in $packages)
	{
		$package.PublishDirectory = Join-Path $publishRoot $package.Name
		$package.StagingDirectory = Join-Path $packageStagingRoot $package.Name
		$package.StagingArchive = Join-Path $packageStagingRoot "$($package.Name).zip"
		$package.ReleaseDirectory = Join-Path $releaseRoot $package.Name
		$package.ReleaseArchive = Join-Path $releaseRoot "$($package.Name).zip"

		foreach ($path in @($package.PublishDirectory, $package.StagingDirectory))
		{
			if (Test-Path $path)
			{
				Remove-Item $path -Recurse -Force
			}

			New-Item $path -ItemType Directory -Force | Out-Null
		}

		if (Test-Path $package.StagingArchive)
		{
			Remove-Item $package.StagingArchive -Force
		}

		$selfContained = $package.SelfContained.ToString().ToLowerInvariant()
		dotnet publish $applicationProject `
			--configuration Release `
			--runtime win-x64 `
			--self-contained $selfContained `
			--output $package.PublishDirectory `
			-p:Version=$Version `
			-p:PublishSingleFile=true `
			-p:PublishTrimmed=false `
			-p:IncludeNativeLibrariesForSelfExtract=true

		if ($LASTEXITCODE -ne 0)
		{
			throw "$($package.Name) publish failed."
		}

		$applicationVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $package.PublishDirectory 'ClipDiff.exe'))
		$binaryVersion = $Version.Split('-')[0] + '.0'

		if ($applicationVersion.FileVersion -ne $binaryVersion -or
			-not ($applicationVersion.ProductVersion -eq $Version -or $applicationVersion.ProductVersion.StartsWith("$Version+", [StringComparison]::Ordinal)))
		{
			throw "$($package.Name) executable version does not match the release version."
		}

		foreach ($file in $requiredPayload)
		{
			$source = if ($file -eq 'ClipDiff.ShellExtension.dll') { $nativeDll } else { Join-Path $package.PublishDirectory $file }

			if (-not (Test-Path $source -PathType Leaf))
			{
				throw "$($package.Name) is missing required file: $file"
			}

			Copy-Item $source $package.StagingDirectory
		}

		Assert-ReleasePayload $package.StagingDirectory "$($package.Name) directory"
		Compress-Archive -Path (Join-Path $package.StagingDirectory '*') -DestinationPath $package.StagingArchive -CompressionLevel Optimal

		Add-Type -AssemblyName System.IO.Compression.FileSystem
		$archive = [System.IO.Compression.ZipFile]::OpenRead($package.StagingArchive)

		try
		{
			$archivePayload = @($archive.Entries | Where-Object { -not [string]::IsNullOrEmpty($_.Name) } | Select-Object -ExpandProperty FullName | Sort-Object)
			$unexpectedArchivePayload = @(Compare-Object ($requiredPayload | Sort-Object) $archivePayload)

			if ($unexpectedArchivePayload.Count -ne 0)
			{
				throw "$($package.Name) archive validation failed. Expected only: $($requiredPayload -join ', ')."
			}
		}
		finally
		{
			$archive.Dispose()
		}
	}

	# Replace public outputs only after both variants pass validation, so a failed
	# build cannot publish mismatched or partial packages.
	New-Item $releaseRoot -ItemType Directory -Force | Out-Null

	foreach ($package in $packages)
	{
		if (Test-Path $package.ReleaseDirectory)
		{
			Remove-Item $package.ReleaseDirectory -Recurse -Force
		}

		if (Test-Path $package.ReleaseArchive)
		{
			Remove-Item $package.ReleaseArchive -Force
		}

		Move-Item $package.StagingDirectory $package.ReleaseDirectory
		Move-Item $package.StagingArchive $package.ReleaseArchive
		Remove-Item $package.PublishDirectory -Recurse -Force

		Write-Host ''
		Write-Host "Release directory: $($package.ReleaseDirectory)"
		Write-Host "Release archive:   $($package.ReleaseArchive)"
		Write-Host "Runtime:           $($package.Requirement)"
	}

	Write-Host "Payload:           $($requiredPayload -join ', ')"
	Write-Host 'Install:           Extract both files from one package into the same directory, then run ClipDiff.exe.'

	if ($Launch)
	{
		Start-Process (Join-Path $packages[0].ReleaseDirectory 'ClipDiff.exe')
	}
}
finally
{
	Pop-Location
}
