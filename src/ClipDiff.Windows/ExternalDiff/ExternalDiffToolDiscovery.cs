using System.IO;
using System.Security;
using Microsoft.Win32;

namespace ClipDiff.Windows.ExternalDiff;

internal static class ExternalDiffToolDiscovery
{
	private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

	private static readonly (RegistryHive Hive, RegistryView View)[] RegistryLocations =
	[
		(RegistryHive.CurrentUser, RegistryView.Registry64),
		(RegistryHive.CurrentUser, RegistryView.Registry32),
		(RegistryHive.LocalMachine, RegistryView.Registry64),
		(RegistryHive.LocalMachine, RegistryView.Registry32)
	];

	public static IReadOnlyList<ExternalDiffToolChoice> FindInstalled(string? selectedExecutablePath = null) =>
		FindInstalled(
			new ExternalDiffRegistry(),
			Environment.GetEnvironmentVariable("PATH"),
			Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
			Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
			selectedExecutablePath);

	internal static IReadOnlyList<ExternalDiffToolChoice> FindInstalled(
		IExternalDiffRegistry registry,
		string? searchPath,
		string programFiles,
		string programFilesX86,
		string localAppData,
		string? selectedExecutablePath = null)
	{
		var registrations = ReadRegistrations(registry).ToArray();
		var choices = new List<ExternalDiffToolChoice>();
		var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach (var tool in ExternalDiffToolCatalog.Tools)
		{
			var executable = FindExecutable(tool, registry, registrations, searchPath,
				programFiles, programFilesX86, localAppData);

			if (executable is not null && seenPaths.Add(executable))
			{
				choices.Add(new ExternalDiffToolChoice(tool, executable));
			}
		}

		var selectedPath = NormalizePath(selectedExecutablePath);

		if (selectedPath is not null && File.Exists(selectedPath) && seenPaths.Add(selectedPath))
		{
			choices.Add(new ExternalDiffToolChoice(
				ExternalDiffToolCatalog.MatchExecutable(selectedPath),
				selectedPath));
		}

		return choices;
	}

	private static string? FindExecutable(
		ExternalDiffTool tool,
		IExternalDiffRegistry registry,
		IReadOnlyList<ToolRegistration> registrations,
		string? searchPath,
		string programFiles,
		string programFilesX86,
		string localAppData)
	{
		foreach (var executableName in tool.ExecutableNames.Distinct(StringComparer.OrdinalIgnoreCase))
		{
			var appPath = FindExistingExecutable(tool, AppPathCandidates(registry, executableName));

			if (appPath is not null)
			{
				return appPath;
			}

			var pathExecutable = FindExistingExecutable(tool, PathCandidates(searchPath, executableName));

			if (pathExecutable is not null)
			{
				return pathExecutable;
			}
		}

		var registeredExecutable = FindExistingExecutable(tool,
			registrations.Where(registration => registration.ToolId == tool.Id)
				.SelectMany(registration => RegistrationCandidates(tool, registration)),
			identifiedTool: true);

		return registeredExecutable ?? FindKnownExecutable(tool.Id, programFiles, programFilesX86, localAppData);
	}

	private static IEnumerable<string?> AppPathCandidates(IExternalDiffRegistry registry, string executableName)
	{
		foreach (var (hive, view) in RegistryLocations)
		{
			yield return TryReadRegistry(() => registry.ReadValue(hive, view,
				$@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{executableName}"));
		}
	}

	private static IEnumerable<string> PathCandidates(string? searchPath, string executableName)
	{
		if (string.IsNullOrWhiteSpace(searchPath))
		{
			yield break;
		}

		foreach (var directory in searchPath.Split(Path.PathSeparator,
			StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
		{
			var normalizedDirectory = NormalizePath(directory);

			if (normalizedDirectory is not null)
			{
				yield return Path.Combine(normalizedDirectory, executableName);
			}
		}
	}

	private static IEnumerable<ToolRegistration> ReadRegistrations(IExternalDiffRegistry registry)
	{
		foreach (var (hive, view) in RegistryLocations)
		{
			yield return new("winmerge", TryReadRegistry(() => registry.ReadValue(hive, view,
				@"SOFTWARE\Thingamahoochie\WinMerge", "Executable")), null);

			foreach (var version in new[] { "", " 5", " 4" })
			{
				yield return new("beyond-compare", TryReadRegistry(() => registry.ReadValue(hive, view,
					$@"SOFTWARE\Scooter Software\Beyond Compare{version}", "ExePath")), null);
			}

			var subKeys = TryReadRegistry(() => registry.ReadSubKeyNames(hive, view, UninstallKey)) ?? [];

			foreach (var subKey in subKeys)
			{
				var keyPath = $@"{UninstallKey}\{subKey}";
				var displayName = TryReadRegistry(() => registry.ReadValue(hive, view, keyPath, "DisplayName"));
				var tool = MatchDisplayName(displayName);

				if (tool is not null)
				{
					var installLocation = TryReadRegistry(() => registry.ReadValue(hive, view, keyPath, "InstallLocation"));
					yield return new(tool.Id, null, installLocation);
				}
			}
		}
	}

	private static ExternalDiffTool? MatchDisplayName(string? displayName)
	{
		if (string.IsNullOrWhiteSpace(displayName))
		{
			return null;
		}

		var name = displayName.Trim();

		return ExternalDiffToolCatalog.Tools.FirstOrDefault(tool =>
			DisplayNames(tool).Any(alias => name.Equals(alias, StringComparison.OrdinalIgnoreCase) ||
				name.StartsWith(alias + " ", StringComparison.OrdinalIgnoreCase)));
	}

	private static IEnumerable<string> DisplayNames(ExternalDiffTool tool) => tool.Id switch
	{
		"diffmerge" => [tool.DisplayName, "DiffMerge"],
		"vscode" => [tool.DisplayName, "Microsoft Visual Studio Code"],
		"visual-studio" => [tool.DisplayName, "Microsoft Visual Studio"],
		"tortoisegitmerge" => [tool.DisplayName, "TortoiseGit"],
		"tortoisemerge" => [tool.DisplayName, "TortoiseSVN"],
		_ => [tool.DisplayName]
	};

	private static IEnumerable<string?> RegistrationCandidates(ExternalDiffTool tool, ToolRegistration registration)
	{
		var executablePath = NormalizePath(registration.ExecutablePath);
		var installLocation = NormalizePath(registration.InstallLocation);

		if (executablePath is not null)
		{
			if (tool.Id == "beyond-compare" &&
				tool.ExecutableNames.Contains(Path.GetFileName(executablePath), StringComparer.OrdinalIgnoreCase))
			{
				yield return Path.Combine(Path.GetDirectoryName(executablePath)!, "BComp.exe");
			}

			yield return executablePath;
		}

		if (installLocation is not null)
		{
			foreach (var executableName in tool.ExecutableNames.Distinct(StringComparer.OrdinalIgnoreCase))
			{
				yield return Path.Combine(installLocation, executableName);
			}

			if (tool.Id is "kdiff3" or "tortoisegitmerge" or "tortoisemerge")
			{
				foreach (var executableName in tool.ExecutableNames)
				{
					yield return Path.Combine(installLocation, "bin", executableName);
				}
			}

			if (tool.Id == "visual-studio")
			{
				yield return Path.Combine(installLocation, "Common7", "IDE", "devenv.exe");
			}
		}
	}

	private static string? FindExistingExecutable(
		ExternalDiffTool tool,
		IEnumerable<string?> candidates,
		bool identifiedTool = false)
	{
		foreach (var candidate in candidates)
		{
			var path = NormalizePath(candidate);

			if (path is not null && File.Exists(path) &&
				(identifiedTool
					? tool.ExecutableNames.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
					: ExternalDiffToolCatalog.MatchExecutable(path).Id == tool.Id))
			{
				return path;
			}
		}

		return null;
	}

	private static string? NormalizePath(string? path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return null;
		}

		try
		{
			var expandedPath = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));

			return Path.IsPathFullyQualified(expandedPath) ? Path.GetFullPath(expandedPath) : null;
		}
		catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
		{
			return null;
		}
	}

	private static T? TryReadRegistry<T>(Func<T?> read) where T : class
	{
		try
		{
			return read();
		}
		catch (Exception exception) when (exception is SecurityException or UnauthorizedAccessException or IOException)
		{
			return null;
		}
	}

	internal static string? FindKnownExecutable(
		string toolId,
		string programFiles,
		string programFilesX86,
		string localAppData) =>
		KnownCandidates(toolId, programFiles, programFilesX86, localAppData).FirstOrDefault(File.Exists);

	private static IEnumerable<string> KnownCandidates(
		string toolId,
		string programFiles,
		string programFilesX86,
		string localAppData)
	{
		if (toolId == "visual-studio")
		{
			return VisualStudioCandidates(programFiles, programFilesX86);
		}

		string[] relativePaths = toolId switch
		{
			"diffmerge" => [@"SourceGear\Common\DiffMerge\sgdm.exe", @"SourceGear\DiffMerge\DiffMerge.exe"],
			"winmerge" => [@"WinMerge\WinMergeU.exe", @"WinMerge\WinMerge.exe"],
			"meld" => [@"Meld\Meld.exe"],
			"kdiff3" => [@"KDiff3\bin\kdiff3.exe", @"KDiff3\kdiff3.exe"],
			"beyond-compare" =>
			[
				@"Beyond Compare 5\BComp.exe", @"Beyond Compare 5\BCompare.exe",
				@"Beyond Compare 4\BComp.exe", @"Beyond Compare 4\BCompare.exe"
			],
			"araxis" => [@"Araxis\Araxis Merge\ConsoleCompare.exe", @"Araxis\Araxis Merge\Compare.exe"],
			"vscode" => [@"Microsoft VS Code\Code.exe", @"Microsoft VS Code Insiders\Code - Insiders.exe"],
			"tortoisegitmerge" => [@"TortoiseGit\bin\TortoiseGitMerge.exe"],
			"tortoisemerge" => [@"TortoiseSVN\bin\TortoiseMerge.exe"],
			"p4merge" => [@"Perforce\p4merge.exe"],
			"examdiff" => [@"ExamDiff Pro\ExamDiff.exe"],
			_ => []
		};

		var machineCandidates = relativePaths.SelectMany(relativePath =>
			UnderRoots([programFiles, programFilesX86], relativePath));
		var userRoot = string.IsNullOrWhiteSpace(localAppData) ? "" : Path.Combine(localAppData, "Programs");
		var userCandidates = relativePaths.SelectMany(relativePath =>
			UnderRoots([userRoot], toolId == "meld" ? @"Meld\meld.exe" : relativePath));

		return toolId == "vscode"
			? userCandidates.Concat(machineCandidates)
			: machineCandidates.Concat(userCandidates);
	}

	private static IEnumerable<string> UnderRoots(IEnumerable<string> roots, string relativePath) =>
		roots.Where(root => !string.IsNullOrWhiteSpace(root))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.Select(root => Path.Combine(root, relativePath.Replace('\\', Path.DirectorySeparatorChar)));

	private static IEnumerable<string> VisualStudioCandidates(string programFiles, string programFilesX86)
	{
		var editions = new[] { "Enterprise", "Professional", "Community" };
		var versions = new[] { "2022", "2019" };

		return versions.SelectMany(version => editions.SelectMany(edition =>
			UnderRoots([programFiles, programFilesX86],
				Path.Combine("Microsoft Visual Studio", version, edition, "Common7", "IDE", "devenv.exe"))));
	}

	private sealed record ToolRegistration(string ToolId, string? ExecutablePath, string? InstallLocation);
}
