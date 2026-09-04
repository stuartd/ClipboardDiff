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
    public async Task ContextSelectedTerminalOutputsWithAnsiControlsRemainText()
    {
        const string previousContents =
            "\u001b[32m127.0.0.1:51842\u001b[0m: GET https://example.test/one\n" +
            "\u001b[36m<< 200 OK\u001b[0m 12b\n";
        const string currentContents =
            "\u001b[32m127.0.0.1:51842\u001b[0m: GET https://example.test/two\n" +
            "\u001b[33m<< 404 Not Found\u001b[0m 18b\n";
        var previousPath = await WriteTextFileAsync("previous-mitmdump.txt", previousContents);
        var currentPath = await WriteTextFileAsync("current-mitmdump.txt", currentContents);

        var result = await new CopiedFileTextReader().ReadValuesAsync([previousPath, currentPath]);

        Assert.AreEqual(2, result.Count);
        Assert.AreEqual(previousContents, result[0].Text);
        Assert.AreEqual(currentContents, result[1].Text);
    }

    [TestMethod]
    public async Task TextFileResultRetainsItsFileNameSeparatelyFromItsContents()
    {
        var path = await WriteTextFileAsync("source file.txt", "file contents");

        var result = await new CopiedFileTextReader().ReadFileAsync(path);

        Assert.IsNotNull(result);
        Assert.AreEqual("file contents", result.Text);
        Assert.AreEqual("source file.txt", result.FileName);
        Assert.AreEqual(Path.GetFullPath(path), result.FilePath);
    }

    [TestMethod]
    public async Task PeExecutableReturnsOnlyItsFileName()
    {
        var path = Path.Combine(_testDirectory, "ClipDiff.exe");
        await File.WriteAllBytesAsync(path, [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00]);

        var result = await new CopiedFileTextReader().ReadAsync([path]);

        Assert.AreEqual("ClipDiff.exe (binary file)", result);
    }

    [TestMethod]
    public async Task KnownBinaryExecutableExtensionIsNeverDecodedAsText()
    {
        var path = Path.Combine(_testDirectory, "tiny.com");
        await File.WriteAllBytesAsync(path, [0xEB, 0xFE]);

        var result = await new CopiedFileTextReader().ReadAsync([path]);

        Assert.AreEqual("tiny.com (binary file)", result);
    }

    [TestMethod]
    public async Task BinaryContentReturnsFileNameEvenWithTextExtension()
    {
        var path = Path.Combine(_testDirectory, "misleading.txt");
        await File.WriteAllBytesAsync(path, [0x01, 0x00, 0x02, 0x03, 0x7F]);

        var result = await new CopiedFileTextReader().ReadAsync([path]);

        Assert.AreEqual("misleading.txt (binary file)", result);
    }

    [TestMethod]
    public async Task KnownBinarySignatureIsNotDecodedAsTextWithMisleadingExtension()
    {
        const string printablePdf = "%PDF-1.7\n1 0 obj\n<< /Type /Catalog >>\nendobj\n%%EOF";
        var path = await WriteTextFileAsync("misleading.txt", printablePdf);

        var result = await new CopiedFileTextReader().ReadAsync([path]);

        Assert.AreEqual("misleading.txt (binary file)", result);
    }

    [TestMethod]
    public async Task KnownBinarySignatureMakesClassificationIndependentOfPayload()
    {
        var path = Path.Combine(_testDirectory, "image.data");
        await File.WriteAllBytesAsync(path, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var result = await new CopiedFileTextReader().ReadAsync([path]);

        Assert.AreEqual("image.data (binary file)", result);
    }

    [TestMethod]
    public async Task OrdinaryTextWithShortMagicLookingPrefixRemainsText()
    {
        const string contents = "MZ is the DOS executable signature.";
        var path = await WriteTextFileAsync("notes.txt", contents);

        var result = await new CopiedFileTextReader().ReadAsync([path]);

        Assert.AreEqual(contents, result);
    }

    [TestMethod]
    public async Task TextBomTakesPrecedenceOverAContainedBinarySignature()
    {
        const string contents = "%PDF- is the prefix used by PDF files.";
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        var path = Path.Combine(_testDirectory, "notes.txt");
        await File.WriteAllBytesAsync(path, encoding.GetPreamble().Concat(encoding.GetBytes(contents)).ToArray());

        var result = await new CopiedFileTextReader().ReadAsync([path]);

        Assert.AreEqual(contents, result);
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
            result.Select(value => value.Text).ToArray());
        CollectionAssert.AreEqual(
            new[] { "first.bat", "second.txt" },
            result.Select(value => value.FileName).ToArray());
    }

    [TestMethod]
    public async Task TwoCopiedFilesApplyFilenameFallbackIndependently()
    {
        var binary = Path.Combine(_testDirectory, "first.exe");
        await File.WriteAllBytesAsync(binary, [0x4D, 0x5A]);
        var missing = Path.Combine(_testDirectory, "second.txt");

        var result = await new CopiedFileTextReader().ReadValuesAsync([binary, missing]);

        CollectionAssert.AreEqual(
            new[] { "first.exe (binary file)", "second.txt (file not found)" },
            result.Select(value => value.Text).ToArray());
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
    public async Task EmptyMissingOrOversizedEntryIncludesFallbackReason()
    {
        var empty = Path.Combine(_testDirectory, "empty.txt");
        await File.WriteAllBytesAsync(empty, []);
        var oversized = await WriteTextFileAsync("large.txt", "five!");
        var missing = Path.Combine(_testDirectory, "missing.txt");
        var reader = new CopiedFileTextReader(maximumTextFileBytes: 4);

        Assert.AreEqual("empty.txt (empty file)", await reader.ReadAsync([empty]));
        Assert.AreEqual("large.txt (file too large)", await reader.ReadAsync([oversized]));
        Assert.AreEqual("missing.txt (file not found)", await reader.ReadAsync([missing]));
    }

    [TestMethod]
    public async Task CopiedDirectoryReturnsItsName()
    {
        var directory = Path.Combine(_testDirectory, "folder");
        Directory.CreateDirectory(directory);

        var result = await new CopiedFileTextReader().ReadAsync([directory]);

        Assert.AreEqual("folder (directory)", result);
    }

    [TestMethod]
    public async Task UnreadableFileIncludesFallbackReason()
    {
        var path = await WriteTextFileAsync("locked.txt", "contents");
        await using var exclusiveStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);

        var result = await new CopiedFileTextReader().ReadAsync([path]);

        Assert.AreEqual("locked.txt (file unreadable)", result);
    }

    private async Task<string> WriteTextFileAsync(string name, string contents)
    {
        var path = Path.Combine(_testDirectory, name);
        await File.WriteAllTextAsync(path, contents, new UTF8Encoding(false));
        return path;
    }
}
