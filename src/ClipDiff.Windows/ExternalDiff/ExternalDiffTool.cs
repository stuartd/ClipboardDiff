namespace ClipDiff.Windows.ExternalDiff;

internal sealed record ExternalDiffTool(
    string Id,
    string DisplayName,
    IReadOnlyList<string> ExecutableNames,
    Func<string, string, IReadOnlyList<string>> BuildArguments);

internal sealed record ExternalDiffToolChoice(
    ExternalDiffTool Tool,
    string ExecutablePath)
{
    public string DisplayName => Tool.DisplayName;
}
