using System.IO;
using System.Text;
using ClipDiff.Windows.Ownership;

namespace ClipDiff.Windows.ExternalDiff;

internal sealed class ExternalDiffFiles(
	string directoryPath,
	string previousPath,
	string currentPath,
	SharedFileLease lease) : IDisposable
{
	private SharedFileLease? sharedFileLease = lease;

	public string DirectoryPath { get; } = directoryPath;

	public string PreviousPath { get; } = previousPath;

	public string CurrentPath { get; } = currentPath;

	public void Dispose()
	{
		var ownership = Interlocked.Exchange(ref sharedFileLease, null);

		if (ownership is null)
		{
			return;
		}

		ExternalDiffWorkspace.TryDelete(DirectoryPath);
		ownership.Dispose();
	}
}

internal sealed class ExternalDiffWorkspace
{
	private static readonly UTF8Encoding Utf8WithByteOrderMark = new(true);
	private readonly string rootDirectory;

	public ExternalDiffWorkspace(string? directory = null)
	{
		rootDirectory = directory ?? Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
			"ClipDiff",
			"Temp");
	}

	public ExternalDiffFiles Create(
		string previousText,
		string currentText,
		string? previousSourceFileName = null,
		string? currentSourceFileName = null)
	{
		ArgumentNullException.ThrowIfNull(previousText);
		ArgumentNullException.ThrowIfNull(currentText);

		Directory.CreateDirectory(rootDirectory);
		var directory = Path.Combine(rootDirectory, Guid.NewGuid().ToString("N"));
		var ownership = SharedFileLease.Acquire(directory + ".owner");

		try
		{
			Directory.CreateDirectory(directory);

			var previousPath = CreateSidePath(
				directory,
				"Previous",
				"Previous clipboard.txt",
				previousSourceFileName);
			var currentPath = CreateSidePath(
				directory,
				"Current",
				"Current clipboard.txt",
				currentSourceFileName);

			File.WriteAllText(previousPath, previousText, Utf8WithByteOrderMark);
			File.WriteAllText(currentPath, currentText, Utf8WithByteOrderMark);
			File.SetAttributes(previousPath, FileAttributes.ReadOnly | FileAttributes.Temporary);
			File.SetAttributes(currentPath, FileAttributes.ReadOnly | FileAttributes.Temporary);

			return new ExternalDiffFiles(directory, previousPath, currentPath, ownership);
		}
		catch
		{
			TryDelete(directory);
			ownership.Dispose();
			throw;
		}
	}

	private static string CreateSidePath(
		string comparisonDirectory,
		string side,
		string defaultFileName,
		string? sourceFileName)
	{
		if (string.IsNullOrWhiteSpace(sourceFileName))
		{
			return Path.Combine(comparisonDirectory, defaultFileName);
		}

		var sideDirectory = Path.Combine(comparisonDirectory, side);
		Directory.CreateDirectory(sideDirectory);
		return Path.Combine(sideDirectory, SanitizeFileName(sourceFileName, defaultFileName));
	}

	private static string SanitizeFileName(string fileName, string fallback)
	{
		var separatorIndex = fileName.LastIndexOfAny(['\\', '/']);
		var name = separatorIndex >= 0 ? fileName[(separatorIndex + 1)..] : fileName;
		var sanitized = new string(name
			.Select(character => character < ' ' || "<>:\"/\\|?*".Contains(character) ? '_' : character)
			.ToArray())
			.TrimEnd(' ', '.');

		return sanitized.Length == 0 ? fallback : sanitized;
	}

	public void CleanupStaleDirectories()
	{
		if (!Directory.Exists(rootDirectory))
		{
			return;
		}

		try
		{
			foreach (var directory in Directory.EnumerateDirectories(rootDirectory))
			{
				// The marker lives beside the directory so it can remain exclusively
				// open throughout deletion. A live comparison cannot be acquired.
				using var ownership = SharedFileLease.TryAcquire(directory + ".owner");

				if (ownership is not null)
				{
					TryDelete(directory);
				}
			}
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
		}
	}

	public static bool TryDelete(string directory)
	{
		if (!Directory.Exists(directory))
		{
			return true;
		}

		try
		{
			foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
			{
				File.SetAttributes(file, FileAttributes.Normal);
			}

			Directory.Delete(directory, true);
			return true;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			return false;
		}
	}
}
