using Microsoft.Win32;

namespace ClipDiff.Windows.ExternalDiff;

internal interface IExternalDiffRegistry
{
	string? ReadValue(RegistryHive hive, RegistryView view, string keyPath, string? valueName = null);

	IReadOnlyList<string> ReadSubKeyNames(RegistryHive hive, RegistryView view, string keyPath);
}

internal sealed class ExternalDiffRegistry : IExternalDiffRegistry
{
	public string? ReadValue(RegistryHive hive, RegistryView view, string keyPath, string? valueName = null)
	{
		using var baseKey = RegistryKey.OpenBaseKey(hive, view);
		using var key = baseKey.OpenSubKey(keyPath);

		return key?.GetValue(valueName) as string;
	}

	public IReadOnlyList<string> ReadSubKeyNames(RegistryHive hive, RegistryView view, string keyPath)
	{
		using var baseKey = RegistryKey.OpenBaseKey(hive, view);
		using var key = baseKey.OpenSubKey(keyPath);

		return key?.GetSubKeyNames() ?? [];
	}
}
