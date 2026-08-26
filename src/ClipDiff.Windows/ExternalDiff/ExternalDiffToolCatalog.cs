namespace ClipDiff.Windows.ExternalDiff;

internal static class ExternalDiffToolCatalog
{
    public static IReadOnlyList<ExternalDiffTool> Tools { get; } =
    [
        new(
            "diffmerge",
            "SourceGear DiffMerge",
            ["sgdm.exe", "DiffMerge.exe"],
            (previous, current, previousLabel, currentLabel) =>
            [
                "-caption=ClipDiff",
                $"-t1={previousLabel}",
                $"-t2={currentLabel}",
                "-ro2",
                previous,
                current
            ]),
        new(
            "winmerge",
            "WinMerge",
            ["WinMergeU.exe", "WinMerge.exe"],
            (previous, current, previousLabel, currentLabel) =>
            [
                "/e", "/u", "/s-", "/wl", "/wr",
                "/dl", previousLabel,
                "/dr", currentLabel,
                previous,
                current
            ]),
        new(
            "meld",
            "Meld",
            ["Meld.exe", "meld.exe"],
            (previous, current, _, _) => [previous, current]),
        new(
            "kdiff3",
            "KDiff3",
            ["kdiff3.exe"],
            (previous, current, previousLabel, currentLabel) =>
            [
                "--L1", previousLabel,
                "--L2", currentLabel,
                previous,
                current
            ]),
        new(
            "beyond-compare",
            "Beyond Compare",
            ["BComp.exe", "BCompare.exe"],
            (previous, current, previousLabel, currentLabel) =>
            [
                "/solo", "/readonly",
                $"/lefttitle={previousLabel}",
                $"/righttitle={currentLabel}",
                previous,
                current
            ]),
        new(
            "araxis",
            "Araxis Merge",
            ["ConsoleCompare.exe", "Compare.exe"],
            (previous, current, previousLabel, currentLabel) =>
            [
                "/wait", "/readOnly", "/2",
                $"/title1:{previousLabel}",
                $"/title2:{currentLabel}",
                previous,
                current
            ]),
        new(
            "vscode",
            "Visual Studio Code",
            ["Code.exe", "Code - Insiders.exe"],
            (previous, current, _, _) => ["--diff", "--wait", previous, current]),
        new(
            "visual-studio",
            "Visual Studio",
            ["devenv.exe"],
            (previous, current, previousLabel, currentLabel) =>
                ["/Diff", previous, current, previousLabel, currentLabel]),
        new(
            "tortoisegitmerge",
            "TortoiseGitMerge",
            ["TortoiseGitMerge.exe"],
            BuildTortoiseArguments),
        new(
            "tortoisemerge",
            "TortoiseMerge",
            ["TortoiseMerge.exe"],
            BuildTortoiseArguments),
        new(
            "p4merge",
            "P4Merge",
            ["p4merge.exe"],
            (previous, current, _, _) => [previous, current]),
        new(
            "examdiff",
            "ExamDiff Pro",
            ["ExamDiff.exe"],
            (previous, current, previousLabel, currentLabel) =>
            [
                previous,
                current,
                $"--left_display_name:{previousLabel}",
                $"--right_display_name:{currentLabel}"
            ])
    ];

    public static ExternalDiffTool MatchExecutable(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        var fileName = GetFileName(executablePath);
        var matches = Tools.Where(tool => tool.ExecutableNames.Contains(fileName, StringComparer.OrdinalIgnoreCase));
        foreach (var match in matches)
        {
            if (!IsAmbiguousExecutable(match, executablePath))
            {
                return match;
            }
        }

        return new ExternalDiffTool(
            "custom",
            $"Custom ({GetFileNameWithoutExtension(fileName)})",
            [fileName],
            (previous, current, _, _) => [previous, current]);
    }

    private static IReadOnlyList<string> BuildTortoiseArguments(
        string previous,
        string current,
        string previousLabel,
        string currentLabel) =>
    [
        "/readonly",
        $"/base:{previous}",
        $"/mine:{current}",
        $"/basename:{previousLabel}",
        $"/minename:{currentLabel}"
    ];

    private static bool IsAmbiguousExecutable(ExternalDiffTool tool, string executablePath) =>
        tool.Id == "araxis" &&
        GetFileName(executablePath).Equals("Compare.exe", StringComparison.OrdinalIgnoreCase) &&
        !executablePath.Contains("Araxis", StringComparison.OrdinalIgnoreCase);

    private static string GetFileName(string path)
    {
        var separatorIndex = path.LastIndexOfAny(['\\', '/']);
        return separatorIndex >= 0 ? path[(separatorIndex + 1)..] : path;
    }

    private static string GetFileNameWithoutExtension(string fileName)
    {
        var extensionIndex = fileName.LastIndexOf('.');
        return extensionIndex > 0 ? fileName[..extensionIndex] : fileName;
    }
}
