using System.IO;
using System.Security;
using Microsoft.Win32;

namespace ClipDiff.Windows.Startup;

internal sealed class StartupRegistration(
	string registryPath = @"Software\Microsoft\Windows\CurrentVersion\Run",
	string? executablePath = null,
	string? entryAssemblyPath = null)
{
	private const string ValueName = "ClipDiff";

	public bool TryRead(out bool registered)
	{
		registered = false;

		try
		{
			using var key = Registry.CurrentUser.OpenSubKey(registryPath);
			registered = key?.GetValue(ValueName) is string command && !string.IsNullOrWhiteSpace(command);
			return true;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
		{
			return false;
		}
	}

	public bool TrySetEnabled(bool enabled)
	{
		try
		{
			if (enabled)
			{
				var path = executablePath ?? Environment.ProcessPath;
				var assemblyPath = entryAssemblyPath ?? ApplicationCommandLine.GetCurrentEntryAssemblyPath();

				if (!ApplicationCommandLine.TryBuild(path, assemblyPath, out var command))
				{
					return false;
				}

				// Windows limits Run value command lines to 260 characters.
				if (command.Length > 260)
				{
					return false;
				}

				using var key = Registry.CurrentUser.CreateSubKey(registryPath);
				key.SetValue(ValueName, command, RegistryValueKind.String);
			}
			else
			{
				using var key = Registry.CurrentUser.OpenSubKey(registryPath, writable: true);
				key?.DeleteValue(ValueName, throwOnMissingValue: false);
			}

			return true;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
		{
			return false;
		}
	}
}
