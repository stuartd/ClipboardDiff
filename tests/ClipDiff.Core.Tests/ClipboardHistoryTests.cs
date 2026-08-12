using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClipDiff.Core.Tests;

[TestClass]
public sealed class ClipboardHistoryTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void CapturesLastTwoUniqueValuesNewestFirstAndEvictsOlderValues()
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
    public void ConsecutiveDuplicateDoesNotChangeOrderingOrEvictPrevious()
    {
        var history = CreateHistory();
        history.Apply(Text(1, "first"));
        history.Apply(Text(2, "second"));
        var ids = history.Entries.Select(entry => entry.Id).ToArray();

        var result = history.Apply(Text(3, "second"));

        Assert.AreEqual(ClipboardHistoryChange.None, result);
        CollectionAssert.AreEqual(ids, history.Entries.Select(entry => entry.Id).ToArray());
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

        history.Apply(Text(2, "second"));
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
    public void DuplicateCurrentFollowedByImmediateClearRemovesCurrent()
    {
        var history = CreateHistory();
        history.Apply(Text(1, "ordinary"));
        history.Apply(Text(2, "ordinary", 30));

        history.Apply(ClipboardObservation.ExplicitClear(3, Start.AddSeconds(75)));

        Assert.AreEqual(0, history.Entries.Count);
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
