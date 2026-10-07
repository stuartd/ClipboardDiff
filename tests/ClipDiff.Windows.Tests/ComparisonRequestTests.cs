using ClipDiff.Windows.Clipboard;
using ClipDiff.Windows.Explorer;
using ClipDiff.Windows.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClipDiff.Windows.Tests;

[TestClass]
public sealed class ComparisonRequestTests
{
	[TestMethod]
	public async Task SlowPairCannotReplaceNewerPairEvenWhenReaderIgnoresCancellation()
	{
		var history = new ClipboardHistory();
		var first = new TaskCompletionSource<IReadOnlyList<CopiedFileText>>();
		var second = new TaskCompletionSource<IReadOnlyList<CopiedFileText>>();
		var reads = 0;
		var publications = 0;
		CancellationToken firstToken = default;
		var requests = new ExplorerComparisonRequests(history, (_, token) =>
		{
			if (++reads == 1)
			{
				firstToken = token;
				return new(first.Task);
			}

			return new(second.Task);
		}, () => { }, () => publications++, () => Assert.Fail("Unexpected rejection."));

		var older = requests.RunAsync(["old-a.txt", "old-b.txt"], false);
		var newer = requests.RunAsync(["new-a.txt", "new-b.txt"], false);
		Assert.IsTrue(firstToken.IsCancellationRequested);
		second.SetResult([Value("new-a"), Value("new-b")]);
		await newer;
		first.SetResult([Value("old-a"), Value("old-b")]);
		await older;

		Assert.AreEqual("new-a", history.Previous?.Text);
		Assert.AreEqual("new-b", history.Current?.Text);
		Assert.AreEqual(1, publications);
	}

	[TestMethod]
	public async Task SingleFileRetainsSourceIdentityAcrossClipboardChange()
	{
		var history = new ClipboardHistory();
		history.AcceptDirectText("A", DateTimeOffset.Now);
		var pending = new TaskCompletionSource<IReadOnlyList<CopiedFileText>>();
		var publications = 0;
		var requests = new ExplorerComparisonRequests(history, (_, _) => new(pending.Task),
			() => { }, () => publications++, () => Assert.Fail("Unexpected rejection."));

		var run = requests.RunAsync(["B.txt"], true);
		history.Apply(ClipboardObservation.TextValue(1, DateTimeOffset.Now, "C"));
		pending.SetResult([Value("B")]);
		await run;

		Assert.AreEqual("A", history.Previous?.Text);
		Assert.AreEqual("C", history.Current?.Text);
		Assert.AreEqual(0, publications);
	}

	[TestMethod]
	public async Task CancelledFileReadCannotRestoreAutomaticallyClearedCapture()
	{
		var history = new ClipboardHistory();
		var now = DateTimeOffset.Now;
		history.Apply(ClipboardObservation.TextValue(1, now, "older"));
		history.Apply(ClipboardObservation.TextValue(2, now, "secret"));
		var pending = new TaskCompletionSource<IReadOnlyList<CopiedFileText>>();
		var requests = new ExplorerComparisonRequests(history, (_, _) => new(pending.Task),
			() => { }, () => Assert.Fail("Cleared text was published."), () => { });

		var run = requests.RunAsync(["selected.txt"], true);
		Assert.AreEqual(ClipboardHistoryChange.RemovedByRecentClear,
			history.Apply(ClipboardObservation.ExplicitClear(3, now.AddSeconds(1))));
		requests.Cancel();
		pending.SetResult([Value("selected")]);
		await run;

		Assert.AreEqual("older", history.Current?.Text);
		Assert.IsNull(history.Previous);
	}

	[TestMethod]
	public async Task NewSingleFileSupersedesPendingPair()
	{
		var history = new ClipboardHistory();
		history.AcceptDirectText("source", DateTimeOffset.Now);
		var pending = new TaskCompletionSource<IReadOnlyList<CopiedFileText>>();
		var reads = 0;
		var requests = new ExplorerComparisonRequests(history,
			(_, _) => ++reads == 1 ? new(pending.Task) : new(new[] { Value("selected") }),
			() => { }, () => { }, () => Assert.Fail("Unexpected rejection."));

		var oldPair = requests.RunAsync(["a.txt", "b.txt"], false);
		await requests.RunAsync(["selected.txt"], true);
		pending.SetResult([Value("stale-a"), Value("stale-b")]);
		await oldPair;

		Assert.AreEqual("source", history.Previous?.Text);
		Assert.AreEqual("selected", history.Current?.Text);
	}

	[TestMethod]
	public async Task PausedOrMissingSourceNeverReadsSelectedFile()
	{
		var history = new ClipboardHistory();
		var rejected = 0;
		var requests = new ExplorerComparisonRequests(history,
			(_, _) => throw new AssertFailedException("Unexpected file I/O."),
			() => { }, () => { }, () => rejected++);

		await requests.RunAsync(["selected.txt"], true);
		history.Pause();
		await requests.RunAsync(["a.txt", "b.txt"], false);
		Assert.AreEqual(2, rejected);
	}

	[TestMethod]
	[DataRow(true)]
	[DataRow(false)]
	public void WarningCannotPublishAfterRecentClearWhetherAcceptedOrCancelled(bool accepted)
	{
		var now = DateTimeOffset.Now;
		var history = new ClipboardHistory();
		history.Apply(ClipboardObservation.TextValue(1, now, "older"));
		history.Apply(ClipboardObservation.TextValue(2, now, "secret"));
		var presentation = new ComparisonPresentation();
		var launches = 0;
		var builtIn = 0;

		presentation.Show(() =>
		{
			Assert.AreEqual(ClipboardHistoryChange.RemovedByRecentClear,
				history.Apply(ClipboardObservation.ExplicitClear(3, now.AddSeconds(1))));
			presentation.Cancel();
			return accepted;
		}, () => { launches++; return true; }, () => builtIn++);

		Assert.AreEqual(0, launches);
		Assert.AreEqual(0, builtIn);
	}

	[TestMethod]
	public void NestedNewComparisonPreventsOlderWarningFallback()
	{
		var presentation = new ComparisonPresentation();
		var shown = new List<string>();

		presentation.Show(() =>
		{
			presentation.Show(null, () => false, () => shown.Add("new"));
			return false;
		}, () => false, () => shown.Add("old"));

		CollectionAssert.AreEqual(new[] { "new" }, shown);
	}

	[TestMethod]
	public void OrdinaryWarningCancellationStillUsesBuiltInViewer()
	{
		var shown = false;
		new ComparisonPresentation().Show(() => false,
			() => throw new AssertFailedException("Unexpected external launch."), () => shown = true);
		Assert.IsTrue(shown);
	}

	private static CopiedFileText Value(string text) => new(text, text + ".txt", "/files/" + text + ".txt");
}
