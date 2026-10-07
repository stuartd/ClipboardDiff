using ClipDiff.Windows.ExternalDiff;
using Microsoft.Win32;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClipDiff.Windows.Tests;

[TestClass]
public sealed class ExternalDiffToolDiscoveryTests
{
	[TestMethod]
	[DataRow(true, false, false, "user")]
	[DataRow(false, true, false, "machine")]
	[DataRow(false, false, true, "x86")]
	[DataRow(true, true, true, "machine")]
	[DataRow(true, false, true, "x86")]
	[DataRow(false, false, false, null)]
	public void DetectsMeldInExistingInstallLocations(
		bool userInstalled,
		bool machineInstalled,
		bool x86Installed,
		string? expectedLocation)
	{
		var root = Path.Combine(Path.GetTempPath(), "ClipDiff.Tests", Guid.NewGuid().ToString("N"));
		var programFiles = Path.Combine(root, "Program Files");
		var programFilesX86 = Path.Combine(root, "Program Files (x86)");
		var localAppData = Path.Combine(root, "User Profile", "AppData", "Local");
		var installs = new[]
		{
			(Location: "user", Installed: userInstalled, Path: Path.Combine(localAppData, "Programs", "Meld", "meld.exe")),
			(Location: "machine", Installed: machineInstalled, Path: Path.Combine(programFiles, "Meld", "Meld.exe")),
			(Location: "x86", Installed: x86Installed, Path: Path.Combine(programFilesX86, "Meld", "Meld.exe"))
		};

		Directory.CreateDirectory(root);

		try
		{
			foreach (var install in installs.Where(install => install.Installed))
			{
				Directory.CreateDirectory(Path.GetDirectoryName(install.Path)!);
				File.Create(install.Path).Dispose();
			}

			var executable = ExternalDiffToolDiscovery.FindKnownExecutable(
				"meld", programFiles, programFilesX86, localAppData);
			var expectedPath = installs.FirstOrDefault(install => install.Location == expectedLocation).Path;

			Assert.AreEqual(expectedPath, executable);
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[TestMethod]
	[DataRow("diffmerge", @"SourceGear\Common\DiffMerge\sgdm.exe")]
	[DataRow("winmerge", @"WinMerge\WinMergeU.exe")]
	[DataRow("meld", @"Meld\meld.exe")]
	[DataRow("kdiff3", @"KDiff3\bin\kdiff3.exe")]
	[DataRow("beyond-compare", @"Beyond Compare 5\BComp.exe")]
	[DataRow("araxis", @"Araxis\Araxis Merge\ConsoleCompare.exe")]
	[DataRow("vscode", @"Microsoft VS Code\Code.exe")]
	[DataRow("tortoisegitmerge", @"TortoiseGit\bin\TortoiseGitMerge.exe")]
	[DataRow("tortoisemerge", @"TortoiseSVN\bin\TortoiseMerge.exe")]
	[DataRow("p4merge", @"Perforce\p4merge.exe")]
	[DataRow("examdiff", @"ExamDiff Pro\ExamDiff.exe")]
	public void FindsSupportedViewersUnderPerUserPrograms(string toolId, string relativePath)
	{
		using var installation = new TestInstallation();
		var executable = installation.CreateExecutable(Path.Combine("User", "Programs", relativePath));

		var choice = installation.FindInstalled().Single();

		Assert.AreEqual(toolId, choice.Tool.Id);
		Assert.AreEqual(executable, choice.ExecutablePath);
	}

	[TestMethod]
	[DataRow("SourceGear DiffMerge 4.2", "diffmerge", "sgdm.exe")]
	[DataRow("WinMerge 2.16.54", "winmerge", "WinMergeU.exe")]
	[DataRow("Meld", "meld", "meld.exe")]
	[DataRow("KDiff3 1.12", "kdiff3", @"bin\kdiff3.exe")]
	[DataRow("Beyond Compare 5", "beyond-compare", "BComp.exe")]
	[DataRow("Araxis Merge 2026", "araxis", "Compare.exe")]
	[DataRow("Microsoft Visual Studio Code (User)", "vscode", "Code.exe")]
	[DataRow("Microsoft Visual Studio Code Insiders (User)", "vscode", "Code - Insiders.exe")]
	[DataRow("Visual Studio Community 2026", "visual-studio", @"Common7\IDE\devenv.exe")]
	[DataRow("TortoiseGitMerge", "tortoisegitmerge", "TortoiseGitMerge.exe")]
	[DataRow("TortoiseGit 2.18", "tortoisegitmerge", @"bin\TortoiseGitMerge.exe")]
	[DataRow("TortoiseMerge", "tortoisemerge", "TortoiseMerge.exe")]
	[DataRow("TortoiseSVN 1.14", "tortoisemerge", @"bin\TortoiseMerge.exe")]
	[DataRow("P4Merge", "p4merge", "p4merge.exe")]
	[DataRow("ExamDiff Pro 16", "examdiff", "ExamDiff.exe")]
	public void FindsCustomInstallDirectoriesAndUsesTheRegisteredToolProfile(
		string displayName, string toolId, string relativePath)
	{
		using var installation = new TestInstallation();
		var customDirectory = Path.Combine(installation.Root, "Custom Tools", "秘密");
		var executable = installation.CreateExecutable(Path.Combine(customDirectory, relativePath));
		installation.Registry.RegisterInstallation(displayName, customDirectory);

		var choice = installation.FindInstalled(executable).Single();

		Assert.AreEqual(toolId, choice.Tool.Id);
		Assert.AreEqual(executable, choice.ExecutablePath, ignoreCase: true);
	}

	[TestMethod]
	[DataRow(RegistryHive.CurrentUser, RegistryView.Registry64)]
	[DataRow(RegistryHive.CurrentUser, RegistryView.Registry32)]
	[DataRow(RegistryHive.LocalMachine, RegistryView.Registry64)]
	[DataRow(RegistryHive.LocalMachine, RegistryView.Registry32)]
	public void ReadsInstallLocationsInBothHivesAndRegistryViews(RegistryHive hive, RegistryView view)
	{
		using var installation = new TestInstallation();
		var executable = installation.CreateExecutable(Path.Combine("Custom Tools", "WinMergeU.exe"));
		installation.Registry.RegisterInstallation("winmerge 2.16", Path.GetDirectoryName(executable)!, hive, view);

		Assert.AreEqual(executable, installation.FindInstalled().Single().ExecutablePath);
		Assert.AreEqual(4, installation.Registry.SubKeyReadCount);
	}

	[TestMethod]
	[DataRow("winmerge", @"SOFTWARE\Thingamahoochie\WinMerge", "Executable", "WinMergeU.exe")]
	[DataRow("beyond-compare", @"SOFTWARE\Scooter Software\Beyond Compare", "ExePath", "BCompare.exe")]
	[DataRow("beyond-compare", @"SOFTWARE\Scooter Software\Beyond Compare 5", "ExePath", "BCompare.exe")]
	[DataRow("beyond-compare", @"SOFTWARE\Scooter Software\Beyond Compare 4", "ExePath", "BCompare.exe")]
	public void ReadsVendorExecutablePaths(string toolId, string keyPath, string valueName, string fileName)
	{
		using var installation = new TestInstallation();
		var executable = installation.CreateExecutable(Path.Combine("Custom Tools", fileName));
		installation.Registry.SetValue(RegistryHive.CurrentUser, RegistryView.Registry32,
			keyPath, valueName, $" \"{executable}\" ");

		var choice = installation.FindInstalled().Single();

		Assert.AreEqual(toolId, choice.Tool.Id);
		Assert.AreEqual(executable, choice.ExecutablePath);
	}

	[TestMethod]
	public void BeyondComparePrefersBCompAlongsideRegisteredBCompare()
	{
		using var installation = new TestInstallation();
		var bcompare = installation.CreateExecutable(Path.Combine("Custom Tools", "BCompare.exe"));
		var bcomp = installation.CreateExecutable(Path.Combine("Custom Tools", "BComp.exe"));
		installation.Registry.SetValue(RegistryHive.CurrentUser, RegistryView.Registry64,
			@"SOFTWARE\Scooter Software\Beyond Compare", "ExePath", bcompare);

		Assert.AreEqual(bcomp, installation.FindInstalled().Single().ExecutablePath);
	}

	[TestMethod]
	public void PreservesAppPathsAndPathPriorityBeforeNewRegistryAndFolderFallbacks()
	{
		using var installation = new TestInstallation();
		var appPath = installation.CreateExecutable(Path.Combine("App Paths", "WinMergeU.exe"));
		var path = installation.CreateExecutable(Path.Combine("On Path", "WinMergeU.exe"));
		var vendorPath = installation.CreateExecutable(Path.Combine("Vendor", "WinMergeU.exe"));
		var uninstallPath = installation.CreateExecutable(Path.Combine("Installed", "WinMergeU.exe"));
		var knownPath = installation.CreateExecutable(Path.Combine("Program Files", "WinMerge", "WinMergeU.exe"));
		installation.Registry.SetValue(RegistryHive.CurrentUser, RegistryView.Registry64,
			@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\WinMergeU.exe", null, appPath);
		installation.Registry.SetValue(RegistryHive.CurrentUser, RegistryView.Registry64,
			@"SOFTWARE\Thingamahoochie\WinMerge", "Executable", vendorPath);
		installation.Registry.RegisterInstallation("WinMerge", Path.GetDirectoryName(uninstallPath)!);

		foreach (var expectedPath in new[] { appPath, path, vendorPath, uninstallPath, knownPath })
		{
			Assert.AreEqual(expectedPath,
				installation.FindInstalled(searchPath: $"\"{Path.GetDirectoryName(path)}\"").Single().ExecutablePath);

			File.Delete(expectedPath);
		}

		Assert.AreEqual(0, installation.FindInstalled().Count);
	}

	[TestMethod]
	public void InvalidAppPathDoesNotHideAValidRegistrationInAnotherHive()
	{
		using var installation = new TestInstallation();
		var unrelated = installation.CreateExecutable("Other.exe");
		var executable = installation.CreateExecutable(Path.Combine("Custom Tools", "WinMergeU.exe"));
		const string keyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\WinMergeU.exe";
		installation.Registry.SetValue(RegistryHive.CurrentUser, RegistryView.Registry64, keyPath, null, unrelated);
		installation.Registry.SetValue(RegistryHive.LocalMachine, RegistryView.Registry32, keyPath, null, executable);

		Assert.AreEqual(executable, installation.FindInstalled().Single().ExecutablePath);
	}

	[TestMethod]
	[DataRow(null)]
	[DataRow("")]
	[DataRow("relative folder")]
	[DataRow("bad\0path")]
	[DataRow("\"path\" --argument")]
	public void IgnoresMalformedOrMissingInstallLocations(string? installLocation)
	{
		using var installation = new TestInstallation();
		installation.Registry.RegisterInstallation("WinMerge", installLocation);

		Assert.AreEqual(0, installation.FindInstalled().Count);
	}

	[TestMethod]
	public void SkipsStaleRegistrationsAndDoesNotMisidentifyOtherPrograms()
	{
		using var installation = new TestInstallation();
		var unrelatedDirectory = Path.Combine(installation.Root, "Other Program");
		installation.CreateExecutable(Path.Combine(unrelatedDirectory, "Compare.exe"));
		installation.Registry.RegisterInstallation("Other Merge", unrelatedDirectory);
		installation.Registry.RegisterInstallation("WinMerge", Path.Combine(installation.Root, "Removed"));
		var executable = installation.CreateExecutable(Path.Combine("Custom Tools", "WinMergeU.exe"));
		installation.Registry.RegisterInstallation("WinMerge 2.16", Path.GetDirectoryName(executable)!,
			RegistryHive.LocalMachine, RegistryView.Registry32);

		Assert.AreEqual(executable, installation.FindInstalled().Single().ExecutablePath);
	}

	[TestMethod]
	public void VendorPathsMustNameTheExpectedExecutable()
	{
		using var installation = new TestInstallation();
		var executable = installation.CreateExecutable("Other.exe");
		installation.Registry.SetValue(RegistryHive.CurrentUser, RegistryView.Registry64,
			@"SOFTWARE\Thingamahoochie\WinMerge", "Executable", executable);

		Assert.AreEqual(0, installation.FindInstalled().Count);
	}

	[TestMethod]
	[DataRow(0)]
	[DataRow(1)]
	[DataRow(2)]
	public void RegistryFailuresStillAllowKnownInstallLocations(int failureKind)
	{
		using var installation = new TestInstallation();
		var executable = installation.CreateExecutable(Path.Combine("User", "Programs", "WinMerge", "WinMergeU.exe"));
		installation.Registry.Failure = failureKind switch
		{
			0 => new UnauthorizedAccessException(),
			1 => new IOException(),
			_ => new System.Security.SecurityException()
		};

		Assert.AreEqual(executable, installation.FindInstalled().Single().ExecutablePath);
	}

	[TestMethod]
	[DoNotParallelize]
	public void ExpandsEnvironmentVariablesAndQuotedInstallLocations()
	{
		using var installation = new TestInstallation();
		const string variableName = "CLIPDIFF_DISCOVERY_TEST_ROOT";
		var previousValue = Environment.GetEnvironmentVariable(variableName);
		var executable = installation.CreateExecutable(Path.Combine("Custom Tools", "WinMergeU.exe"));

		try
		{
			Environment.SetEnvironmentVariable(variableName, Path.GetDirectoryName(executable));
			installation.Registry.RegisterInstallation("WinMerge", $"\"%{variableName}%\"");

			Assert.AreEqual(executable, installation.FindInstalled().Single().ExecutablePath);
		}
		finally
		{
			Environment.SetEnvironmentVariable(variableName, previousValue);
		}
	}

	[TestMethod]
	public void ManualSelectionCanFindAnUnregisteredPortableViewer()
	{
		using var installation = new TestInstallation();
		var executable = installation.CreateExecutable(Path.Combine("Portable Tools", "MyDiff.exe"));

		var choice = installation.FindInstalled(executable).Single();

		Assert.AreEqual("custom", choice.Tool.Id);
		Assert.AreEqual(executable, choice.ExecutablePath);
	}

	private sealed class TestInstallation : IDisposable
	{
		public string Root { get; } = Path.Combine(Path.GetTempPath(), "ClipDiff.Tests", Guid.NewGuid().ToString("N"));

		public FakeRegistry Registry { get; } = new();

		public string CreateExecutable(string relativePath)
		{
			var path = Path.Combine(Root, relativePath.Replace('\\', Path.DirectorySeparatorChar));
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			File.Create(path).Dispose();

			return path;
		}

		public IReadOnlyList<ExternalDiffToolChoice> FindInstalled(string? selectedPath = null, string? searchPath = null) =>
			ExternalDiffToolDiscovery.FindInstalled(Registry, searchPath,
				Path.Combine(Root, "Program Files"), Path.Combine(Root, "Program Files (x86)"),
				Path.Combine(Root, "User"), selectedPath);

		public void Dispose()
		{
			if (Directory.Exists(Root))
			{
				Directory.Delete(Root, recursive: true);
			}
		}
	}

	private sealed class FakeRegistry : IExternalDiffRegistry
	{
		private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
		private readonly Dictionary<(RegistryHive, RegistryView, string, string), string?> values = [];
		private readonly Dictionary<(RegistryHive, RegistryView, string), List<string>> subKeys = [];

		public Exception? Failure { get; set; }

		public int SubKeyReadCount { get; private set; }

		public void SetValue(RegistryHive hive, RegistryView view, string keyPath, string? valueName, string? value) =>
			values[(hive, view, keyPath.ToUpperInvariant(), (valueName ?? "").ToUpperInvariant())] = value;

		public void RegisterInstallation(
			string displayName,
			string? installLocation,
			RegistryHive hive = RegistryHive.CurrentUser,
			RegistryView view = RegistryView.Registry64)
		{
			var key = (hive, view, UninstallKey);

			if (!subKeys.TryGetValue(key, out var names))
			{
				names = [];
				subKeys.Add(key, names);
			}

			var name = Guid.NewGuid().ToString("N");
			names.Add(name);
			SetValue(hive, view, $@"{UninstallKey}\{name}", "DisplayName", displayName);
			SetValue(hive, view, $@"{UninstallKey}\{name}", "InstallLocation", installLocation);
		}

		public string? ReadValue(RegistryHive hive, RegistryView view, string keyPath, string? valueName = null)
		{
			if (Failure is not null)
			{
				throw Failure;
			}

			return values.GetValueOrDefault((hive, view, keyPath.ToUpperInvariant(), (valueName ?? "").ToUpperInvariant()));
		}

		public IReadOnlyList<string> ReadSubKeyNames(RegistryHive hive, RegistryView view, string keyPath)
		{
			SubKeyReadCount++;

			if (Failure is not null)
			{
				throw Failure;
			}

			return subKeys.GetValueOrDefault((hive, view, keyPath)) ?? [];
		}
	}
}
