using System.Runtime.InteropServices;
using System.Text;
using ClipDiff.Windows.Clipboard;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClipDiff.Windows.Tests;

[TestClass]
public sealed class CopiedShortcutTextReaderTests
{
	private string testDirectory = null!;

	[TestInitialize]
	public void Initialize()
	{
		testDirectory = Path.Combine(Path.GetTempPath(), "ClipDiff.ShortcutTests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(testDirectory);
	}

	[TestCleanup]
	public void Cleanup()
	{
		Directory.Delete(testDirectory, recursive: true);
	}

	[TestMethod]
	public async Task ShortcutReadsTargetContentsAndRetainsTargetIdentity()
	{
		var shortcut = await WriteFileAsync("copied.LNK", "shortcut placeholder");
		var target = Path.Combine(testDirectory, "日本語 saved text.txt");
		const string contents = "saved ☃\r\n\tcontents\r\n";
		await File.WriteAllTextAsync(target, contents, Encoding.Unicode);
		var reader = new CopiedFileTextReader(path => path == shortcut ? target : null);

		var result = await reader.ReadFileAsync(shortcut);

		Assert.IsNotNull(result);
		Assert.AreEqual(contents, result.Text);
		Assert.AreEqual(Path.GetFileName(target), result.FileName);
		Assert.AreEqual(target, result.FilePath);
	}

	[TestMethod]
	public async Task ShortcutAndOrdinaryFileKeepPairOrdering()
	{
		var shortcut = await WriteFileAsync("previous.lnk", "shortcut placeholder");
		var target = await WriteFileAsync("previous.txt", "old contents");
		var current = await WriteFileAsync("current.txt", "new contents");
		var reader = new CopiedFileTextReader(path => path == shortcut ? target : null);

		var values = await reader.ReadValuesAsync([shortcut, current]);

		CollectionAssert.AreEqual(new[] { "old contents", "new contents" }, values.Select(value => value.Text).ToArray());
		CollectionAssert.AreEqual(new[] { target, current }, values.Select(value => value.FilePath).ToArray());
	}

	[TestMethod]
	[DataRow("binary.exe", "binary file")]
	[DataRow("empty.txt", "empty file")]
	[DataRow("missing.txt", "file not found")]
	[DataRow("large.txt", "file too large")]
	[DataRow("folder", "directory")]
	public async Task TargetUsesOrdinaryFileFallbacks(string name, string reason)
	{
		var shortcut = await WriteFileAsync("copied.lnk", "link");
		var target = Path.Combine(testDirectory, name);

		if (name == "folder")
		{
			Directory.CreateDirectory(target);
		}
		else if (name != "missing.txt")
		{
			await File.WriteAllTextAsync(target, name == "empty.txt" ? "" : "longer than the limit");
		}

		var result = await new CopiedFileTextReader(_ => target, maximumTextFileBytes: 4).ReadFileAsync(shortcut);

		Assert.IsNotNull(result);
		Assert.AreEqual($"{name} ({reason})", result.Text);
		Assert.AreEqual(name, result.FileName);
		Assert.AreEqual(target, result.FilePath);
	}

	[TestMethod]
	[DataRow(null)]
	[DataRow("")]
	[DataRow("relative.txt")]
	[DataRow("https://example.test/file.txt")]
	public async Task UnusableTargetFallsBackToShortcutName(string? target)
	{
		var shortcut = await WriteFileAsync("copied.lnk", "shortcut placeholder");

		var result = await new CopiedFileTextReader(_ => target).ReadFileAsync(shortcut);

		Assert.IsNotNull(result);
		Assert.AreEqual("copied.lnk (file unreadable)", result.Text);
		Assert.AreEqual(shortcut, result.FilePath);
	}

	[TestMethod]
	public async Task ShellFailureIsNonfatalAndDoesNotExposeExceptionText()
	{
		var shortcut = await WriteFileAsync("copied.lnk", "shortcut placeholder");
		var reader = new CopiedFileTextReader(_ => throw new COMException("private path or contents"));

		var result = await reader.ReadFileAsync(shortcut);

		Assert.IsNotNull(result);
		Assert.AreEqual("copied.lnk (file unreadable)", result.Text);
	}

	[TestMethod]
	public async Task ShortcutChainReachesTextAndCyclesStop()
	{
		var first = await WriteFileAsync("first.lnk", "shortcut placeholder");
		var second = await WriteFileAsync("second.lnk", "shortcut placeholder");
		var target = await WriteFileAsync("target.txt", "saved contents");
		var reader = new CopiedFileTextReader(path => path == first ? second : target);

		Assert.AreEqual("saved contents", await reader.ReadAsync([first]));

		var cyclicReader = new CopiedFileTextReader(path => path == first ? second : first);

		Assert.AreEqual("first.lnk (file unreadable)", await cyclicReader.ReadAsync([first]));
	}

	[TestMethod]
	public async Task ShortcutDepthIsBounded()
	{
		var paths = new List<string>();

		for (var index = 0; index < 9; index++)
		{
			paths.Add(await WriteFileAsync($"{index}.lnk", "shortcut placeholder"));
		}

		var reads = 0;
		var reader = new CopiedFileTextReader(_ => paths[++reads]);

		Assert.AreEqual("8.lnk (file unreadable)", await reader.ReadAsync([paths[0]]));
		Assert.AreEqual(8, reads);
	}

	[TestMethod]
	public async Task OversizedShortcutIsRejectedBeforeLoading()
	{
		var shortcut = await WriteFileAsync("large.lnk", "longer than the limit");
		var reader = new CopiedFileTextReader(
			_ => throw new AssertFailedException("An oversized shortcut must not be loaded."), maximumTextFileBytes: 4);

		Assert.AreEqual("large.lnk (file too large)", await reader.ReadAsync([shortcut]));
	}

	[TestMethod]
	public async Task CancelledResolutionDoesNotReadTarget()
	{
		var shortcut = await WriteFileAsync("copied.lnk", "shortcut placeholder");
		using var cancellation = new CancellationTokenSource();
		var reader = new CopiedFileTextReader(_ =>
		{
			cancellation.Cancel();
			return Path.Combine(testDirectory, "unread.txt");
		});

		await Assert.ThrowsAsync<OperationCanceledException>(() => reader.ReadFileAsync(shortcut, cancellation.Token).AsTask());
	}

	[TestMethod]
	public async Task SlowShortcutCannotBlockOrReplaceNewerCapture()
	{
		var shortcut = await WriteFileAsync("slow.lnk", "shortcut placeholder");
		var target = await WriteFileAsync("newer.txt", "newer saved contents");
		using var cancellation = new CancellationTokenSource();
		using var release = new ManualResetEventSlim();
		var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var fileReader = new CopiedFileTextReader(_ =>
		{
			started.SetResult();
			Assert.IsTrue(release.Wait(TimeSpan.FromSeconds(5)));
			finished.SetResult();

			return target;
		});
		var observationReader = new ClipboardFileObservationReader(readFiles: fileReader.ReadValuesAsync);
		var stale = observationReader.ReadAsync(new(1, DateTimeOffset.Now, [shortcut]), cancellation.Token).AsTask();

		try
		{
			await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
			cancellation.Cancel();
			await Assert.ThrowsAsync<OperationCanceledException>(() => stale.WaitAsync(TimeSpan.FromSeconds(2)));

			var current = await observationReader.ReadAsync(new(2, DateTimeOffset.Now, [target]), CancellationToken.None)
				.AsTask().WaitAsync(TimeSpan.FromSeconds(2));
			var history = new ClipboardHistory();
			history.Apply(current);

			Assert.AreEqual("newer saved contents", history.Current?.Text);
			Assert.AreEqual(target, history.Current?.SourceFilePath);
			Assert.AreEqual(2u, history.LastSequenceNumber);
		}
		finally
		{
			release.Set();
			await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
		}
	}

	[TestMethod]
	public async Task OrdinaryTextDoesNotInvokeShortcutReader()
	{
		var path = await WriteFileAsync("ordinary.txt", "saved contents");
		var reader = new CopiedFileTextReader(_ => throw new AssertFailedException("Only .lnk files should be resolved."));

		Assert.AreEqual("saved contents", await reader.ReadAsync([path]));
	}

	private async Task<string> WriteFileAsync(string name, string contents)
	{
		var path = Path.Combine(testDirectory, name);
		await File.WriteAllTextAsync(path, contents, new UTF8Encoding(false));

		return path;
	}
}
