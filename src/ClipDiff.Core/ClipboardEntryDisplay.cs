namespace ClipDiff;

public sealed record ClipboardEntryFileLabels(string? Previous, string? Current);

public static class ClipboardEntryDisplay
{
    public static ClipboardEntryFileLabels ResolveFileLabels(
        ClipboardEntry? previous,
        ClipboardEntry? current)
    {
        var previousLabel = FileName(previous);
        var currentLabel = FileName(current);
        if (previousLabel is null || currentLabel is null ||
            !previousLabel.Equals(currentLabel, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(previous!.SourceFilePath) ||
            string.IsNullOrWhiteSpace(current!.SourceFilePath))
        {
            return new(previousLabel, currentLabel);
        }

        var previousSegments = SplitPath(previous.SourceFilePath);
        var currentSegments = SplitPath(current.SourceFilePath);
        var maximumDepth = Math.Max(previousSegments.Length, currentSegments.Length);
        for (var depth = 2; depth <= maximumDepth; depth++)
        {
            var previousSuffix = Suffix(previousSegments, depth);
            var currentSuffix = Suffix(currentSegments, depth);
            if (!previousSuffix.Equals(currentSuffix, StringComparison.OrdinalIgnoreCase))
            {
                return new(previousSuffix, currentSuffix);
            }
        }

        return new(previousLabel, currentLabel);
    }

    private static string? FileName(ClipboardEntry? entry) =>
        string.IsNullOrWhiteSpace(entry?.SourceFileName) ? null : entry.SourceFileName;

    private static string[] SplitPath(string path) =>
        path.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);

    private static string Suffix(string[] segments, int depth) =>
        string.Join('/', segments.Skip(Math.Max(0, segments.Length - depth)));
}
