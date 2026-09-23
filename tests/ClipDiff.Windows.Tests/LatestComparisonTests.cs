using ClipDiff.Windows.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClipDiff.Windows.Tests;

[TestClass]
public sealed class LatestComparisonTests
{
    [TestMethod]
    public async Task SupersededWorkerCannotPublishEvenIfItIgnoresCancellation()
    {
        var comparison = new LatestComparison();
        var first = new TaskCompletionSource<DiffDocument>();
        var second = new TaskCompletionSource<DiffDocument>();
        var published = new List<DiffDocument>();
        CancellationToken oldToken = default;
        var oldRun = comparison.RunAsync(token => { oldToken = token; return first.Task; }, published.Add);
        var newRun = comparison.RunAsync(_ => second.Task, published.Add);
        Assert.IsTrue(oldToken.IsCancellationRequested);
        var latest = Document("30", "60", true);
        second.SetResult(latest);
        await newRun;
        first.SetResult(Document("old", "stale", false));
        await oldRun;
        Assert.HasCount(1, published);
        Assert.AreSame(latest, published[0]);
        Assert.AreEqual(new HighlightRange(0, 1), published[0].Rows[0].NewHighlights.Single());
    }

    [TestMethod]
    public async Task ClearPreventsPublicationAndLaterRequestsStillWork()
    {
        var comparison = new LatestComparison();
        var pending = new TaskCompletionSource<DiffDocument>();
        var published = new List<DiffDocument>();
        var run = comparison.RunAsync(_ => pending.Task, published.Add);
        comparison.Cancel();
        pending.SetResult(Document("old", "new", false));
        await run;
        Assert.IsEmpty(published);
        await comparison.RunAsync(_ => Task.FromResult(Document("a b", "ab", true)), published.Add);
        Assert.HasCount(1, published);
    }

    [TestMethod]
    public async Task RapidOptionChangesPublishOnlyTheLatestSpans()
    {
        var comparison = new LatestComparison();
        var workers = new List<TaskCompletionSource<DiffDocument>>();
        var runs = new List<Task>();
        var published = new List<DiffDocument>();
        for (var index = 0; index < 10; index++)
        {
            var worker = new TaskCompletionSource<DiffDocument>();
            workers.Add(worker);
            runs.Add(comparison.RunAsync(_ => worker.Task, published.Add));
        }
        for (var index = 0; index < workers.Count; index++)
        {
            workers[index].SetResult(Document("a\tb", "a  b", index % 2 == 1));
            await runs[index];
        }
        Assert.HasCount(1, published);
        Assert.AreEqual(DiffKind.Equal, published[0].Rows[0].Kind);
        Assert.IsEmpty(published[0].Rows[0].OldHighlights);
        Assert.IsEmpty(published[0].Rows[0].NewHighlights);
    }

    [TestMethod]
    public async Task CooperativeCancellationIsObservedWithoutPublishing()
    {
        var comparison = new LatestComparison();
        var pending = new TaskCompletionSource<DiffDocument>();
        var published = new List<DiffDocument>();
        var run = comparison.RunAsync(token =>
        {
            token.Register(() => pending.SetCanceled(token));
            return pending.Task;
        }, published.Add);
        comparison.Cancel();
        await run;
        Assert.IsEmpty(published);
    }

    private static DiffDocument Document(string old, string current, bool ignoreSpacing) => new DiffEngine().Compare(
        new ClipboardEntry(Guid.NewGuid(), old, DateTimeOffset.UtcNow),
        new ClipboardEntry(Guid.NewGuid(), current, DateTimeOffset.UtcNow), ignoreSpacing: ignoreSpacing);
}
