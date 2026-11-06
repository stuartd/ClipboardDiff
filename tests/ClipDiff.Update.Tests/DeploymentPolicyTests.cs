using ClipDiff.Windows.Installation;
using ClipDiff.Windows.Settings;
using ClipDiff.Windows.Updates;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClipDiff.Update.Tests;

[TestClass]
public sealed class DeploymentPolicyTests
{
	[TestMethod]
	public void CleanupKeepsCurrentPeerAndUnrecognisedFiles()
	{
		var directory = Path.Combine(Path.GetTempPath(), "ClipDiff.Tests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);

		try
		{
			var current = "ClipDiff.ShellExtension." + new string('a', 64) + ".dll";
			var peer = "ClipDiff.ShellExtension." + new string('b', 64) + ".dll";
			var obsolete = "ClipDiff.ShellExtension." + new string('c', 64) + ".dll";
			var unknown = "ClipDiff.ShellExtension.my-file.dll";

			foreach (var name in new[] { current, peer, obsolete, unknown, "ClipDiff.ShellExtension.dll" })
			{
				File.WriteAllText(Path.Combine(directory, name), "placeholder");
			}

			ShellExtensionFiles.TryCleanup(directory, current, [Path.Combine(directory, peer)]);
			CollectionAssert.AreEquivalent(new[] { current, peer, unknown }, Directory.GetFiles(directory).Select(Path.GetFileName).ToArray());
		}
		finally
		{
			Directory.Delete(directory, true);
		}
	}

	[TestMethod]
	public void FailedCleanupIsNonfatal()
	{
		ShellExtensionFiles.TryCleanup(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), "ClipDiff.ShellExtension.dll", []);
	}

	[TestMethod]
	public void RejectsExtensionMetadataThatCouldEscapeTheInstallDirectory()
	{
		Assert.AreEqual("ClipDiff.ShellExtension.dll", ShellExtensionFiles.ResolveFileName("../other.dll"));
		var name = "ClipDiff.ShellExtension." + new string('a', 64) + ".dll";
		Assert.AreEqual(name, ShellExtensionFiles.ResolveFileName(name));
	}

	[TestMethod]
	public void UpdatePreferencesSurviveRestartWithoutDisturbingExistingPreferences()
	{
		var directory = Path.Combine(Path.GetTempPath(), "ClipDiff.Tests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);

		try
		{
			var path = Path.Combine(directory, "settings.json");
			File.WriteAllText(path, """{"SelectedExecutablePath":"Diff.exe","StartupPromptShown":true}""");
			var store = new ClipDiffSettingsStore(path);
			var original = store.Load();
			Assert.IsNull(original.UpdateChecks?.AutomaticChecksEnabled);
			var preferences = new UpdateCheckSettings(false, DateTimeOffset.UtcNow, "1.2.3");
			Assert.IsTrue(store.TrySave(original with { UpdateChecks = preferences }));
			var reloaded = new ClipDiffSettingsStore(path).Load();
			Assert.AreEqual(preferences, reloaded.UpdateChecks);
			Assert.AreEqual(original.SelectedExecutablePath, reloaded.SelectedExecutablePath);
			Assert.IsTrue(reloaded.StartupPromptShown);
		}
		finally
		{
			Directory.Delete(directory, true);
		}
	}
}
