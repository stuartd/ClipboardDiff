[CmdletBinding()]
param(
	[Parameter(Mandatory)][string]$Version,
	[Parameter(Mandatory)][string]$PayloadDirectory,
	[Parameter(Mandatory)][string]$ExtensionFileName,
	[Parameter(Mandatory)][string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'release-version.ps1')
$Version = Get-ReleaseVersion -Version $Version
$fileVersion = $Version.Split('-')[0] + '.0'
$compiler = Get-Command ISCC.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -First 1

if (-not $compiler)
{
	$compiler = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe'
}

if (-not (Test-Path $compiler -PathType Leaf))
{
	throw 'Inno Setup 6.3 or later is required. Install Inno Setup 6, or put ISCC.exe on PATH.'
}

if ($ExtensionFileName -cnotmatch '^ClipDiff\.ShellExtension\.[0-9a-f]{64}\.dll$')
{
	throw 'The installer requires a content-addressed shell extension filename.'
}

$PayloadDirectory = (Resolve-Path $PayloadDirectory).Path
$expected = @('ClipDiff.exe', $ExtensionFileName) | Sort-Object
$actual = @(Get-ChildItem $PayloadDirectory | Select-Object -ExpandProperty Name | Sort-Object)

if (@(Compare-Object $expected $actual).Count -ne 0)
{
	throw 'The installer source must be exactly the validated two-file self-contained payload.'
}

$dllHash = (Get-FileHash (Join-Path $PayloadDirectory $ExtensionFileName) -Algorithm SHA256).Hash.ToLowerInvariant()

if ($ExtensionFileName -cne "ClipDiff.ShellExtension.$dllHash.dll")
{
	throw 'The extension content does not match its filename.'
}

New-Item $OutputDirectory -ItemType Directory -Force | Out-Null
$OutputDirectory = (Resolve-Path $OutputDirectory).Path
$installer = Join-Path $OutputDirectory "ClipDiff-$Version-win-x64-Setup.exe"

if (Test-Path $installer)
{
	Remove-Item $installer -Force
}

& $compiler "/DAppVersion=$Version" "/DAppFileVersion=$fileVersion" "/DPayloadDirectory=$PayloadDirectory" `
	"/DExtensionFileName=$ExtensionFileName" "/DInstallerOutputDirectory=$OutputDirectory" `
	(Join-Path $PSScriptRoot '../installer/ClipDiff.iss')

if ($LASTEXITCODE -ne 0 -or -not (Test-Path $installer -PathType Leaf))
{
	throw 'Inno Setup did not produce the installer.'
}
