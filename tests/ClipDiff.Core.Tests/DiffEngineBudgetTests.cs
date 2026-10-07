using System.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClipDiff.Core.Tests;

[TestClass]
public sealed class DiffEngineBudgetTests
{
	public TestContext TestContext { get; set; } = null!;

	[TestMethod]
	public void TenThousandUnrelatedLinesStayWithinAllocationBudget()
	{
		const int lineCount = 10_000;
		var previous = Entry(Lines("old", lineCount));
		var current = Entry(Lines("new", lineCount));
		var engine = new DiffEngine();
		engine.Compare(Entry("warmup old"), Entry("warmup new"));

		long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
		var stopwatch = Stopwatch.StartNew();
		var document = engine.Compare(previous, current);
		stopwatch.Stop();

		long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
		TestContext.WriteLine($"10,000 unrelated lines: {allocated:N0} allocated bytes, {stopwatch.Elapsed.TotalMilliseconds:N1} ms.");

		// The line-search frontier and trace are capped at 8 MiB. This includes the
		// complete document and bounded inline highlighting, with room for runtimes.
		Assert.IsLessThan(64L * 1024 * 1024, allocated);
		Assert.AreEqual(new DiffSummary(0, 0, lineCount, 0), document.Summary);
		AssertPreservesLines(previous, current, document);
	}

	[TestMethod]
	public void BudgetFallbackKeepsExactPrefixAndSuffixAndIsDeterministic()
	{
		var previous = Entry($"prefix\n{Lines("old", 10_000)}\nsuffix");
		var current = Entry($"prefix\n{Lines("new", 10_000)}\nsuffix");
		var engine = new DiffEngine();
		var first = engine.Compare(previous, current);
		var second = engine.Compare(previous, current);

		Assert.AreEqual(new DiffSummary(0, 0, 10_000, 2), first.Summary);
		Assert.AreEqual(DiffKind.Equal, first.Rows[0].Kind);
		Assert.AreEqual(DiffKind.Equal, first.Rows[^1].Kind);
		AssertPreservesLines(previous, current, first);
		CollectionAssert.AreEqual(
			first.Rows.Select(RowSignature).ToArray(),
			second.Rows.Select(RowSignature).ToArray());
	}

	[TestMethod]
	[DataRow(true)]
	[DataRow(false)]
	public void TenThousandWhollyInsertedOrDeletedLinesKeepLineNumbers(bool insert)
	{
		var shorter = Entry("prefix\nsuffix");
		var longer = Entry($"prefix\n{Lines("extra", 10_000)}\nsuffix");
		var previous = insert ? shorter : longer;
		var current = insert ? longer : shorter;
		var document = new DiffEngine().Compare(previous, current);

		Assert.AreEqual(insert
			? new DiffSummary(10_000, 0, 0, 2)
			: new DiffSummary(0, 10_000, 0, 2), document.Summary);
		Assert.AreEqual(insert ? DiffKind.Inserted : DiffKind.Removed, document.Rows[1].Kind);
		Assert.AreEqual(insert ? 2 : (int?)null, document.Rows[1].NewLineNumber);
		Assert.AreEqual(insert ? (int?)null : 2, document.Rows[1].OldLineNumber);
		AssertPreservesLines(previous, current, document);
	}

	[TestMethod]
	public void CancellationDuringLargeEditBlockStopsRowGeneration()
	{
		using var cancellation = new CancellationTokenSource();
		var generatedRows = 0;
		var engine = new DiffEngine(() =>
		{
			generatedRows++;

			if (generatedRows == 50)
			{
				cancellation.Cancel();
			}

			return Guid.NewGuid();
		});
		var previous = Entry(Lines("old", 10_000));
		var current = Entry(Lines("new", 10_000));

		Assert.ThrowsExactly<OperationCanceledException>(() =>
			engine.Compare(previous, current, cancellationToken: cancellation.Token));
		Assert.AreEqual(50, generatedRows);
	}

	private static void AssertPreservesLines(ClipboardEntry previous, ClipboardEntry current, DiffDocument document)
	{
		var oldRows = document.Rows.Where(row => row.OldLineNumber is not null).ToArray();
		var newRows = document.Rows.Where(row => row.NewLineNumber is not null).ToArray();

		CollectionAssert.AreEqual(TextLines.Split(previous.Text), oldRows.Select(row => row.OldText).ToArray());
		CollectionAssert.AreEqual(TextLines.Split(current.Text), newRows.Select(row => row.NewText).ToArray());
		CollectionAssert.AreEqual(Enumerable.Range(1, oldRows.Length).ToArray(), oldRows.Select(row => row.OldLineNumber!.Value).ToArray());
		CollectionAssert.AreEqual(Enumerable.Range(1, newRows.Length).ToArray(), newRows.Select(row => row.NewLineNumber!.Value).ToArray());
	}

	private static ClipboardEntry Entry(string text) => new(Guid.NewGuid(), text, DateTimeOffset.UnixEpoch);

	private static string Lines(string prefix, int count) => string.Join('\n', Enumerable.Range(0, count).Select(index => $"{prefix}-{index}"));

	private static string RowSignature(DiffRow row) => $"{row.OldLineNumber}|{row.NewLineNumber}|{row.OldText}|{row.NewText}|{row.Kind}";
}
