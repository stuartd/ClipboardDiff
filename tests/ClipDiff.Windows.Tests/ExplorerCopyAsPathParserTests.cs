using ClipDiff.Windows.Clipboard;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClipDiff.Windows.Tests;

[TestClass]
public sealed class ExplorerCopyAsPathParserTests
{
	[TestMethod]
	[DataRow("\"C:\\work\\script.bat\"", @"C:\work\script.bat", false)]
	[DataRow(@"C:\work\script.bat", @"C:\work\script.bat", true)]
	[DataRow("\"\\\\server\\share\\code.txt\"", @"\\server\share\code.txt", false)]
	[DataRow(@"\\server\share\code.txt", @"\\server\share\code.txt", true)]
	[DataRow(@"D:\source files\日本語 ☃.cs", @"D:\source files\日本語 ☃.cs", true)]
	[DataRow("\"D:\\source files\\日本語 ☃.cs\"", @"D:\source files\日本語 ☃.cs", false)]
	[DataRow("\"C:\\work\\script.bat\"\r\n", @"C:\work\script.bat", false)]
	[DataRow("C:\\work\\script.bat\n", @"C:\work\script.bat", true)]
	[DataRow(@"C:/work/script.bat", "C:/work/script.bat", true)]
	[DataRow(@"C:\work\file (copy).cs", @"C:\work\file (copy).cs", true)]
	[DataRow(@"C:\work\file(12).cs", @"C:\work\file(12).cs", true)]
	public void ParsesFullPathWithoutSplittingSpaces(string text, string expectedPath, bool expectedProbe)
	{
		var parsed = ExplorerCopyAsPathParser.TryParse(text, out var paths, out var requiresExistingFiles);

		Assert.IsTrue(parsed);
		CollectionAssert.AreEqual(new[] { expectedPath }, paths.ToArray());
		Assert.AreEqual(expectedProbe, requiresExistingFiles);
	}

	[TestMethod]
	[DataRow("\"C:\\old files\\code.txt\"\r\n\"D:\\new files\\日本語.txt\"", false)]
	[DataRow("C:\\old files\\code.txt\nD:\\new files\\日本語.txt\n", true)]
	[DataRow("\"C:\\old files\\code.txt\"\r\nD:\\new files\\日本語.txt\r\n", true)]
	[DataRow("C:\\old files\\code.txt\n\"D:\\new files\\日本語.txt\"", true)]
	public void ParsesPairsInClipboardOrder(string text, bool expectedProbe)
	{
		var parsed = ExplorerCopyAsPathParser.TryParse(text, out var paths, out var requiresExistingFiles);

		Assert.IsTrue(parsed);
		CollectionAssert.AreEqual(new[] { @"C:\old files\code.txt", @"D:\new files\日本語.txt" }, paths.ToArray());
		Assert.AreEqual(expectedProbe, requiresExistingFiles);
	}

	[TestMethod]
	[DataRow("")]
	[DataRow("\n")]
	[DataRow(@"work\script.bat")]
	[DataRow("\"work\\script.bat\"")]
	[DataRow(@"C:File.cs")]
	[DataRow(@"\work\File.cs")]
	[DataRow(@"/work/File.cs")]
	[DataRow(@"%USERPROFILE%\File.cs")]
	[DataRow(@"C:\%PROJECT%\File.cs")]
	[DataRow(@"$env:USERPROFILE\File.cs")]
	[DataRow("https://example.test/File.cs")]
	[DataRow("file:///C:/work/File.cs")]
	[DataRow("vscode-remote://ssh-remote+host/work/File.cs")]
	[DataRow(@"C:\work\File.cs:12")]
	[DataRow(@"C:\work\File.cs:12:3")]
	[DataRow("\"C:\\work\\File.cs:12:3\"")]
	[DataRow(@"C:\work\File.cs(12)")]
	[DataRow(@"C:\work\File.cs(12,3)")]
	[DataRow("\"C:\\work\\File.cs(12,3)\"")]
	[DataRow(@"C:\work\*.cs")]
	[DataRow(@"C:\work\File?.cs")]
	[DataRow("C:\\work\\File.cs\t")]
	[DataRow(@"\\server")]
	[DataRow(@"\\server\")]
	[DataRow("\"C:\\work\\script.bat")]
	[DataRow("C:\\work\\script.bat\"")]
	[DataRow("\"C:\\work\\scr\"ipt.bat\"")]
	[DataRow("\"\"C:\\work\\script.bat\"\"")]
	[DataRow("Compare \"C:\\work\\script.bat\"")]
	[DataRow("var path = \"C:\\work\\script.bat\";")]
	[DataRow("C:\\work\\File.cs\nprose")]
	[DataRow("prose\nC:\\work\\File.cs")]
	[DataRow("C:\\work\\File.cs\n\nD:\\work\\Other.cs")]
	public void RejectsCompletePayloadWhenAnyLineIsNotAFullPath(string text)
	{
		Assert.IsFalse(ExplorerCopyAsPathParser.TryParse(text, out var paths, out var requiresExistingFiles));
		Assert.AreEqual(0, paths.Count);
		Assert.IsFalse(requiresExistingFiles);
	}

	[TestMethod]
	[DataRow("\"C:\\one.txt\"\n\"C:\\two.txt\"\n\"C:\\three.txt\"", false)]
	[DataRow("C:\\one.txt\nC:\\two.txt\nC:\\three.txt", true)]
	public void RecognizesLongerListsForExistingFileCountPolicy(string text, bool expectedProbe)
	{
		Assert.IsTrue(ExplorerCopyAsPathParser.TryParse(text, out var paths, out var requiresExistingFiles));
		Assert.AreEqual(3, paths.Count);
		Assert.AreEqual(expectedProbe, requiresExistingFiles);
	}
}
