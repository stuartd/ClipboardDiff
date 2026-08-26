namespace ClipDiff.Windows.ExternalDiff;

internal sealed record ExternalDiffTool(
    string Id,
    string DisplayName,
    IReadOnlyList<string> ExecutableNames,
    Func<string, string, string, string, IReadOnlyList<string>> ArgumentBuilder)
{
    public IReadOnlyList<string> BuildArguments(
        string previousPath,
        string currentPath,
        string previousLabel = DiffFormatting.DefaultPreviousLabel,
        string currentLabel = DiffFormatting.DefaultCurrentLabel) =>
        ArgumentBuilder(previousPath, currentPath, previousLabel, currentLabel);
}

internal sealed record ExternalDiffToolChoice(
    ExternalDiffTool Tool,
    string ExecutablePath)
{
    public string DisplayName => Tool.DisplayName;
}
