using ClipDiff.Windows.Clipboard;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClipDiff.Windows.Tests;

[TestClass]
public sealed class ClipboardInspectionReaderTests
{
	private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

	[TestMethod]
	public async Task BlockedRendererDoesNotBlockCallerAndClosesOnTheOpeningThread()
	{
		using var release = new ManualResetEventSlim();
		var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var callingThread = 0;
		var openingThread = 0;
		var closingThread = 0;
		var reader = new ClipboardInspectionReader(
			() => { openingThread = Environment.CurrentManagedThreadId; return true; },
			() => closingThread = Environment.CurrentManagedThreadId,
			(sequence, time) =>
			{
				Assert.AreEqual(openingThread, Environment.CurrentManagedThreadId);
				started.SetResult();
				Assert.IsTrue(release.Wait(TimeSpan.FromSeconds(5)));
				return new ClipboardInspection.Completed(ClipboardObservation.TextValue(sequence, time, "synthetic"));
			});
		var returned = new TaskCompletionSource<Task<ClipboardInspection?>>(TaskCreationOptions.RunContinuationsAsynchronously);
		var caller = new Thread(() =>
		{
			callingThread = Environment.CurrentManagedThreadId;
			returned.SetResult(reader.ReadAsync(7, Now, CancellationToken.None));
		});
		caller.Start();

		try
		{
			var pending = await returned.Task.WaitAsync(TimeSpan.FromSeconds(3));
			await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
			Assert.AreNotEqual(callingThread, openingThread);
			Assert.IsFalse(pending.IsCompleted);
			Assert.AreEqual(0, closingThread);
		}
		finally
		{
			release.Set();
			Assert.IsTrue(caller.Join(TimeSpan.FromSeconds(3)));
			await (await returned.Task).WaitAsync(TimeSpan.FromSeconds(3));
		}

		var completed = (ClipboardInspection.Completed)(await await returned.Task)!;
		Assert.AreEqual(7u, completed.Observation.SequenceNumber);
		Assert.AreEqual(Now, completed.Observation.ObservedAt);
		Assert.AreEqual(openingThread, closingThread);
	}

	[TestMethod]
	public async Task FailedOpenDoesNotInspectOrCloseAnotherApplicationsClipboard()
	{
		var reader = new ClipboardInspectionReader(
			() => false,
			() => Assert.Fail("A failed open must not be closed."),
			(_, _) => throw new AssertFailedException("A failed open must not inspect data."));

		Assert.IsNull(await reader.ReadAsync(7, Now, CancellationToken.None));
	}

	[TestMethod]
	public async Task InspectionFailureStillClosesClipboardExactlyOnce()
	{
		var closes = 0;
		var reader = new ClipboardInspectionReader(
			() => true,
			() => Interlocked.Increment(ref closes),
			(_, _) => throw new InvalidOperationException("Synthetic inspection failure."));

		await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => reader.ReadAsync(7, Now, CancellationToken.None));
		Assert.AreEqual(1, closes);
	}

	[TestMethod]
	public async Task CancellationAfterOpenClosesClipboardWithoutRetrievingPayload()
	{
		using var cancellation = new CancellationTokenSource();
		var closes = 0;
		var reader = new ClipboardInspectionReader(
			() => { cancellation.Cancel(); return true; },
			() => Interlocked.Increment(ref closes),
			(_, _) => throw new AssertFailedException("Cancellation must prevent inspection."));

		await Assert.ThrowsAsync<OperationCanceledException>(() => reader.ReadAsync(7, Now, cancellation.Token));
		Assert.AreEqual(1, closes);
	}

	[TestMethod]
	public async Task CancellingBlockedInspectionDoesNotAbandonAnOpenClipboard()
	{
		using var release = new ManualResetEventSlim();
		using var cancellation = new CancellationTokenSource();
		var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var closes = 0;
		var reader = new ClipboardInspectionReader(
			() => true,
			() => Interlocked.Increment(ref closes),
			(sequence, time) =>
			{
				started.SetResult();
				Assert.IsTrue(release.Wait(TimeSpan.FromSeconds(5)));
				return new ClipboardInspection.Completed(ClipboardObservation.TextValue(sequence, time, "synthetic stale text"));
			});
		var pending = reader.ReadAsync(7, Now, cancellation.Token);

		try
		{
			await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
			cancellation.Cancel();

			Assert.IsFalse(pending.IsCompleted);
			Assert.AreEqual(0, closes);
		}
		finally
		{
			release.Set();
			await Assert.ThrowsAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(3)));
		}

		Assert.AreEqual(1, closes);
	}

	[TestMethod]
	public async Task CancellationBeforeSchedulingDoesNotOpenClipboard()
	{
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();
		var reader = new ClipboardInspectionReader(
			() => throw new AssertFailedException("A cancelled read must not open the clipboard."),
			() => Assert.Fail("An unopened clipboard must not be closed."),
			(_, _) => throw new AssertFailedException("A cancelled read must not inspect data."));

		await Assert.ThrowsAsync<OperationCanceledException>(() => reader.ReadAsync(7, Now, cancellation.Token));
	}
}
