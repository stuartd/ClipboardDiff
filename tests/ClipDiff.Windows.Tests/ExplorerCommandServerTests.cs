using System.IO.Pipes;
using ClipDiff.Windows.Explorer;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClipDiff.Windows.Tests;

[TestClass]
public sealed class ExplorerCommandServerTests
{
	[TestMethod]
	public async Task SlowHandlerDoesNotBlockNextDelivery()
	{
		var name = Guid.NewGuid().ToString("N")[..12];
		var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var second = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
		var count = 0;
		using var server = new ExplorerCommandServer(path =>
		{
			if (Interlocked.Increment(ref count) == 1)
			{
				first.SetResult();
				return pending.Task;
			}

			second.SetResult(path);
			return Task.CompletedTask;
		}, name, TimeSpan.FromSeconds(1));

		try
		{
			Assert.IsTrue(await ExplorerCommandClient.TrySendSelectedFileAsync("first.txt", name));
			await first.Task.WaitAsync(TimeSpan.FromSeconds(3));
			await Task.Delay(TimeSpan.FromMilliseconds(2100));
			Assert.IsTrue(await ExplorerCommandClient.TrySendSelectedFileAsync("second.txt", name));
			Assert.AreEqual("second.txt", await second.Task.WaitAsync(TimeSpan.FromSeconds(3)));
			Assert.IsFalse(pending.Task.IsCompleted);
		}
		finally
		{
			pending.TrySetResult();
		}
	}

	[TestMethod]
	public async Task PartialClientDoesNotBlockHealthyClientAndIsDisconnectedAtDeadline()
	{
		var name = Guid.NewGuid().ToString("N")[..12];
		var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
		using var server = new ExplorerCommandServer(path =>
		{
			received.TrySetResult(path);
			return Task.CompletedTask;
		}, name, TimeSpan.FromMilliseconds(300));
		using var partial = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
		await partial.ConnectAsync(timeout.Token);
		await partial.WriteAsync(new byte[] { 5, 0 }, timeout.Token);

		Assert.IsTrue(await ExplorerCommandClient.TrySendSelectedFileAsync("healthy.txt", name));
		Assert.AreEqual("healthy.txt", await received.Task.WaitAsync(timeout.Token));
		var buffer = new byte[1];
		Assert.AreEqual(0, await partial.ReadAsync(buffer, timeout.Token));
	}

	[TestMethod]
	public async Task OlderPartialMessageCannotSupersedeNewerCompleteRequest()
	{
		var name = Guid.NewGuid().ToString("N")[..12];
		var calls = 0;
		using var server = new ExplorerCommandServer(_ =>
		{
			Interlocked.Increment(ref calls);
			return Task.CompletedTask;
		}, name, TimeSpan.FromSeconds(1));
		using var older = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
		await older.ConnectAsync(timeout.Token);
		await older.WriteAsync(new byte[] { 3, 0, 0, 0 }, timeout.Token);
		Assert.IsTrue(await ExplorerCommandClient.TrySendSelectedFileAsync("newer.txt", name));
		await older.WriteAsync("old"u8.ToArray(), timeout.Token);

		Assert.IsFalse(await ExplorerCommandProtocol.ReadDeliveryResultAsync(older, timeout.Token));
		Assert.AreEqual(1, calls);
	}

	[TestMethod]
	public async Task MissingServerReportsDeliveryFailure()
	{
		Assert.IsFalse(await ExplorerCommandClient.TrySendSelectedFileAsync(
			"selected.txt", Guid.NewGuid().ToString("N")[..12]));
	}
}
