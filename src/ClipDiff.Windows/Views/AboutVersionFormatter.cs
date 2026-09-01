namespace ClipDiff.Windows.Views;

internal static class AboutVersionFormatter
{
    private const int ShortCommitLength = 7;

    public static string Format(string? informationalVersion, Version? assemblyVersion)
    {
        var parts = informationalVersion?.Split('+', 2);

        var version = parts is { Length: > 0 } && !string.IsNullOrWhiteSpace(parts[0])
            ? parts[0]
            : assemblyVersion?.ToString(2) ?? "Unknown";

        if (parts is not { Length: 2 })
        {
            return version;
        }

        var commit = parts[1]
            .Split('.')
            .LastOrDefault(IsCommitHash);
            
        return commit is null
            ? version
            : $"{version} ({commit[..ShortCommitLength]})";
    }

    private static bool IsCommitHash(string value) =>
        value.Length >= ShortCommitLength && value.All(IsHexDigit);

    private static bool IsHexDigit(char value) =>
        value is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
}
