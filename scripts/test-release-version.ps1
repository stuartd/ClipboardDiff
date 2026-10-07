[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'release-version.ps1')
$applicationProject = Join-Path $PSScriptRoot '../src/ClipDiff.Windows/ClipDiff.Windows.csproj'

foreach ($version in @('1.2.3', '1.2.3-preview.1'))
{
	$tagVersion = Get-ReleaseVersion -Version "v$version" -FromTag

	if ($tagVersion -ne $version -or (Get-ReleaseVersion -Version $version) -ne $version)
	{
		throw "The release tag did not resolve to '$version'."
	}

	foreach ($selfContained in @($true, $false))
	{
		$variant = if ($selfContained) { 'self-contained' } else { 'net10' }
		$packageName = Get-ReleasePackageName -Version $tagVersion -SelfContained $selfContained

		if ("$packageName.zip" -ne "ClipDiff-$version-win-x64-$variant.zip")
		{
			throw 'The release asset name does not contain the resolved tag version.'
		}
	}

	$propertyOutput = & dotnet msbuild $applicationProject -nologo -target:GetAssemblyVersion -getProperty:Version,AssemblyVersion,FileVersion,InformationalVersion "-p:Version=$tagVersion"

	if ($LASTEXITCODE -ne 0)
	{
		throw 'Could not resolve release assembly version properties.'
	}

	$properties = ($propertyOutput -join "`n" | ConvertFrom-Json).Properties
	$binaryVersion = $version.Split('-')[0] + '.0'

	if ($properties.Version -ne $version -or
		$properties.AssemblyVersion -ne $binaryVersion -or
		$properties.FileVersion -ne $binaryVersion -or
		-not ($properties.InformationalVersion -eq $version -or $properties.InformationalVersion.StartsWith("$version+", [StringComparison]::Ordinal)))
	{
		throw 'Release assembly metadata does not match the resolved tag version.'
	}
}

foreach ($tag in @('', '1.2.3', 'v', 'v1.2', 'v1.2.3+metadata', 'v1.2.3/invalid'))
{
	$rejected = $false

	try
	{
		Get-ReleaseVersion -Version $tag -FromTag | Out-Null
	}
	catch
	{
		$rejected = $true
	}

	if (-not $rejected)
	{
		throw "Invalid release tag '$tag' was accepted."
	}
}

Write-Host 'Release tag, assembly metadata, and package-name checks passed.'
