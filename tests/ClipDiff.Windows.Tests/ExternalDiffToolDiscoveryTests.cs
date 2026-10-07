using ClipDiff.Windows.ExternalDiff;
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
}
