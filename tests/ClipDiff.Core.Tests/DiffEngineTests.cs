using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClipDiff.Core.Tests;

[TestClass]
public sealed class DiffEngineTests
{
    [TestMethod]
    public void AllLinesEqual()
    {
        var document = Compare("alpha\nbravo", "alpha\nbravo");

        CollectionAssert.AreEqual(
            new[] { DiffKind.Equal, DiffKind.Equal },
            document.Rows.Select(row => row.Kind).ToArray());
        Assert.AreEqual(new DiffSummary(0, 0, 0, 2), document.Summary);
        Assert.AreEqual("No differences", DiffFormatting.Summary(document.Summary));
    }

    [TestMethod]
    public void RepresentativeChangedAndInsertedLinesHaveCorrectNumbersAndSummary()
    {
        var document = Compare(
            "alpha\nbravo\ncharlie",
            "alpha\nbravo changed\ncharlie\ndelta");

        var changed = document.Rows.Single(row => row.Kind == DiffKind.Changed);
        Assert.AreEqual(2, changed.OldLineNumber);
        Assert.AreEqual(2, changed.NewLineNumber);
        Assert.AreEqual("bravo", changed.OldText);
        Assert.AreEqual("bravo changed", changed.NewText);

        var inserted = document.Rows.Single(row => row.Kind == DiffKind.Inserted);
        Assert.IsNull(inserted.OldLineNumber);
        Assert.AreEqual(4, inserted.NewLineNumber);
        Assert.AreEqual("delta", inserted.NewText);
        Assert.AreEqual(new DiffSummary(1, 0, 1, 2), document.Summary);
        Assert.AreEqual("1 changed line, 1 added line", DiffFormatting.Summary(document.Summary));
    }

    [TestMethod]
    public void OneInsertedLine()
    {
        AssertKinds("alpha\ncharlie", "alpha\nbravo\ncharlie", DiffKind.Equal, DiffKind.Inserted, DiffKind.Equal);
    }

    [TestMethod]
    public void OneRemovedLine()
    {
        AssertKinds("alpha\nbravo\ncharlie", "alpha\ncharlie", DiffKind.Equal, DiffKind.Removed, DiffKind.Equal);
    }

    [TestMethod]
    public void ChangedPlusInsertedWithinOneBlock()
    {
        AssertKinds("one", "changed\ninserted", DiffKind.Changed, DiffKind.Inserted);
    }

    [TestMethod]
    public void ChangedPlusRemovedWithinOneBlock()
    {
        AssertKinds("changed\nremoved", "one", DiffKind.Changed, DiffKind.Removed);
    }

    [TestMethod]
    public void MoreRemovalsThanInsertionsArePairedThenRemoved()
    {
        AssertKinds("one\ntwo\nthree", "replacement", DiffKind.Changed, DiffKind.Removed, DiffKind.Removed);
    }

    [TestMethod]
    public void MoreInsertionsThanRemovalsArePairedThenInserted()
    {
        AssertKinds("one", "replacement\ntwo\nthree", DiffKind.Changed, DiffKind.Inserted, DiffKind.Inserted);
    }

    [TestMethod]
    public void BlankLineChangeIsComparedAtLineLevel()
    {
        AssertKinds("one\n\nthree", "one\ntwo\nthree", DiffKind.Equal, DiffKind.Changed, DiffKind.Equal);
    }

    [TestMethod]
    public void TrailingNewlineDifferenceIsPreserved()
    {
        var document = Compare("a", "a\n");

        AssertKinds(document, DiffKind.Equal, DiffKind.Inserted);
        Assert.AreEqual(string.Empty, document.Rows[1].NewText);
    }

    [TestMethod]
    public void CrLfAndLfNormalizeToEqualLines()
    {
        AssertKinds("alpha\r\nbravo", "alpha\nbravo", DiffKind.Equal, DiffKind.Equal);
    }

    [TestMethod]
    public void RepeatedIdenticalLinesProduceDeterministicShortestEdit()
    {
        var first = Compare("same\nold\nsame", "same\nsame");
        var second = Compare("same\nold\nsame", "same\nsame");

        CollectionAssert.AreEqual(
            first.Rows.Select(RowSignature).ToArray(),
            second.Rows.Select(RowSignature).ToArray());
        Assert.AreEqual(1, first.Summary.Removed);
        Assert.AreEqual(2, first.Summary.Unchanged);
    }

    [TestMethod]
    public void CompletelyUnrelatedTextsPairAsChangedRows()
    {
        AssertKinds("old one\nold two", "new one\nnew two", DiffKind.Changed, DiffKind.Changed);
    }

    [TestMethod]
    public void EmptyLineOnOneSideIsRetained()
    {
        var document = Compare("\nvalue", "value");

        Assert.AreEqual(DiffKind.Removed, document.Rows[0].Kind);
        Assert.AreEqual(string.Empty, document.Rows[0].OldText);
    }

    [TestMethod]
    public void TabsAndUnicodeArePreservedInUnifiedOutput()
    {
        var document = Compare("\told café", "\tnew 日本語");

        var output = DiffFormatting.Unified(document);

        Assert.AreEqual(
            "--- Previous clipboard\n+++ Current clipboard\n- \told café\n+ \tnew 日本語",
            output);
    }

    [TestMethod]
    public void SummaryLabelUsesChangedAddedRemovedOrderAndCorrectPluralization()
    {
        Assert.AreEqual(
            "1 changed line, 15 added lines, 2 removed lines",
            DiffFormatting.Summary(new DiffSummary(15, 2, 1, 99)));
    }

    [TestMethod]
    public void UnifiedOutputHasExactHeadersMarkersAndNoTrailingNewline()
    {
        var output = DiffFormatting.Unified(Compare("same\nold", "same\nnew\nadded"));

        Assert.AreEqual(
            "--- Previous clipboard\n+++ Current clipboard\n  same\n- old\n+ new\n+ added",
            output);
        Assert.IsFalse(output.EndsWith('\n'));
    }

    private static DiffDocument Compare(string previous, string current)
    {
        var time = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        return new DiffEngine().Compare(
            new ClipboardEntry(Guid.NewGuid(), previous, time),
            new ClipboardEntry(Guid.NewGuid(), current, time),
            time);
    }

    private static void AssertKinds(string previous, string current, params DiffKind[] expected) =>
        AssertKinds(Compare(previous, current), expected);

    private static void AssertKinds(DiffDocument document, params DiffKind[] expected) =>
        CollectionAssert.AreEqual(expected, document.Rows.Select(row => row.Kind).ToArray());

    private static string RowSignature(DiffRow row) =>
        $"{row.OldLineNumber}|{row.NewLineNumber}|{row.OldText}|{row.NewText}|{row.Kind}";
}
