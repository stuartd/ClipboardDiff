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

    public ExternalDiffFiles Create(string previousText, string currentText)
    {
        ArgumentNullException.ThrowIfNull(previousText);
        ArgumentNullException.ThrowIfNull(currentText);

        Directory.CreateDirectory(_rootDirectory);
        var directory = Path.Combine(_rootDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        var previousPath = Path.Combine(directory, "Previous clipboard.txt");
        var currentPath = Path.Combine(directory, "Current clipboard.txt");
        try
        {
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
