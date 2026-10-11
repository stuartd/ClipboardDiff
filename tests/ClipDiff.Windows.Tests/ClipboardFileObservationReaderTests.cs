using ClipDiff.Windows.Clipboard;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClipDiff.Windows.Tests;

[TestClass]
public sealed class ClipboardFileObservationReaderTests
{
	private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

	[TestMethod]
	public async Task SuccessiveFullPathsRotateSavedContentsAndSourceIdentity()
	{
		var savedFiles = new Dictionary<string, CopiedFileText>
		{
			[@"C:\old files\日本語.cs"] = new("old saved text", "日本語.cs", @"C:\old files\日本語.cs"),
			[@"D:\new files\日本語.cs"] = new("new saved text", "日本語.cs", @"D:\new files\日本語.cs")
		};
		var reader = new ClipboardFileObservationReader(savedFiles.ContainsKey,
			(paths, _) => ValueTask.FromResult<IReadOnlyList<CopiedFileText>>(paths.Select(path => savedFiles[path]).ToArray()));
		var history = new ClipboardHistory();
		history.Apply(await reader.ReadAsync(Parse(@"C:\old files\日本語.cs", 1), CancellationToken.None));
		history.Apply(await reader.ReadAsync(Parse(@"D:\new files\日本語.cs", 2), CancellationToken.None));

		Assert.AreEqual("old saved text", history.Previous?.Text);
		Assert.AreEqual("new saved text", history.Current?.Text);
		Assert.AreEqual("日本語.cs", history.Previous?.SourceFileName);
		Assert.AreEqual("日本語.cs", history.Current?.SourceFileName);
		Assert.AreEqual(@"C:\old files\日本語.cs", history.Previous?.SourceFilePath);
		Assert.AreEqual(@"D:\new files\日本語.cs", history.Current?.SourceFilePath);
	}

	[TestMethod]
	[DataRow("C:\\first.txt\nD:\\second.txt\n")]
	[DataRow("\"C:\\first.txt\"\r\nD:\\second.txt\r\n")]
	[DataRow("C:\\first.txt\n\"D:\\second.txt\"")]
	public async Task PairIsValidatedBeforeReadingAndReplacesHistoryInOrder(string text)
	{
		var probes = new List<string>();
		var reads = 0;
		var reader = new ClipboardFileObservationReader(
			path => { probes.Add(path); return true; },
			(paths, _) =>
			{
				CollectionAssert.AreEqual(paths.ToArray(), probes.ToArray());
				reads++;
				return ValueTask.FromResult<IReadOnlyList<CopiedFileText>>(
					[new("first saved", "first.txt", paths[0]), new("second saved", "second.txt", paths[1])]);
			});
		var history = new ClipboardHistory();
		history.Apply(ClipboardObservation.TextValue(1, Now, "older"));
		history.Apply(ClipboardObservation.TextValue(2, Now, "current"));
		var observation = await reader.ReadAsync(Parse(text, 3), CancellationToken.None);

		Assert.AreEqual("current", history.Current?.Text);
		Assert.AreEqual(ClipboardObservationKind.TextPair, observation.Kind);
		history.Apply(observation);

		Assert.AreEqual(1, reads);
		Assert.AreEqual(2, history.Entries.Count);
		Assert.AreEqual("first saved", history.Previous?.Text);
		Assert.AreEqual("second saved", history.Current?.Text);
		Assert.AreEqual(@"C:\first.txt", history.Previous?.SourceFilePath);
		Assert.AreEqual(@"D:\second.txt", history.Current?.SourceFilePath);
	}

	[TestMethod]
	[DataRow("C:\\missing.txt\r\n")]
	[DataRow("C:\\existing.txt\nD:\\missing.txt\n")]
	[DataRow("\"C:\\missing.txt\"\r\nD:\\existing.txt")]
	[DataRow("C:\\one.txt\nC:\\two.txt\nC:\\missing.txt")]
	public async Task FailedProbePreservesEntireOriginalPayloadWithoutReadingFiles(string text)
	{
		var reader = new ClipboardFileObservationReader(
			path => !path.Contains("missing", StringComparison.Ordinal),
			(_, _) => throw new AssertFailedException("No file may be read after a failed existence check."));
		var observation = await reader.ReadAsync(Parse(text), CancellationToken.None);

		Assert.AreEqual(ClipboardObservationKind.Text, observation.Kind);
		Assert.AreEqual(text, observation.Text);
		Assert.IsNull(observation.SourceFileName);
		Assert.IsNull(observation.SourceFilePath);
	}

	[TestMethod]
	[DataRow("C:\\one.txt\nC:\\two.txt\nC:\\three.txt", 3)]
	[DataRow("\"C:\\one.txt\"\n\"C:\\two.txt\"\n\"C:\\three.txt\"", 0)]
	public async Task MoreThanTwoValidPathsDoNotReadFilesOrChangeCaptures(string text, int expectedProbes)
	{
		var probes = 0;
		var reader = new ClipboardFileObservationReader(
			_ => { probes++; return true; },
			(_, _) => throw new AssertFailedException("A list of more than two files must not be read."));
		var history = new ClipboardHistory();
		history.Apply(ClipboardObservation.TextValue(1, Now, "older"));
		history.Apply(ClipboardObservation.TextValue(2, Now, "current"));
		var observation = await reader.ReadAsync(Parse(text, 3), CancellationToken.None);
		history.Apply(observation);

		Assert.AreEqual(expectedProbes, probes);
		Assert.AreEqual(ClipboardObservationKind.NonText, observation.Kind);
		Assert.AreEqual("older", history.Previous?.Text);
		Assert.AreEqual("current", history.Current?.Text);
	}

	[TestMethod]
	public async Task QuotedAndStructuredPathsDoNotRequireExistenceProbes()
	{
		var reader = new ClipboardFileObservationReader(
			_ => throw new AssertFailedException("Quoted and structured file copies must keep their fallbacks."),
			(paths, _) => ValueTask.FromResult<IReadOnlyList<CopiedFileText>>(
				[new("missing.txt (file not found)", "missing.txt", paths[0])]));
		var quoted = await reader.ReadAsync(Parse("\"C:\\missing.txt\""), CancellationToken.None);
		var structured = await reader.ReadAsync(new(7, Now, [@"C:\missing.txt"]), CancellationToken.None);

		Assert.AreEqual("missing.txt (file not found)", quoted.Text);
		Assert.AreEqual(quoted, structured);
	}

	[TestMethod]
	public async Task PartialPairCannotReplaceHistory()
	{
		var reader = new ClipboardFileObservationReader(_ => true,
			(paths, _) => ValueTask.FromResult<IReadOnlyList<CopiedFileText>>([new("first", "first.txt", paths[0])]));
		var observation = await reader.ReadAsync(Parse("C:\\first.txt\nD:\\second.txt"), CancellationToken.None);

		Assert.AreEqual(ClipboardObservationKind.NonText, observation.Kind);
	}

	[TestMethod]
	public async Task CancelledSlowProbeAllowsNewerCaptureWithoutWaitingForNetworkAccess()
	{
		using var release = new ManualResetEventSlim();
		using var cancellation = new CancellationTokenSource();
		var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var history = new ClipboardHistory();
		var reader = new ClipboardFileObservationReader(path =>
		{
			if (path.StartsWith(@"\\slow", StringComparison.Ordinal))
			{
				started.SetResult();
				Assert.IsTrue(release.Wait(TimeSpan.FromSeconds(5)));
				finished.SetResult();
			}

			return true;
		}, (paths, _) => ValueTask.FromResult<IReadOnlyList<CopiedFileText>>([new("saved", "file.txt", paths[0])]));
		var stale = reader.ReadAsync(Parse(@"\\slow\share\file.txt", 1), cancellation.Token).AsTask();

		try
		{
			await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
			cancellation.Cancel();
			await Assert.ThrowsAsync<OperationCanceledException>(() => stale.WaitAsync(TimeSpan.FromSeconds(2)));
			var current = await reader.ReadAsync(Parse(@"C:\file.txt", 2), CancellationToken.None)
				.AsTask().WaitAsync(TimeSpan.FromSeconds(2));
			history.Apply(current);

			Assert.AreEqual(@"C:\file.txt", history.Current?.SourceFilePath);
			Assert.AreEqual(2u, history.LastSequenceNumber);
		}
		finally
		{
			release.Set();
			await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
		}
	}

	[TestMethod]
	public async Task CancellationRejectsReaderThatIgnoresTokenWithoutPublishingHalfPair()
	{
		using var cancellation = new CancellationTokenSource();
		var pending = new TaskCompletionSource<IReadOnlyList<CopiedFileText>>(TaskCreationOptions.RunContinuationsAsynchronously);
		var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var history = new ClipboardHistory();
		var reader = new ClipboardFileObservationReader(_ => true, (_, _) =>
		{
			started.SetResult();
			return new(pending.Task);
		});
		var stale = reader.ReadAsync(Parse("C:\\first.txt\nD:\\second.txt", 1), cancellation.Token).AsTask();
		await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
		cancellation.Cancel();
		history.Apply(ClipboardObservation.TextValue(2, Now, "newer text"));
		pending.SetResult([new("stale first", "first.txt", @"C:\first.txt"), new("stale second", "second.txt", @"D:\second.txt")]);

		await Assert.ThrowsAsync<OperationCanceledException>(() => stale);
		Assert.AreEqual("newer text", history.Current?.Text);
		Assert.IsNull(history.Previous);
	}

	[TestMethod]
	public async Task DefaultExistenceProbePreservesUnquotedMissingFilesAndDirectories()
	{
		await WithWorkspaceAsync(async directory =>
		{
			var reader = new ClipboardFileObservationReader();

			foreach (var path in new[] { directory, Path.Combine(directory, "missing.txt") })
			{
				var text = path + "\r\n";
				var observation = await reader.ReadAsync(new(7, Now, [path], text), CancellationToken.None);

				Assert.AreEqual(text, observation.Text);
				Assert.IsNull(observation.SourceFileName);
			}
		});
	}

	[TestMethod]
	public async Task ExistingPathsReuseEncodingAndBinaryFallbacks()
	{
		await WithWorkspaceAsync(async directory =>
		{
			var previous = Path.Combine(directory, "日本語.cs");
			var current = Path.Combine(directory, "program.exe");
			await File.WriteAllTextAsync(previous, "saved ☃\r\n", System.Text.Encoding.Unicode);
			await File.WriteAllBytesAsync(current, [0x4D, 0x5A]);
			var observation = await new ClipboardFileObservationReader().ReadAsync(
				new(7, Now, [previous, current], previous + "\n" + current), CancellationToken.None);

			Assert.AreEqual(ClipboardObservationKind.TextPair, observation.Kind);
			Assert.AreEqual("saved ☃\r\n", observation.PreviousText);
			Assert.AreEqual("program.exe (binary file)", observation.Text);
			Assert.AreEqual("日本語.cs", observation.PreviousSourceFileName);
			Assert.AreEqual("program.exe", observation.SourceFileName);
		});
	}

	[TestMethod]
	public async Task FilesDeletedAfterRecognitionStillPublishCompleteFallbackPair()
	{
		await WithWorkspaceAsync(async directory =>
		{
			var previous = Path.Combine(directory, "previous.txt");
			var current = Path.Combine(directory, "current.txt");
			await File.WriteAllTextAsync(previous, "previous");
			await File.WriteAllTextAsync(current, "current");
			var reader = new ClipboardFileObservationReader(path =>
			{
				var exists = File.Exists(path);

				if (path == current)
				{
					File.Delete(previous);
					File.Delete(current);
				}

				return exists;
			});
			var observation = await reader.ReadAsync(
				new(7, Now, [previous, current], previous + "\n" + current), CancellationToken.None);

			Assert.AreEqual(ClipboardObservationKind.TextPair, observation.Kind);
			Assert.AreEqual("previous.txt (file not found)", observation.PreviousText);
			Assert.AreEqual("current.txt (file not found)", observation.Text);
		});
	}

	[TestMethod]
	public async Task ExistingEmptyOversizedAndUnreadableFilesKeepConversionFallbacks()
	{
		await WithWorkspaceAsync(async directory =>
		{
			var empty = Path.Combine(directory, "empty.txt");
			var large = Path.Combine(directory, "large.txt");
			var locked = Path.Combine(directory, "locked.txt");
			await File.WriteAllTextAsync(empty, "");
			await File.WriteAllTextAsync(large, "five!");
			await File.WriteAllTextAsync(locked, "text");
			var reader = new ClipboardFileObservationReader(readFiles: new CopiedFileTextReader(4).ReadValuesAsync);
			var pair = await reader.ReadAsync(new(7, Now, [empty, large], empty + "\n" + large), CancellationToken.None);
			await using var exclusive = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None);
			var unreadable = await reader.ReadAsync(new(8, Now, [locked], locked), CancellationToken.None);

			Assert.AreEqual("empty.txt (empty file)", pair.PreviousText);
			Assert.AreEqual("large.txt (file too large)", pair.Text);
			Assert.AreEqual("locked.txt (file unreadable)", unreadable.Text);
		});
	}

	[TestMethod]
	public async Task DefaultReaderKeepsQuotedMissingFileAndDirectoryFallbacks()
	{
		await WithWorkspaceAsync(async directory =>
		{
			var missing = Path.Combine(directory, "missing.txt");
			var observation = await new ClipboardFileObservationReader().ReadAsync(
				new(7, Now, [missing, directory]), CancellationToken.None);

			Assert.AreEqual("missing.txt (file not found)", observation.PreviousText);
			Assert.AreEqual(Path.GetFileName(directory) + " (directory)", observation.Text);
		});
	}

	private static async Task WithWorkspaceAsync(Func<string, Task> run)
	{
		var directory = Path.Combine(Path.GetTempPath(), "ClipDiff.PathCaptureTests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);

		try
		{
			await run(directory);
		}
		finally
		{
			Directory.Delete(directory, true);
		}
	}

	private static ClipboardInspection.CopiedFiles Parse(string text, uint sequence = 7)
	{
		Assert.IsTrue(ExplorerCopyAsPathParser.TryParse(text, out var paths, out var requiresExistingFiles));
		return new(sequence, Now, paths, requiresExistingFiles ? text : null);
	}
}
