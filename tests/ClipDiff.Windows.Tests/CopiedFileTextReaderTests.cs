using System.Text;
using ClipDiff.Windows.Clipboard;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClipDiff.Windows.Tests;

[TestClass]
public sealed class CopiedFileTextReaderTests
{
    private string _testDirectory = null!;

    [TestInitialize]
    public void Initialize()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "ClipDiff.CopiedFileTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDirectory);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, true);
        }
    }

    [TestMethod]
    public async Task TextBatchFileReturnsItsFullContents()
    {
        const string contents = "@echo off\r\necho hello\r\n";
        var path = await WriteTextFileAsync("hello.bat", contents);

        var result = await new CopiedFileTextReader().ReadAsync([path]);

        Assert.AreEqual(contents, result);
    }

    [TestMethod]
    public async Task PeExecutableReturnsOnlyItsFileName()
    {
        var path = Path.Combine(_testDirectory, "ClipDiff.exe");
        await File.WriteAllBytesAsync(path, [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00]);

        var result = await new CopiedFileTextReader().ReadAsync([path]);

        Assert.AreEqual("ClipDiff.exe", result);
    }

    [TestMethod]
    public async Task KnownBinaryExecutableExtensionIsNeverDecodedAsText()
    {
        var path = Path.Combine(_testDirectory, "tiny.com");
        await File.WriteAllBytesAsync(path, [0xEB, 0xFE]);

        var result = await new CopiedFileTextReader().ReadAsync([path]);

        Assert.AreEqual("tiny.com", result);
    }

    [TestMethod]
    public async Task BinaryContentReturnsFileNameEvenWithTextExtension()
    {
        var path = Path.Combine(_testDirectory, "misleading.txt");
        await File.WriteAllBytesAsync(path, [0x01, 0x00, 0x02, 0x03, 0x7F]);

        var result = await new CopiedFileTextReader().ReadAsync([path]);

        Assert.AreEqual("misleading.txt", result);
    }

    [TestMethod]
    public async Task Utf16TextFileIsDecodedWithoutItsBom()
    {
        const string contents = "echo snowman \u2603";
        var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(contents)).ToArray();
        var path = Path.Combine(_testDirectory, "unicode.cmd");
        await File.WriteAllBytesAsync(path, bytes);

        var result = await new CopiedFileTextReader().ReadAsync([path]);

        Assert.AreEqual(contents, result);
    }

    [TestMethod]
    public async Task Windows1252TextFileIsDecoded()
    {
        var path = Path.Combine(_testDirectory, "legacy.bat");
        await File.WriteAllBytesAsync(path,
            [0x40, 0x65, 0x63, 0x68, 0x6F, 0x20, 0x63, 0x61, 0x66, 0xE9]);

        var result = await new CopiedFileTextReader().ReadAsync([path]);

        Assert.AreEqual("@echo caf\u00e9", result);
    }

    [TestMethod]
    public async Task TwoCopiedTextFilesReturnTheirContentsAsComparisonValues()
    {
        var first = await WriteTextFileAsync("first.bat", "first contents");
        var second = await WriteTextFileAsync("second.txt", "second contents");

        var result = await new CopiedFileTextReader().ReadValuesAsync([first, second]);

        CollectionAssert.AreEqual(
            new[] { "first contents", "second contents" },
            result.ToArray());
    }

    [TestMethod]
    public async Task TwoCopiedFilesApplyFilenameFallbackIndependently()
    {
        var binary = Path.Combine(_testDirectory, "first.exe");
        await File.WriteAllBytesAsync(binary, [0x4D, 0x5A]);
        var missing = Path.Combine(_testDirectory, "second.txt");

        var result = await new CopiedFileTextReader().ReadValuesAsync([binary, missing]);

        CollectionAssert.AreEqual(new[] { "first.exe", "second.txt" }, result.ToArray());
    }

    [TestMethod]
    public async Task MoreThanTwoCopiedFilesAreIgnored()
    {
        var first = await WriteTextFileAsync("first.bat", "first contents");
        var second = await WriteTextFileAsync("second.txt", "second contents");
        var third = await WriteTextFileAsync("third.ps1", "third contents");

        var result = await new CopiedFileTextReader().ReadValuesAsync([first, second, third]);

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public async Task EmptyUnreadableOrOversizedEntryFallsBackToFileName()
    {
        var empty = Path.Combine(_testDirectory, "empty.txt");
        await File.WriteAllBytesAsync(empty, []);
        var oversized = await WriteTextFileAsync("large.txt", "five!");
        var missing = Path.Combine(_testDirectory, "missing.txt");
        var reader = new CopiedFileTextReader(maximumTextFileBytes: 4);

        Assert.AreEqual("empty.txt", await reader.ReadAsync([empty]));
        Assert.AreEqual("large.txt", await reader.ReadAsync([oversized]));
        Assert.AreEqual("missing.txt", await reader.ReadAsync([missing]));
    }

    [TestMethod]
    public async Task CopiedDirectoryReturnsItsName()
    {
        var directory = Path.Combine(_testDirectory, "folder");
        Directory.CreateDirectory(directory);

        var result = await new CopiedFileTextReader().ReadAsync([directory]);

        Assert.AreEqual("folder", result);
    }

    private async Task<string> WriteTextFileAsync(string name, string contents)
    {
        var path = Path.Combine(_testDirectory, name);
        await File.WriteAllTextAsync(path, contents, new UTF8Encoding(false));
        return path;
    }
}
