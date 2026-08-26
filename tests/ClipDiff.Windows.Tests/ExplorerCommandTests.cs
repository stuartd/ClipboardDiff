using ClipDiff.Windows.Explorer;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClipDiff.Windows.Tests;

[TestClass]
public sealed class ExplorerCommandTests
{
    [TestMethod]
    public void ContextCommandAcceptsExactlyOneSelectedFile()
    {
        var result = ExplorerContextCommandLine.TryGetSelectedFile(
            ["--compare-with-current", @"C:\Work files\current.txt"],
            out var filePath);

        Assert.IsTrue(result);
        Assert.AreEqual(@"C:\Work files\current.txt", filePath);
    }

    [TestMethod]
    public void ContextCommandRejectsNormalOrAmbiguousLaunches()
    {
        Assert.IsFalse(ExplorerContextCommandLine.TryGetSelectedFile([], out _));
        Assert.IsFalse(ExplorerContextCommandLine.TryGetSelectedFile(
            ["--compare-with-current", "one.txt", "two.txt"],
            out _));
        Assert.IsFalse(ExplorerContextCommandLine.TryGetSelectedFile(
            ["--compare-with-current", " "],
            out _));
    }

    [TestMethod]
    public void ShellCommandQuotesExecutableAndExplorerFilePlaceholder()
    {
        var command = ExplorerContextCommandLine.BuildShellCommand(
            @"C:\Program Files\ClipDiff\ClipDiff.exe",
            entryAssemblyPath: null);

        Assert.AreEqual(
            "\"C:\\Program Files\\ClipDiff\\ClipDiff.exe\" --compare-with-current \"%1\"",
            command);
    }

    [TestMethod]
    public void ShellCommandIncludesEntryAssemblyWhenHostedByDotnet()
    {
        var command = ExplorerContextCommandLine.BuildShellCommand(
            @"C:\Program Files\dotnet\dotnet.exe",
            @"C:\ClipDiff build\ClipDiff.dll");

        Assert.AreEqual(
            "\"C:\\Program Files\\dotnet\\dotnet.exe\" \"C:\\ClipDiff build\\ClipDiff.dll\" " +
            "--compare-with-current \"%1\"",
            command);
    }

    [TestMethod]
    public void ExplorerItemIncludesOnlyTheCurrentSourceFileName()
    {
        Assert.AreEqual(
            "Compare with current ClipDiff capture",
            ExplorerContextCommandLine.BuildDisplayName(null));
        Assert.AreEqual(
            "Compare with current ClipDiff capture — current.cs",
            ExplorerContextCommandLine.BuildDisplayName(@"C:\Work files\current.cs"));
    }

    [TestMethod]
    public async Task PipeProtocolRoundTripsUnicodePath()
    {
        const string expected = @"C:\Résumé files\雪.txt";
        await using var stream = new MemoryStream();

        await ExplorerCommandProtocol.WriteFilePathAsync(stream, expected);
        stream.Position = 0;
        var actual = await ExplorerCommandProtocol.ReadFilePathAsync(stream);

        Assert.AreEqual(expected, actual);
    }

    [TestMethod]
    public async Task PipeProtocolRejectsInvalidLength()
    {
        await using var stream = new MemoryStream([0xFF, 0xFF, 0xFF, 0xFF]);

        var result = await ExplorerCommandProtocol.ReadFilePathAsync(stream);

        Assert.IsNull(result);
    }
}
