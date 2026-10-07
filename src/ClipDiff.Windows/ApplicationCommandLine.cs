using System.IO;

namespace ClipDiff.Windows;

internal static class ApplicationCommandLine
{
	public static string Build(string processPath, string? entryAssemblyPath)
	{
		if (!TryBuild(processPath, entryAssemblyPath, out var command))
		{
			throw new ArgumentException("The application requires an absolute executable path and, when hosted by dotnet, an absolute entry-assembly path.");
		}

		return command;
	}

	public static bool TryBuild(string? processPath, string? entryAssemblyPath, out string command)
	{
		command = string.Empty;

		if (!IsAbsolutePath(processPath))
		{
			return false;
		}

		var separatorIndex = Math.Max(processPath!.LastIndexOf('/'), processPath.LastIndexOf('\\'));
		var fileName = processPath[(separatorIndex + 1)..];
		var isDotnetHost = string.Equals(fileName, "dotnet", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(fileName, "dotnet.exe", StringComparison.OrdinalIgnoreCase);

		if (isDotnetHost && !IsAbsolutePath(entryAssemblyPath))
		{
			return false;
		}

		command = $"\"{processPath}\"";

		if (isDotnetHost)
		{
			command += $" \"{entryAssemblyPath}\"";
		}

		return true;
	}

	private static bool IsAbsolutePath(string? value)
	{
		if (string.IsNullOrWhiteSpace(value) || value.Contains('"') || value.Contains('\0'))
		{
			return false;
		}

		// Recognize Windows paths when the pure command tests run on another OS.
		return Path.IsPathFullyQualified(value) ||
			(value.Length >= 3 && char.IsAsciiLetter(value[0]) && value[1] == ':' && value[2] is '\\' or '/') ||
			value.StartsWith(@"\\", StringComparison.Ordinal);
	}
}
