namespace ClipDiff.Windows.Explorer;

internal static class ExplorerFileSelection
{
    public static bool TryGetPair(
        IReadOnlyList<string> filePaths,
        out string previousFilePath,
        out string currentFilePath)
    {
        ArgumentNullException.ThrowIfNull(filePaths);

        if (filePaths.Count == 2 && IsUsablePath(filePaths[0]) && IsUsablePath(filePaths[1]))
        {
            previousFilePath = filePaths[0];
            currentFilePath = filePaths[1];
            return true;
        }

        previousFilePath = string.Empty;
        currentFilePath = string.Empty;
        return false;
    }

    private static bool IsUsablePath(string? filePath) =>
        !string.IsNullOrWhiteSpace(filePath) && !filePath.Contains('\0');
}
