using System.IO;
using System.Text;

namespace ClipDiff.Windows.ExternalDiff;

internal sealed record ExternalDiffFiles(string DirectoryPath, string PreviousPath, string CurrentPath);

internal sealed class ExternalDiffWorkspace
{
    private static readonly UTF8Encoding Utf8WithByteOrderMark = new(true);
    private readonly string _rootDirectory;

    public ExternalDiffWorkspace(string? rootDirectory = null)
    {
        _rootDirectory = rootDirectory ?? Path.Combine(
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

        Directory.CreateDirectory(_rootDirectory);
        var directory = Path.Combine(_rootDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
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
            return new ExternalDiffFiles(directory, previousPath, currentPath);
        }
        catch
        {
            TryDelete(directory);
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
        if (!Directory.Exists(_rootDirectory))
        {
            return;
        }

        try
        {
            foreach (var directory in Directory.EnumerateDirectories(_rootDirectory))
            {
                TryDelete(directory);
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
