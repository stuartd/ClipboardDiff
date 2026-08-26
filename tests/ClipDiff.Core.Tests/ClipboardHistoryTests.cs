using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClipDiff.Core.Tests;

[TestClass]
public sealed class ClipboardHistoryTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void CapturesLastTwoValuesNewestFirstAndEvictsOlderValues()
    {
        var history = CreateHistory();

        history.Apply(Text(1, "first"));
        history.Apply(Text(2, "second"));
        history.Apply(Text(3, "third"));

        CollectionAssert.AreEqual(new[] { "third", "second" }, history.Entries.Select(entry => entry.Text).ToArray());
        Assert.AreEqual("third", history.Current?.Text);
        Assert.AreEqual("second", history.Previous?.Text);
    }

    [TestMethod]
    public void ConsecutiveIdenticalCopiesCreateAReadyNoDifferencesComparison()
    {
        var history = CreateHistory();
        history.Apply(Text(1, "same text"));

        var result = history.Apply(Text(2, "same text"));

        Assert.AreEqual(ClipboardHistoryChange.Accepted, result);
        Assert.AreEqual(2, history.Entries.Count);
        Assert.AreEqual("same text", history.Current?.Text);
        Assert.AreEqual("same text", history.Previous?.Text);
        Assert.AreNotEqual(history.Current?.Id, history.Previous?.Id);
        Assert.AreEqual("Ready to diff", history.Status);

        var document = new DiffEngine().Compare(history.Previous!, history.Current!);
        Assert.AreEqual("No differences", DiffFormatting.Summary(document.Summary));
    }

    [TestMethod]
    public void TextPairReplacesHistoryWithPreviousAndCurrentValuesInClipboardOrder()
    {
        var history = CreateHistory();
        history.Apply(Text(1, "stale value"));

        var result = history.Apply(ClipboardObservation.TextPair(
            2,
            Start.AddSeconds(1),
            "first file contents",
            "second file contents"));

        Assert.AreEqual(ClipboardHistoryChange.Accepted, result);
        Assert.AreEqual("first file contents", history.Previous?.Text);
        Assert.AreEqual("second file contents", history.Current?.Text);
        Assert.AreEqual(2, history.Entries.Count);
        Assert.AreEqual("Ready to diff", history.Status);
    }

    [TestMethod]
    public void FileNamesAreRetainedWithSingleAndPairedFileValues()
    {
        var history = CreateHistory();
        history.Apply(ClipboardObservation.TextValue(
            1,
            Start,
            "single contents",
            "single.txt",
            @"C:\single\single.txt"));

        Assert.AreEqual("single.txt", history.Current?.SourceFileName);
        Assert.AreEqual(@"C:\single\single.txt", history.Current?.SourceFilePath);

        history.Apply(ClipboardObservation.TextPair(
            2,
            Start.AddSeconds(1),
            "old contents",
            "new contents",
            "old.cs",
            "new.cs",
            @"C:\old\old.cs",
            @"C:\new\new.cs"));

        Assert.AreEqual("old.cs", history.Previous?.SourceFileName);
        Assert.AreEqual("new.cs", history.Current?.SourceFileName);
        Assert.AreEqual(@"C:\old\old.cs", history.Previous?.SourceFilePath);
        Assert.AreEqual(@"C:\new\new.cs", history.Current?.SourceFilePath);
    }

    [TestMethod]
    public void DirectTextActsLikeANewCopyWithoutChangingTheClipboardSequence()
    {
        var history = CreateHistory();
        history.Apply(Text(6, "older contents"));
        history.Apply(Text(7, "copied file contents"));

        var result = history.AcceptDirectText("selected file contents", Start.AddSeconds(1));

        Assert.AreEqual(ClipboardHistoryChange.Accepted, result);
        Assert.AreEqual("copied file contents", history.Previous?.Text);
        Assert.AreEqual("selected file contents", history.Current?.Text);
        Assert.AreEqual(2, history.Entries.Count);
        Assert.IsFalse(history.Entries.Any(entry => entry.Text == "older contents"));
        Assert.AreEqual((uint)7, history.LastSequenceNumber);
        Assert.AreEqual("Ready to diff", history.Status);
    }

    [TestMethod]
    public void DirectFileRetainsItsFileName()
    {
        var history = CreateHistory();
        history.Apply(Text(1, "copied contents"));

        history.AcceptDirectText(
            "selected contents",
            Start.AddSeconds(1),
            "selected.txt",
            @"C:\selected\selected.txt");

        Assert.AreEqual("selected.txt", history.Current?.SourceFileName);
        Assert.AreEqual(@"C:\selected\selected.txt", history.Current?.SourceFilePath);
        Assert.IsNull(history.Previous?.SourceFileName);
    }

    [TestMethod]
    public void DirectTextIsNotRemovedByAClipboardClear()
    {
        var history = CreateHistory();
        history.Apply(Text(1, "copied file contents"));
        history.AcceptDirectText("selected file contents", Start.AddSeconds(1));

        history.Apply(ClipboardObservation.ExplicitClear(2, Start.AddSeconds(2)));

        Assert.AreEqual("selected file contents", history.Current?.Text);
        Assert.AreEqual("copied file contents", history.Previous?.Text);
    }

    [TestMethod]
    public void DirectTextIsIgnoredWhileMonitoringIsPaused()
    {
        var history = CreateHistory();
        history.Apply(Text(1, "copied file contents"));
        history.Pause();

        var result = history.AcceptDirectText("selected file contents", Start.AddSeconds(1));

        Assert.AreEqual(ClipboardHistoryChange.None, result);
        Assert.AreEqual("copied file contents", history.Current?.Text);
        Assert.IsNull(history.Previous);
    }

    [TestMethod]
    public void EmptyTextOutsideClearWindowLeavesHistoryUntouched()
    {
        var history = CreateHistory();
        history.Apply(Text(1, "kept"));

        history.Apply(Text(2, string.Empty, 61));

        Assert.AreEqual("kept", history.Current?.Text);
    }

    [TestMethod]
    public void WhitespaceOnlyTextIsAccepted()
    {
        var history = CreateHistory();

        history.Apply(Text(1, " \t\r\n"));

        Assert.AreEqual(" \t\r\n", history.Current?.Text);
    }

    [TestMethod]
    public void NonTextObservationDoesNotClearHistory()
    {
        var history = CreateHistory();
        history.Apply(Text(1, "kept"));

        history.Apply(ClipboardObservation.NonText(2, Start.AddSeconds(1)));

        Assert.AreEqual("kept", history.Current?.Text);
    }

    [TestMethod]
    public void PausedMonitoringLeavesHistoryUntouchedAndResumeSetsBaseline()
    {
        var history = CreateHistory();
        history.Apply(Text(1, "first"));
        history.Pause();

        history.Apply(Text(2, "ignored"));
        history.Resume(8);
        history.Apply(Text(8, "also ignored"));
        history.Apply(Text(9, "accepted"));

        CollectionAssert.AreEqual(new[] { "accepted", "first" }, history.Entries.Select(entry => entry.Text).ToArray());
        Assert.AreEqual((uint)9, history.LastSequenceNumber);
    }

    [TestMethod]
    public void ClearRemovesBothEntries()
    {
        var history = CreateHistory();
        history.Apply(Text(1, "first"));
        history.Apply(Text(2, "second"));

        history.Clear();

        Assert.AreEqual(0, history.Entries.Count);
    }

    [TestMethod]
    public void StatusStringsFollowMonitoringAndEntryCount()
    {
        var history = CreateHistory();
        Assert.AreEqual("Waiting for copied text", history.Status);

        history.Apply(Text(1, "first"));
        Assert.AreEqual("Copy one more text value", history.Status);

        history.Apply(Text(2, "first"));
        Assert.AreEqual("Ready to diff", history.Status);

        history.Pause();
        Assert.AreEqual("Monitoring paused", history.Status);
    }

    [TestMethod]
    public void RecentExplicitClearRemovesLatestEligibleEntry()
    {
        var history = CreateHistory();
        history.Apply(Text(1, "older"));
        history.Apply(Text(2, "possibly sensitive"));

        var result = history.Apply(ClipboardObservation.ExplicitClear(3, Start.AddSeconds(5)));

        Assert.AreEqual(ClipboardHistoryChange.RemovedByRecentClear, result);
        Assert.AreEqual("older", history.Current?.Text);
        Assert.IsNull(history.Previous);
    }

    [TestMethod]
    public void ClearOutsideWindowDoesNotRemoveEntry()
    {
        var history = CreateHistory();
        history.Apply(Text(1, "kept"));

        history.Apply(ClipboardObservation.ExplicitClear(2, Start.AddSeconds(61)));

        Assert.AreEqual("kept", history.Current?.Text);
    }

    [TestMethod]
    [DataRow(ClipboardObservationKind.NonText)]
    [DataRow(ClipboardObservationKind.Sensitive)]
    [DataRow(ClipboardObservationKind.InspectionFailed)]
    public void InterveningObservationPreventsUnrelatedClear(ClipboardObservationKind interveningKind)
    {
        var history = CreateHistory();
        history.Apply(Text(1, "ordinary"));
        history.Apply(new ClipboardObservation(interveningKind, 2, Start.AddSeconds(1)));

        history.Apply(ClipboardObservation.ExplicitClear(3, Start.AddSeconds(2)));

        Assert.AreEqual("ordinary", history.Current?.Text);
    }

    [TestMethod]
    public void SecondIdenticalCopyFollowedByImmediateClearRemovesOnlyLatestEntry()
    {
        var history = CreateHistory();
        history.Apply(Text(1, "ordinary"));
        history.Apply(Text(2, "ordinary", 30));

        history.Apply(ClipboardObservation.ExplicitClear(3, Start.AddSeconds(75)));

        Assert.AreEqual(1, history.Entries.Count);
        Assert.AreEqual("ordinary", history.Current?.Text);
    }

    [TestMethod]
    public void OwnClipboardWriteNeverEntersHistoryAndResetsClearEligibility()
    {
        var history = CreateHistory();
        history.Apply(Text(1, "ordinary"));

        history.Apply(ClipboardObservation.OwnWrite(2, Start.AddSeconds(1)));
        history.Apply(ClipboardObservation.ExplicitClear(3, Start.AddSeconds(2)));

        Assert.AreEqual("ordinary", history.Current?.Text);
        Assert.AreEqual(1, history.Entries.Count);
    }

    [TestMethod]
    public void StartupSequenceIsOnlyABaseline()
    {
        var history = CreateHistory(startupSequence: 42);

        history.Apply(Text(42, "pre-start value"));
        history.Apply(Text(43, "future value"));

        Assert.AreEqual("future value", history.Current?.Text);
        Assert.AreEqual(1, history.Entries.Count);
    }

    private static ClipboardHistory CreateHistory(uint startupSequence = 0) =>
        new(startupSequence, idFactory: Guid.NewGuid);

    private static ClipboardObservation Text(uint sequence, string value, int seconds = 0) =>
        ClipboardObservation.TextValue(sequence, Start.AddSeconds(seconds), value);
}
