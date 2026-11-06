using System.IO;
using System.Reflection;
using System.Security;
using System.Text.RegularExpressions;

namespace ClipDiff.Windows.Installation;

internal static class ShellExtensionFiles
{
	private static readonly Regex FileNamePattern = new(
		@"\AClipDiff\.ShellExtension(?:\.[0-9a-f]{64})?\.dll\z", RegexOptions.CultureInvariant);

	public static string CurrentFileName { get; } = ResolveFileName(typeof(ShellExtensionFiles).Assembly
		.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(attribute => attribute.Key == "ShellExtensionFileName")?.Value);

	internal static string ResolveFileName(string? metadata) =>
		metadata is not null && FileNamePattern.IsMatch(metadata) ? metadata : "ClipDiff.ShellExtension.dll";

	public static void TryCleanup(string directory, string currentFileName, IEnumerable<string> protectedPaths)
	{
		try
		{
			var keep = new HashSet<string>(protectedPaths, StringComparer.OrdinalIgnoreCase)
			{
				Path.Combine(directory, currentFileName)
			};

			foreach (var path in Directory.EnumerateFiles(directory, "ClipDiff.ShellExtension*.dll"))
			{
				if (!FileNamePattern.IsMatch(Path.GetFileName(path)) || keep.Contains(path))
				{
					continue;
				}

				try
				{
					// Windows rejects deletion of a loaded image. Do not schedule a reboot or restart Explorer.
					File.Delete(path);
				}
				catch (Exception exception) when (IsCleanupFailure(exception))
				{
					// Retry on a later launch after Explorer has released the image.
				}
			}
		}
		catch (Exception exception) when (IsCleanupFailure(exception))
		{
			// An unwritable directory or failed enumeration must never prevent startup.
		}
	}

	private static bool IsCleanupFailure(Exception exception) =>
		exception is IOException or UnauthorizedAccessException or SecurityException;
}
