using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using ClipDiff.Windows.Clipboard;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClipDiff.Windows.Tests;

[TestClass]
public sealed class ShellShortcutTargetReaderTests
{
	private string testDirectory = null!;

	[TestInitialize]
	public void Initialize()
	{
		testDirectory = Path.Combine(Path.GetTempPath(), "ClipDiff.ShellLinkTests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(testDirectory);
	}

	[TestCleanup]
	public void Cleanup()
	{
		Directory.Delete(testDirectory, recursive: true);
	}

	[TestMethod]
	public async Task RealShellShortcutReadsUnicodeTargetWithoutChangingLinkOrUsingArguments()
	{
		var target = Path.Combine(testDirectory, "日本語 saved text.txt");
		await File.WriteAllTextAsync(target, "saved contents ☃\r\n");
		var shortcut = CreateShortcut("copied.lnk", target);
		var originalShortcut = await File.ReadAllBytesAsync(shortcut);

		// Match the application's background file-reading path.
		var result = await Task.Run(() => new CopiedFileTextReader().ReadFileAsync(shortcut).AsTask());

		Assert.IsNotNull(result);
		Assert.AreEqual("saved contents ☃\r\n", result.Text);
		Assert.AreEqual(Path.GetFileName(target), result.FileName);
		Assert.AreEqual(target, result.FilePath);
		CollectionAssert.AreEqual(originalShortcut, await File.ReadAllBytesAsync(shortcut));
	}

	[TestMethod]
	[DataRow("target.txt", "file not found")]
	[DataRow("program.exe", "binary file")]
	[DataRow("folder", "directory")]
	public async Task RealShellShortcutPreservesTargetFallback(string name, string reason)
	{
		var target = Path.Combine(testDirectory, name);
		await File.WriteAllTextAsync(target, "placeholder");
		var shortcut = CreateShortcut("copied.lnk", target);
		File.Delete(target);

		if (name == "folder")
		{
			Directory.CreateDirectory(target);
		}

		var result = await new CopiedFileTextReader().ReadFileAsync(shortcut);

		Assert.IsNotNull(result);
		Assert.AreEqual($"{name} ({reason})", result.Text);
		Assert.AreEqual(target, result.FilePath);
	}

	[TestMethod]
	public async Task InvalidShellShortcutIsNotDecodedAsOrdinaryText()
	{
		var shortcut = Path.Combine(testDirectory, "invalid.lnk");
		await File.WriteAllTextAsync(shortcut, "This is not a Shell shortcut.");

		var result = await new CopiedFileTextReader().ReadFileAsync(shortcut);

		Assert.IsNotNull(result);
		Assert.AreEqual("invalid.lnk (file unreadable)", result.Text);
	}

	private string CreateShortcut(string name, string target)
	{
		var shortcut = Path.Combine(testDirectory, name);
		var shellLink = (ShellShortcutTargetReader.IShellLinkW)Activator.CreateInstance(
			Type.GetTypeFromCLSID(ShellShortcutTargetReader.ClassId, throwOnError: true)!)!;

		try
		{
			shellLink.SetPath(target);
			shellLink.SetArguments("ignored argument.txt");
			((IPersistFile)shellLink).Save(shortcut, true);
		}
		finally
		{
			Marshal.FinalReleaseComObject(shellLink);
		}

		return shortcut;
	}
}
