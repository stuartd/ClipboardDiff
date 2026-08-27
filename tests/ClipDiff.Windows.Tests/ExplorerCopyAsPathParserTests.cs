using ClipDiff.Windows.Clipboard;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClipDiff.Windows.Tests;

[TestClass]
public sealed class ExplorerCopyAsPathParserTests
{
    [TestMethod]
    public void ParsesSingleQuotedDrivePath()
    {
        var parsed = ExplorerCopyAsPathParser.TryParse(
            "\"C:\\work\\script.bat\"",
            out var paths);

        Assert.IsTrue(parsed);
        CollectionAssert.AreEqual(new[] { @"C:\work\script.bat" }, paths.ToArray());
    }

    [TestMethod]
    public void ParsesTwoQuotedPathsInClipboardOrder()
    {
        var parsed = ExplorerCopyAsPathParser.TryParse(
            "\"C:\\old\\code.txt\"\r\n\"D:\\new\\code.txt\"",
            out var paths);

        Assert.IsTrue(parsed);
        CollectionAssert.AreEqual(
            new[] { @"C:\old\code.txt", @"D:\new\code.txt" },
            paths.ToArray());
    }

    [TestMethod]
    public void ParsesQuotedUncPath()
    {
        var parsed = ExplorerCopyAsPathParser.TryParse(
            "\"\\\\server\\share\\code.txt\"",
            out var paths);

        Assert.IsTrue(parsed);
        CollectionAssert.AreEqual(new[] { @"\\server\share\code.txt" }, paths.ToArray());
    }

    [TestMethod]
    public void AllowsTrailingClipboardLineBreak()
    {
        var parsed = ExplorerCopyAsPathParser.TryParse(
            "\"C:\\work\\script.bat\"\r\n",
            out var paths);

        Assert.IsTrue(parsed);
        CollectionAssert.AreEqual(new[] { @"C:\work\script.bat" }, paths.ToArray());
    }

    [TestMethod]
    public void LeavesUnquotedPathAsOrdinaryText()
    {
        Assert.IsFalse(ExplorerCopyAsPathParser.TryParse(@"C:\work\script.bat", out var paths));
        Assert.AreEqual(0, paths.Count);
    }

    [TestMethod]
    public void LeavesRelativeOrEmbeddedPathAsOrdinaryText()
    {
        Assert.IsFalse(ExplorerCopyAsPathParser.TryParse("\"work\\script.bat\"", out _));
        Assert.IsFalse(ExplorerCopyAsPathParser.TryParse("Compare \"C:\\work\\script.bat\"", out _));
    }

    [TestMethod]
    public void RecognizesMoreThanTwoPathsSoExistingFileCountPolicyCanIgnoreThem()
    {
        var parsed = ExplorerCopyAsPathParser.TryParse(
            "\"C:\\one.txt\"\n\"C:\\two.txt\"\n\"C:\\three.txt\"",
            out var paths);

        Assert.IsTrue(parsed);
        Assert.AreEqual(3, paths.Count);
    }
}
