namespace ClipDiff.Windows.Explorer;

internal static class ExplorerContextCommandLine
{
    public const string CompareWithCurrentSwitch = "--compare-with-current";

    public static bool TryGetSelectedFile(IReadOnlyList<string> arguments, out string filePath)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 2 &&
            string.Equals(arguments[0], CompareWithCurrentSwitch, StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(arguments[1]) &&
            !arguments[1].Contains('\0'))
        {
            filePath = arguments[1];
            return true;
        }

        filePath = string.Empty;
        return false;
    }

    public static string BuildShellCommand(string processPath, string? entryAssemblyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(processPath);

        var command = Quote(processPath);
        if (IsDotnetHost(processPath) &&
            !string.IsNullOrWhiteSpace(entryAssemblyPath))
        {
            command += " " + Quote(entryAssemblyPath);
        }

        return $"{command} {CompareWithCurrentSwitch} \"%1\"";
    }

    private static bool IsDotnetHost(string processPath)
    {
        var separatorIndex = Math.Max(processPath.LastIndexOf('/'), processPath.LastIndexOf('\\'));
        var fileName = processPath[(separatorIndex + 1)..];
        return string.Equals(fileName, "dotnet", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(fileName, "dotnet.exe", StringComparison.OrdinalIgnoreCase);
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";
}
