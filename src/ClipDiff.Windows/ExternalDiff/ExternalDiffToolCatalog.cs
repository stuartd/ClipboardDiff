namespace ClipDiff.Windows.ExternalDiff;

internal static class ExternalDiffToolCatalog
{
    private const string PreviousTitle = "Previous clipboard";
    private const string CurrentTitle = "Current clipboard";

    public static IReadOnlyList<ExternalDiffTool> Tools { get; } =
    [
        new(
            "diffmerge",
            "SourceGear DiffMerge",
            ["sgdm.exe", "DiffMerge.exe"],
            (previous, current) =>
            [
                "-caption=ClipDiff",
                $"-t1={PreviousTitle}",
                $"-t2={CurrentTitle}",
                "-ro2",
                previous,
                current
            ]),
        new(
            "winmerge",
            "WinMerge",
            ["WinMergeU.exe", "WinMerge.exe"],
            (previous, current) =>
            [
                "/e", "/u", "/s-", "/wl", "/wr",
                "/dl", PreviousTitle,
                "/dr", CurrentTitle,
                previous,
                current
            ]),
        new(
            "meld",
            "Meld",
            ["Meld.exe", "meld.exe"],
            (previous, current) => [previous, current]),
        new(
            "kdiff3",
            "KDiff3",
            ["kdiff3.exe"],
            (previous, current) =>
            [
                "--L1", PreviousTitle,
                "--L2", CurrentTitle,
                previous,
                current
            ]),
        new(
            "beyond-compare",
            "Beyond Compare",
            ["BComp.exe", "BCompare.exe"],
            (previous, current) =>
            [
                "/solo", "/readonly",
                $"/lefttitle={PreviousTitle}",
                $"/righttitle={CurrentTitle}",
                previous,
                current
            ]),
        new(
            "araxis",
            "Araxis Merge",
            ["ConsoleCompare.exe", "Compare.exe"],
            (previous, current) =>
            [
                "/wait", "/readOnly", "/2",
                $"/title1:{PreviousTitle}",
                $"/title2:{CurrentTitle}",
                previous,
                current
            ]),
        new(
            "vscode",
            "Visual Studio Code",
            ["Code.exe", "Code - Insiders.exe"],
            (previous, current) => ["--diff", "--wait", previous, current]),
        new(
            "visual-studio",
            "Visual Studio",
            ["devenv.exe"],
            (previous, current) => ["/Diff", previous, current, PreviousTitle, CurrentTitle]),
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
            (previous, current) => [previous, current]),
        new(
            "examdiff",
            "ExamDiff Pro",
            ["ExamDiff.exe"],
            (previous, current) =>
            [
                previous,
                current,
                $"--left_display_name:{PreviousTitle}",
                $"--right_display_name:{CurrentTitle}"
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
            (previous, current) => [previous, current]);
    }

    private static IReadOnlyList<string> BuildTortoiseArguments(string previous, string current) =>
    [
        "/readonly",
        $"/base:{previous}",
        $"/mine:{current}",
        $"/basename:{PreviousTitle}",
        $"/minename:{CurrentTitle}"
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
