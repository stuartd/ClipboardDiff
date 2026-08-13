using System.IO;
using Microsoft.Win32;

namespace ClipDiff.Windows.ExternalDiff;

internal static class ExternalDiffToolDiscovery
{
    public static IReadOnlyList<ExternalDiffToolChoice> FindInstalled(string? selectedExecutablePath = null)
    {
        var choices = new List<ExternalDiffToolChoice>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tool in ExternalDiffToolCatalog.Tools)
        {
            var executable = FindExecutable(tool);
            if (executable is not null && seenPaths.Add(executable))
            {
                choices.Add(new ExternalDiffToolChoice(tool, executable));
            }
        }

        if (!string.IsNullOrWhiteSpace(selectedExecutablePath))
        {
            var selectedPath = selectedExecutablePath;
            if (File.Exists(selectedPath) && seenPaths.Add(selectedPath))
            {
                choices.Add(new ExternalDiffToolChoice(
                    ExternalDiffToolCatalog.MatchExecutable(selectedPath),
                    selectedPath));
            }
        }

        return choices;
    }

    private static string? FindExecutable(ExternalDiffTool tool)
    {
        foreach (var executableName in tool.ExecutableNames)
        {
            var appPath = ReadAppPath(executableName);
            if (appPath is not null && MatchesTool(tool, appPath))
            {
                return appPath;
            }

            var pathExecutable = FindOnPath(executableName);
            if (pathExecutable is not null && MatchesTool(tool, pathExecutable))
            {
                return pathExecutable;
            }
        }

        return KnownCandidates(tool.Id).FirstOrDefault(File.Exists);
    }

    private static bool MatchesTool(ExternalDiffTool tool, string executablePath) =>
        ExternalDiffToolCatalog.MatchExecutable(executablePath).Id == tool.Id;

    private static string? ReadAppPath(string executableName)
    {
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    using var key = baseKey.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{executableName}");
                    if (key?.GetValue(null) is string path && File.Exists(path.Trim('"')))
                    {
                        return Path.GetFullPath(path.Trim('"'));
                    }
                }
                catch (System.Security.SecurityException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        return null;
    }

    private static string? FindOnPath(string executableName)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var candidate = Path.Combine(directory.Trim('"'), executableName);
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
            }
        }

        return null;
    }

    private static IEnumerable<string> KnownCandidates(string toolId)
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        return toolId switch
        {
            "diffmerge" => UnderProgramFiles(programFiles, programFilesX86,
                @"SourceGear\Common\DiffMerge\sgdm.exe",
                @"SourceGear\DiffMerge\DiffMerge.exe"),
            "winmerge" => UnderProgramFiles(programFiles, programFilesX86, @"WinMerge\WinMergeU.exe"),
            "meld" => UnderProgramFiles(programFiles, programFilesX86, @"Meld\Meld.exe"),
            "kdiff3" => UnderProgramFiles(programFiles, programFilesX86,
                @"KDiff3\bin\kdiff3.exe",
                @"KDiff3\kdiff3.exe"),
            "beyond-compare" => UnderProgramFiles(programFiles, programFilesX86,
                @"Beyond Compare 5\BComp.exe",
                @"Beyond Compare 5\BCompare.exe",
                @"Beyond Compare 4\BComp.exe",
                @"Beyond Compare 4\BCompare.exe"),
            "araxis" => UnderProgramFiles(programFiles, programFilesX86,
                @"Araxis\Araxis Merge\ConsoleCompare.exe",
                @"Araxis\Araxis Merge\Compare.exe"),
            "vscode" =>
            [
                Path.Combine(localAppData, @"Programs\Microsoft VS Code\Code.exe"),
                Path.Combine(localAppData, @"Programs\Microsoft VS Code Insiders\Code - Insiders.exe"),
                .. UnderProgramFiles(programFiles, programFilesX86,
                    @"Microsoft VS Code\Code.exe",
                    @"Microsoft VS Code Insiders\Code - Insiders.exe")
            ],
            "visual-studio" => VisualStudioCandidates(programFiles, programFilesX86),
            "tortoisegitmerge" => UnderProgramFiles(programFiles, programFilesX86, @"TortoiseGit\bin\TortoiseGitMerge.exe"),
            "tortoisemerge" => UnderProgramFiles(programFiles, programFilesX86, @"TortoiseSVN\bin\TortoiseMerge.exe"),
            "p4merge" => UnderProgramFiles(programFiles, programFilesX86, @"Perforce\p4merge.exe"),
            "examdiff" => UnderProgramFiles(programFiles, programFilesX86, @"ExamDiff Pro\ExamDiff.exe"),
            _ => []
        };
    }

    private static string[] UnderProgramFiles(string programFiles, string programFilesX86, params string[] relativePaths) =>
        relativePaths.SelectMany(relativePath => new[]
        {
            Path.Combine(programFiles, relativePath),
            Path.Combine(programFilesX86, relativePath)
        }).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private static IEnumerable<string> VisualStudioCandidates(string programFiles, string programFilesX86)
    {
        var editions = new[] { "Enterprise", "Professional", "Community" };
        var versions = new[] { "2022", "2019" };
        return versions.SelectMany(version => editions.SelectMany(edition => new[]
        {
            Path.Combine(programFiles, "Microsoft Visual Studio", version, edition, "Common7", "IDE", "devenv.exe"),
            Path.Combine(programFilesX86, "Microsoft Visual Studio", version, edition, "Common7", "IDE", "devenv.exe")
        }));
    }
}
