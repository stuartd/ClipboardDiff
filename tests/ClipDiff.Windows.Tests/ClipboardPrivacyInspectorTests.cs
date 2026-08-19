using ClipDiff.Windows.Clipboard;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClipDiff.Windows.Tests;

[TestClass]
public sealed class ClipboardPrivacyInspectorTests
{
    private const uint Exclude = 100;
    private const uint History = 101;
    private const uint Cloud = 102;
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void ExcludeMarkerPreventsTextRequest()
    {
        var clipboard = FakeClipboard.WithText("secret");
        clipboard.Formats.Add(Exclude);

        var result = Inspect(clipboard);

        Assert.AreEqual(ClipboardObservationKind.Sensitive, result.Kind);
        Assert.AreEqual(0, clipboard.TextReadCount);
    }

    [TestMethod]
    [DataRow(History)]
    [DataRow(Cloud)]
    public void ZeroPrivacyDwordExcludesText(uint format)
    {
        var clipboard = FakeClipboard.WithText("secret");
        clipboard.Dwords[format] = 0;

        var result = Inspect(clipboard);

        Assert.AreEqual(ClipboardObservationKind.Sensitive, result.Kind);
        Assert.AreEqual(0, clipboard.TextReadCount);
    }

    [TestMethod]
    [DataRow(History)]
    [DataRow(Cloud)]
    public void OnePrivacyDwordAllowsText(uint format)
    {
        var clipboard = FakeClipboard.WithText("ordinary");
        clipboard.Dwords[format] = 1;

        var result = Inspect(clipboard);

        Assert.AreEqual(ClipboardObservationKind.Text, result.Kind);
        Assert.AreEqual("ordinary", result.Text);
    }

    [TestMethod]
    [DataRow(History)]
    [DataRow(Cloud)]
    public void MalformedPrivacyDwordIsExcludedConservatively(uint format)
    {
        var clipboard = FakeClipboard.WithText("secret");
        clipboard.MalformedDwords.Add(format);

        var result = Inspect(clipboard);

        Assert.AreEqual(ClipboardObservationKind.Sensitive, result.Kind);
        Assert.AreEqual(0, clipboard.TextReadCount);
    }

    [TestMethod]
    public void NoPrivacyFormatsAllowsUnicodeText()
    {
        var result = Inspect(FakeClipboard.WithText("ordinary"));

        Assert.AreEqual(ClipboardObservationKind.Text, result.Kind);
        Assert.AreEqual("ordinary", result.Text);
    }

    [TestMethod]
    public void PrivacyMarkerPlusTextNeverReadsText()
    {
        var clipboard = FakeClipboard.WithText("secret");
        clipboard.Dwords[History] = 0;

        Inspect(clipboard);

        Assert.AreEqual(0, clipboard.TextReadCount);
    }

    [TestMethod]
    public void FormatEnumerationFailureFailsClosedWithoutReadingText()
    {
        var clipboard = FakeClipboard.WithText("secret");
        clipboard.FormatInspectionSucceeds = false;

        var result = Inspect(clipboard);

        Assert.AreEqual(ClipboardObservationKind.InspectionFailed, result.Kind);
        Assert.AreEqual(0, clipboard.TextReadCount);
    }

    [TestMethod]
    public void EmptyClipboardIsAnExplicitClear()
    {
        var clipboard = new FakeClipboard { HasAnyFormats = false };

        Assert.AreEqual(ClipboardObservationKind.ExplicitClear, Inspect(clipboard).Kind);
    }

    [TestMethod]
    public void EmptyUnicodeTextIsAnExplicitClear()
    {
        Assert.AreEqual(ClipboardObservationKind.ExplicitClear, Inspect(FakeClipboard.WithText(string.Empty)).Kind);
    }

    [TestMethod]
    public void NonTextClipboardIsIgnored()
    {
        var clipboard = new FakeClipboard();
        clipboard.Formats.Add(500);

        Assert.AreEqual(ClipboardObservationKind.NonText, Inspect(clipboard).Kind);
    }

    [TestMethod]
    public void TextReadFailureRequestsARetryOutcome()
    {
        var clipboard = FakeClipboard.WithText("not returned");
        clipboard.TextReadSucceeds = false;

        Assert.AreEqual(ClipboardObservationKind.InspectionFailed, Inspect(clipboard).Kind);
    }

    [TestMethod]
    public void CopiedFilesAreReturnedWithoutReadingIncidentalUnicodeText()
    {
        var clipboard = FakeClipboard.WithText("incidental path text");
        clipboard.Formats.Add(ClipboardPrivacyInspector.NativeFileDropFormat);
        clipboard.FilePaths = [@"C:\work\script.bat"];

        var result = InspectRaw(clipboard);

        Assert.IsTrue(result is ClipboardInspection.CopiedFiles);
        var copiedFiles = (ClipboardInspection.CopiedFiles)result;
        CollectionAssert.AreEqual(new[] { @"C:\work\script.bat" }, copiedFiles.FilePaths.ToArray());
        Assert.AreEqual(0, clipboard.TextReadCount);
        Assert.AreEqual(1, clipboard.FilePathReadCount);
    }

    [TestMethod]
    public void PrivacyMarkerPreventsCopiedFilePathRequest()
    {
        var clipboard = FakeClipboard.WithFiles(@"C:\work\secret.txt");
        clipboard.Dwords[History] = 0;

        var result = Inspect(clipboard);

        Assert.AreEqual(ClipboardObservationKind.Sensitive, result.Kind);
        Assert.AreEqual(0, clipboard.FilePathReadCount);
    }

    [TestMethod]
    public void CopiedFilePathReadFailureRequestsARetryOutcome()
    {
        var clipboard = FakeClipboard.WithFiles(@"C:\work\script.bat");
        clipboard.FilePathReadSucceeds = false;

        Assert.AreEqual(ClipboardObservationKind.InspectionFailed, Inspect(clipboard).Kind);
    }

    private static ClipboardObservation Inspect(FakeClipboard clipboard)
    {
        var result = InspectRaw(clipboard);
        return result is ClipboardInspection.Completed completed
            ? completed.Observation
            : throw new AssertFailedException("Expected a completed clipboard inspection.");
    }

    private static ClipboardInspection InspectRaw(FakeClipboard clipboard) =>
        new ClipboardPrivacyInspector(clipboard, new ClipboardFormatIds(Exclude, History, Cloud)).Inspect(7, Now);

    private sealed class FakeClipboard : IClipboardDataAccess
    {
        public HashSet<uint> Formats { get; } = [];

        public Dictionary<uint, uint> Dwords { get; } = [];

        public HashSet<uint> MalformedDwords { get; } = [];

        public bool FormatInspectionSucceeds { get; set; } = true;

        public bool HasAnyFormats { get; set; } = true;

        public bool TextReadSucceeds { get; set; } = true;

        public bool FilePathReadSucceeds { get; set; } = true;

        public string? Text { get; set; }

        public IReadOnlyList<string>? FilePaths { get; set; }

        public int TextReadCount { get; private set; }

        public int FilePathReadCount { get; private set; }

        public static FakeClipboard WithText(string text)
        {
            var clipboard = new FakeClipboard { Text = text };
            clipboard.Formats.Add(ClipboardPrivacyInspector.NativeUnicodeTextFormat);
            return clipboard;
        }

        public static FakeClipboard WithFiles(params string[] filePaths)
        {
            var clipboard = new FakeClipboard { FilePaths = filePaths };
            clipboard.Formats.Add(ClipboardPrivacyInspector.NativeFileDropFormat);
            return clipboard;
        }

        public bool TryHasAnyFormats(out bool hasAnyFormats)
        {
            hasAnyFormats = HasAnyFormats;
            return FormatInspectionSucceeds;
        }

        public bool IsFormatAvailable(uint format) => Formats.Contains(format) || Dwords.ContainsKey(format) || MalformedDwords.Contains(format);

        public bool TryReadDword(uint format, out uint value)
        {
            value = 0;
            return !MalformedDwords.Contains(format) && Dwords.TryGetValue(format, out value);
        }

        public bool TryReadUnicodeText(out string? text)
        {
            TextReadCount++;
            text = TextReadSucceeds ? Text : null;
            return TextReadSucceeds;
        }

        public bool TryReadFilePaths(out IReadOnlyList<string>? filePaths)
        {
            FilePathReadCount++;
            filePaths = FilePathReadSucceeds ? FilePaths : null;
            return FilePathReadSucceeds;
        }
    }
}
