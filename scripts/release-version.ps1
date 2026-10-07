function Get-ReleaseVersion
{
	param(
		[string]$Version,
		[switch]$FromTag
	)

	$pattern = '^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?$'

	if ($FromTag)
	{
		if (-not $Version.StartsWith('v', [StringComparison]::Ordinal))
		{
			throw "Release tags must start with v; got '$Version'."
		}

		$Version = $Version.Substring(1)
	}

	if ($Version -notmatch $pattern)
	{
		throw "Release version '$Version' is invalid. Use a version such as 1.2.3 or 1.2.3-preview.1."
	}

	return $Version
}

function Get-ReleasePackageName
{
	param(
		[string]$Version,
		[bool]$SelfContained
	)

	$resolvedVersion = Get-ReleaseVersion -Version $Version
	$variant = if ($SelfContained) { 'self-contained' } else { 'net10' }
	return "ClipDiff-$resolvedVersion-win-x64-$variant"
}
