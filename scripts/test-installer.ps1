# Windows-only integration smoke test. Requires the release assets already built.
[CmdletBinding()]
param([string]$Version)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'release-version.ps1')

if ($env:OS -ne 'Windows_NT' -or -not [Environment]::Is64BitProcess)
{
	throw 'Run the installer integration test in x64 PowerShell on Windows.'
}

if (Get-Process ClipDiff -ErrorAction SilentlyContinue)
{
	throw 'Close ClipDiff before running this isolated installer test.'
}

$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{75F83916-586C-4D73-B744-AD39917A5039}_is1'

if (Test-Path $uninstallKey)
{
	throw 'This test requires a user profile without an existing ClipDiff installer installation.'
}

if ([string]::IsNullOrWhiteSpace($Version))
{
	$Version = (& dotnet msbuild (Join-Path $repositoryRoot 'src/ClipDiff.Windows/ClipDiff.Windows.csproj') -nologo -getProperty:Version | Select-Object -Last 1).Trim()
}

$Version = Get-ReleaseVersion -Version $Version
$baselineInstaller = Join-Path $repositoryRoot "releases/ClipDiff-$Version-win-x64-Setup.exe"
$baselinePayload = Join-Path $repositoryRoot "releases/$(Get-ReleasePackageName -Version $Version -SelfContained $true)"
$testRoot = Join-Path $repositoryRoot "artifacts/installer-smoke/$([Guid]::NewGuid().ToString('N'))"
$installDirectory = Join-Path $testRoot 'Installed app'
$settingsPath = Join-Path $env:LOCALAPPDATA 'ClipDiff/settings.json'
$savedSettings = if (Test-Path $settingsPath) { [IO.File]::ReadAllBytes($settingsPath) } else { $null }
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$runRegistryPath = 'HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run'
$savedRun = [Microsoft.Win32.Registry]::GetValue($runRegistryPath, 'ClipDiff', $null)
$loadedImages = [Collections.Generic.List[IntPtr]]::new()
$app = $null

New-Item $testRoot -ItemType Directory -Force | Out-Null
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class ClipDiffInstallerTestNative {
 [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
 public static extern IntPtr LoadLibraryW(string path);
 [DllImport("kernel32.dll")]
 public static extern bool FreeLibrary(IntPtr module);
}
'@

function Invoke-Installer([string]$Path, [string]$Step, [switch]$Uninstall)
{
	$arguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/LOG=`"$(Join-Path $testRoot "$Step.log")`"")

	if (-not $Uninstall)
	{
		$arguments += "/DIR=`"$installDirectory`""
	}

	$process = Start-Process $Path -ArgumentList $arguments -PassThru

	if (-not $process.WaitForExit(60000))
	{
		$process.Kill()
		throw "$Step timed out. See $testRoot."
	}

	if ($process.ExitCode -ne 0)
	{
		throw "$Step failed or requested a restart (exit $($process.ExitCode)). See $testRoot."
	}
}

function Start-TestApp([string]$ExpectedExtension)
{
	$process = Start-Process (Join-Path $installDirectory 'ClipDiff.exe') -PassThru
	$serverKey = 'HKCU:\Software\Classes\CLSID\{6B46A974-40E2-4AD4-9F68-E534202B11E8}\InprocServer32'

	for ($attempt = 0; $attempt -lt 100; $attempt++)
	{
		$path = if (Test-Path $serverKey) { (Get-Item $serverKey).GetValue('') } else { '' }

		if ($path -eq (Join-Path $installDirectory $ExpectedExtension))
		{
			return $process
		}

		if ($process.HasExited)
		{
			throw 'The installed app exited before registering its extension.'
		}

		Start-Sleep -Milliseconds 100
	}

	$process.Kill()
	throw 'The installed app did not register the expected extension.'
}

function Hold-Extension([string]$Name)
{
	$path = Join-Path $installDirectory $Name
	$module = [ClipDiffInstallerTestNative]::LoadLibraryW($path)

	if ($module -eq [IntPtr]::Zero)
	{
		throw "Could not load the installed native extension: $path"
	}

	$loadedImages.Add($module)

	try
	{
		[IO.File]::Open($path, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None).Dispose()
		throw 'The test did not establish the expected Windows image lock.'
	}
	catch [IO.IOException] { }
	catch [UnauthorizedAccessException] { }
}

try
{
	New-Item (Split-Path $settingsPath) -ItemType Directory -Force | Out-Null
	[IO.File]::WriteAllText($settingsPath, '{"StartupPromptShown":true,"UpdateChecks":{"AutomaticChecksEnabled":false}}')
	Invoke-Installer $baselineInstaller 'first-install'
	$originalExtension = Get-ChildItem $baselinePayload -Filter 'ClipDiff.ShellExtension.*.dll' | Select-Object -ExpandProperty Name -First 1
	Set-ItemProperty $runKey -Name ClipDiff -Value "`"$(Join-Path $installDirectory 'ClipDiff.exe')`""
	$app = Start-TestApp $originalExtension
	Hold-Extension $originalExtension
	Invoke-Installer $baselineInstaller 'upgrade-unchanged-extension'

	if (-not $app.WaitForExit(10000))
	{
		throw 'The installer did not shut down the running app.'
	}

	# A harmless PE overlay changes the native binary's hash without changing its COM contract.
	$changedPayload = Join-Path $testRoot 'changed-payload'
	New-Item $changedPayload -ItemType Directory -Force | Out-Null
	$changedDll = Join-Path $testRoot 'changed.dll'
	Copy-Item (Join-Path $baselinePayload $originalExtension) $changedDll
	$stream = [IO.File]::OpenWrite($changedDll)

	try
	{
		$stream.Position = $stream.Length
		$stream.Write([Text.Encoding]::ASCII.GetBytes('ClipDiff installer integration fixture'))
	}
	finally
	{
		$stream.Dispose()
	}

	$changedName = 'ClipDiff.ShellExtension.' + (Get-FileHash $changedDll -Algorithm SHA256).Hash.ToLowerInvariant() + '.dll'
	$publishDirectory = Join-Path $testRoot 'publish'
	& dotnet publish (Join-Path $repositoryRoot 'src/ClipDiff.Windows/ClipDiff.Windows.csproj') --configuration Release `
		--runtime win-x64 --self-contained true --output $publishDirectory "-p:Version=$Version" `
		"-p:ShellExtensionFileName=$changedName" -p:PublishSingleFile=true -p:PublishTrimmed=false -p:IncludeNativeLibrariesForSelfExtract=true

	if ($LASTEXITCODE -ne 0)
	{
		throw 'Could not build the changed-extension fixture application.'
	}

	Copy-Item (Join-Path $publishDirectory 'ClipDiff.exe') $changedPayload
	Copy-Item $changedDll (Join-Path $changedPayload $changedName)
	& (Join-Path $PSScriptRoot 'create-installer.ps1') -Version $Version -PayloadDirectory $changedPayload `
		-ExtensionFileName $changedName -OutputDirectory $testRoot
	$app = Start-TestApp $originalExtension
	Invoke-Installer (Join-Path $testRoot "ClipDiff-$Version-win-x64-Setup.exe") 'upgrade-changed-extension'

	if (-not $app.WaitForExit(10000) -or -not (Test-Path (Join-Path $installDirectory $originalExtension)))
	{
		throw 'Changed-extension update did not close the app and preserve the loaded old DLL.'
	}

	$app = Start-TestApp $changedName
	Hold-Extension $changedName
	Invoke-Installer (Join-Path $installDirectory 'unins000.exe') 'uninstall' -Uninstall

	if (-not $app.WaitForExit(10000) -or (Test-Path (Join-Path $installDirectory 'ClipDiff.exe')) -or (Test-Path $uninstallKey))
	{
		throw 'Uninstall did not stop the app and remove the executable/installation record.'
	}

	if ([Microsoft.Win32.Registry]::GetValue($runRegistryPath, 'ClipDiff', $null) -or
		(Test-Path 'HKCU:\Software\Classes\CLSID\{6B46A974-40E2-4AD4-9F68-E534202B11E8}'))
	{
		throw 'Uninstall left the installed app registered.'
	}

	Write-Host 'Installer first install, running-app upgrades, unchanged/changed loaded DLLs, and uninstall passed without a restart.'
	Write-Host 'Explorer context-menu behavior still requires the manual Windows checks in docs/windows-update-testing.md.'
}
finally
{
	if ($app -and -not $app.HasExited)
	{
		$app.Kill()
		$app.WaitForExit()
	}

	foreach ($module in $loadedImages)
	{
		[ClipDiffInstallerTestNative]::FreeLibrary($module) | Out-Null
	}

	if ($null -ne $savedSettings)
	{
		[IO.File]::WriteAllBytes($settingsPath, $savedSettings)
	}
	else
	{
		Remove-Item $settingsPath -ErrorAction SilentlyContinue
	}

	if ($null -ne $savedRun)
	{
		Set-ItemProperty $runKey -Name ClipDiff -Value $savedRun
	}
	else
	{
		Remove-ItemProperty $runKey -Name ClipDiff -ErrorAction SilentlyContinue
	}
}
