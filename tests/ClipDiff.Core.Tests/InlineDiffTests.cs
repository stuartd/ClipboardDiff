using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClipDiff.Core.Tests;

[TestClass]
public sealed class InlineDiffTests
{
    [TestMethod]
    [DataRow("timeout = 30", "timeout = 60", false, "3", "6")]
    [DataRow("x = 12; y = 34", "x = 92; y = 38", false, "1|4", "9|8")]
    [DataRow("return value;", "return new_value;", false, "", "new_")]
    [DataRow("return new_value;", "return value;", false, "new_", "")]
    [DataRow("a\tb", "a b", false, "\t", " ")]
    [DataRow("a\tb", "a  b", true, "", "")]
    [DataRow("a b", "ab", true, " ", "")]
    [DataRow("\t let  total = 30  ", "let total\t= 60", true, "3", "6")]
    [DataRow("👩🏽‍💻", "👨🏻‍💻", false, "👩🏽‍💻", "👨🏻‍💻")]
    [DataRow("e\u0301", "o\u0301", false, "e\u0301", "o\u0301")]
    [DataRow("\t👩🏽‍💻\t30", "\t👩🏽‍💻\t60", false, "3", "6")]
    [DataRow("", "new", false, "", "new")]
    [DataRow("new", "", false, "new", "")]
    [DataRow("a \t b", "ab", true, " \t ", "")]
    public void AcceptanceSpans(string old, string current, bool ignoreSpacing, string oldSpans, string newSpans)
    {
        var row = Compare(old, current, ignoreSpacing).Rows.Single();
        Assert.AreEqual(oldSpans, string.Join('|', Highlighted(old, row.OldHighlights)));
        Assert.AreEqual(newSpans, string.Join('|', Highlighted(current, row.NewHighlights)));
        Assert.AreEqual(old, row.OldText);
        Assert.AreEqual(current, row.NewText);
        AssertRanges(old, row.OldHighlights);
        AssertRanges(current, row.NewHighlights);
    }

    [TestMethod]
    public void ExactSpacingStillPreservesTheUnchangedWord()
    {
        var row = Compare("\t let  total = 30  ", "let total\t= 60").Rows.Single();
        Assert.AreEqual("\t | | |3|  ", string.Join('|', Highlighted(row.OldText!, row.OldHighlights)));
        Assert.AreEqual("\t|6", string.Join('|', Highlighted(row.NewText!, row.NewHighlights)));
    }

    [TestMethod]
    public void EqualAndUnpairedLinesHaveNoSpans()
    {
        foreach (var document in new[] { Compare("same\nold", "same"), Compare("same", "same\nnew") })
        {
            foreach (var row in document.Rows)
            {
                Assert.IsEmpty(row.OldHighlights);
                Assert.IsEmpty(row.NewHighlights);
            }
        }
    }

    [TestMethod]
    public void IgnoreSpacingUsesTheSameLineAndInlineSemanticsAndPreservesSources()
    {
        var document = Compare(" \ta\t b  \n\t30", "a b\n 60 ", true);
        Assert.AreEqual(new DiffSummary(0, 0, 1, 1), document.Summary);
        Assert.AreEqual(" \ta\t b  ", document.Rows[0].OldText);
        Assert.AreEqual("a b", document.Rows[0].NewText);
        Assert.AreEqual("3", Highlighted(document.Rows[1].OldText!, document.Rows[1].OldHighlights).Single());
        Assert.AreEqual(" \ta\t b  \n\t30", document.Previous.Text);
        Assert.AreEqual("a b\n 60 ", document.Current.Text);
        Assert.AreEqual("--- Previous clipboard\n+++ Current clipboard\n   \ta\t b  \n- \t30\n+  60 ", DiffFormatting.Unified(document));
    }

    [TestMethod]
    public void AdjacentChangesMergeAndTiesAdvanceTheOriginalSide()
    {
        var row = Compare("a!b", "b!a").Rows.Single();
        CollectionAssert.AreEqual(new[] { new HighlightRange(0, 2) }, row.OldHighlights.ToArray());
        CollectionAssert.AreEqual(new[] { new HighlightRange(1, 3) }, row.NewHighlights.ToArray());
    }

    [TestMethod]
    public void UnicodeEqualityIsOrdinalWithoutCanonicalNormalization()
    {
        var row = Compare("é", "e\u0301").Rows.Single();
        Assert.AreEqual(DiffKind.Changed, row.Kind);
        Assert.AreEqual("é", Highlighted(row.OldText!, row.OldHighlights).Single());
        Assert.AreEqual("e\u0301", Highlighted(row.NewText!, row.NewHighlights).Single());
    }

    [TestMethod]
    public void EligibleDifficultPairRetainsMatchingEdgesAndHighlightsTheMiddle()
    {
        var row = Compare("same " + new string('a', 400) + " end", "same " + new string('b', 400) + " end").Rows.Single();
        CollectionAssert.AreEqual(new[] { new HighlightRange(5, 405) }, row.OldHighlights.ToArray());
        CollectionAssert.AreEqual(new[] { new HighlightRange(5, 405) }, row.NewHighlights.ToArray());
    }

    [TestMethod]
    public void ByteLimitIsUtf8AndSkippedPairsRetainTextAndChangedStatus()
    {
        var text = string.Concat(Enumerable.Repeat("😀", 2048));
        var eligible = Compare(text, new string('a', 8192)).Rows.Single(); // 16,384 UTF-8 bytes
        Assert.IsNotEmpty(eligible.OldHighlights);
        var skipped = Compare(text, new string('a', 8193)).Rows.Single();
        Assert.IsEmpty(skipped.OldHighlights);
        Assert.IsEmpty(skipped.NewHighlights);
        Assert.AreEqual(DiffKind.Changed, skipped.Kind);
        Assert.AreEqual(text, skipped.OldText);
        Assert.AreEqual(new string('a', 8193), skipped.NewText);
    }

    [TestMethod]
    public void SharedBudgetExhaustsInDocumentOrderAndResetsForEachComparison()
    {
        var old = new string('a', 8192);
        var current = new string('b', 8192);
        var document = Compare(string.Join('\n', Enumerable.Repeat(old, 125)),
            string.Join('\n', Enumerable.Repeat(current, 125)));
        // Each pair costs 16,384 bytes and four token-search cells; refinement is too large.
        Assert.AreEqual(122, document.Rows.Count(row => row.OldHighlights.Count > 0));
        Assert.IsNotEmpty(document.Rows[121].OldHighlights);
        Assert.IsEmpty(document.Rows[122].OldHighlights);
        Assert.AreEqual(new DiffSummary(0, 0, 125, 0), document.Summary);
        Assert.IsTrue(document.Rows.All(row => row.OldText == old && row.NewText == current));
        Assert.IsNotEmpty(Compare(old, current).Rows.Single().OldHighlights);
    }

    [TestMethod]
    public void RandomExactModeRetainsIdenticalUnhighlightedText()
    {
        var random = new Random(814);
        string[] pieces = ["foo", "foo", "bar", " ", "  ", "\t", "=", "!", "_", "9", "é", "e\u0301", "👩🏽‍💻", "中"];
        for (var sample = 0; sample < 500; sample++)
        {
            string MakeText() => string.Concat(Enumerable.Range(0, random.Next(1, 25)).Select(_ => pieces[random.Next(pieces.Length)]));
            var old = MakeText();
            var current = MakeText();
            var row = Compare(old, current).Rows.Single();
            AssertRanges(old, row.OldHighlights);
            AssertRanges(current, row.NewHighlights);
            Assert.AreEqual(Remaining(old, row.OldHighlights), Remaining(current, row.NewHighlights));
        }
    }

    [TestMethod]
    public void SwappingInputsReversesInsertionAndDeletionSpans()
    {
        var forward = Compare("return value;", "return new_value;").Rows.Single();
        var reverse = Compare("return new_value;", "return value;").Rows.Single();
        CollectionAssert.AreEqual(forward.OldHighlights.ToArray(), reverse.NewHighlights.ToArray());
        CollectionAssert.AreEqual(forward.NewHighlights.ToArray(), reverse.OldHighlights.ToArray());
    }

    [TestMethod]
    public void CancelledComparisonCannotReturnADocument()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => new DiffEngine().Compare(
            Entry("old"), Entry("new"), cancellationToken: cancellation.Token));
    }

    [TestMethod]
    public void DisplaySlicesExpandTabsAfterMappingGraphemesAndKeepTextPlain()
    {
        const string source = "\t👩🏽‍💻\t30 <tag>&";
        var slices = DiffTextFormatting.Slices(source, [new(2, 4)]);
        CollectionAssert.AreEqual(new[]
        {
            new DiffTextSlice("    👩🏽‍💻", false),
            new DiffTextSlice("    3", true),
            new DiffTextSlice("0 <tag>&", false)
        }, slices.ToArray());
        Assert.AreEqual(source.Replace("\t", "    ", StringComparison.Ordinal), string.Concat(slices.Select(slice => slice.Text)));
    }

    [TestMethod]
    public void DisplaySlicesNeverSplitCombiningMarksAndPreserveUnhighlightedEmptyText()
    {
        CollectionAssert.AreEqual(new[] { new DiffTextSlice("e\u0301", true), new DiffTextSlice("!", false) },
            DiffTextFormatting.Slices("e\u0301!", [new(0, 1)]).ToArray());
        Assert.AreEqual(string.Empty, DiffTextFormatting.Slices(null, []).Single().Text);
    }

    private static ClipboardEntry Entry(string text) => new(Guid.NewGuid(), text, DateTimeOffset.UtcNow);
    private static DiffDocument Compare(string old, string current, bool ignoreSpacing = false) =>
        new DiffEngine().Compare(Entry(old), Entry(current), ignoreSpacing: ignoreSpacing);
    private static string[] Graphemes(string text) => StringInfo.ParseCombiningCharacters(text)
        .Select(start => StringInfo.GetNextTextElement(text, start)).ToArray();
    private static IEnumerable<string> Highlighted(string text, IReadOnlyList<HighlightRange> ranges)
    {
        var units = Graphemes(text);
        return ranges.Select(range => string.Concat(units[range.Start..range.End]));
    }
    private static string Remaining(string text, IReadOnlyList<HighlightRange> ranges) =>
        string.Concat(Graphemes(text).Where((_, index) => !ranges.Any(range => index >= range.Start && index < range.End)));
    private static void AssertRanges(string text, IReadOnlyList<HighlightRange> ranges)
    {
        var previousEnd = -1;
        var length = Graphemes(text).Length;
        foreach (var range in ranges)
        {
            Assert.IsTrue(range.Start > previousEnd);
            Assert.IsTrue(range.End > range.Start && range.End <= length);
            previousEnd = range.End;
        }
    }
}
