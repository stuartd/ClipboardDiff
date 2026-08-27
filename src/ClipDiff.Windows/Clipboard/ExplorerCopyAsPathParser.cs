namespace ClipDiff.Windows.Clipboard;

internal static class ExplorerCopyAsPathParser
{
    public static bool TryParse(string text, out IReadOnlyList<string> filePaths)
    {
        ArgumentNullException.ThrowIfNull(text);

        var lines = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
        var lineCount = lines.Length;
        while (lineCount > 0 && lines[lineCount - 1].Length == 0)
        {
            lineCount--;
        }

        var paths = new List<string>(lineCount);
        for (var index = 0; index < lineCount; index++)
        {
            var line = lines[index];
            if (line.Length < 3 || line[0] != '"' || line[^1] != '"')
            {
                filePaths = [];
                return false;
            }

            var path = line[1..^1];
            if (path.Contains('"', StringComparison.Ordinal) || !IsAbsoluteWindowsPath(path))
            {
                filePaths = [];
                return false;
            }

            paths.Add(path);
        }

        filePaths = paths;
        return lineCount > 0;
    }

    private static bool IsAbsoluteWindowsPath(string path)
    {
        if (path.Length >= 3 &&
            IsAsciiLetter(path[0]) &&
            path[1] == ':' &&
            IsDirectorySeparator(path[2]))
        {
            return true;
        }

        if (path.Length < 5 ||
            !IsDirectorySeparator(path[0]) ||
            !IsDirectorySeparator(path[1]) ||
            IsDirectorySeparator(path[2]))
        {
            return false;
        }

        for (var index = 3; index < path.Length - 1; index++)
        {
            if (IsDirectorySeparator(path[index]) && !IsDirectorySeparator(path[index + 1]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAsciiLetter(char value) =>
        value is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    private static bool IsDirectorySeparator(char value) => value is '\\' or '/';
}
