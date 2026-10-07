using System.IO;
using System.IO.Pipes;

namespace ClipDiff.Windows.Explorer;

internal sealed class ExplorerCommandServer : IDisposable
{
	private const int MaximumPendingMessages = 4;
	private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(100);
	private static readonly TimeSpan DefaultMessageTimeout = TimeSpan.FromSeconds(1);
	private readonly Func<string, Task> selectedFileHandler;
	private readonly string pipeName;
	private readonly TimeSpan messageTimeout;
	private readonly CancellationTokenSource shutdown = new();
	private readonly Lock gate = new();
	private readonly HashSet<NamedPipeServerStream> activePipes = [];
	private readonly Task listenTask;
	private long latestAccepted;
	private bool disposed;

	public ExplorerCommandServer(Func<string, Task> handler)
		: this(handler, ExplorerCommandClient.GetPipeName(), DefaultMessageTimeout)
	{
	}

	internal ExplorerCommandServer(Func<string, Task> handler, string name, TimeSpan timeout)
	{
		selectedFileHandler = handler ?? throw new ArgumentNullException(nameof(handler));
		pipeName = name;
		messageTimeout = timeout;
		listenTask = Task.Run(() => ListenAsync(shutdown.Token));
	}

	public void Dispose()
	{
		lock (gate)
		{
			if (disposed)
			{
				return;
			}

			disposed = true;
			shutdown.Cancel();

			foreach (var pipe in activePipes)
			{
				pipe.Dispose();
			}
		}

		_ = listenTask.ContinueWith(
			static (_, state) => ((CancellationTokenSource)state!).Dispose(), shutdown,
			CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
	}

	private async Task ListenAsync(CancellationToken cancellationToken)
	{
		using var slots = new SemaphoreSlim(MaximumPendingMessages);
		var receipts = new List<Task>();
		long sequence = 0;

		try
		{
			while (!cancellationToken.IsCancellationRequested)
			{
				await slots.WaitAsync(cancellationToken).ConfigureAwait(false);
				NamedPipeServerStream? pipe = null;

				try
				{
					pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut,
						MaximumPendingMessages, PipeTransmissionMode.Byte,
						PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

					lock (gate)
					{
						cancellationToken.ThrowIfCancellationRequested();
						activePipes.Add(pipe);
					}

					await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
					receipts.RemoveAll(task => task.IsCompleted);
					receipts.Add(ReceiveAsync(pipe, ++sequence, slots, cancellationToken));
					pipe = null; // Receipt now owns the pipe and slot.
				}
				catch
				{
					if (pipe is not null)
					{
						RemovePipe(pipe);
						pipe.Dispose();
					}

					slots.Release();
					cancellationToken.ThrowIfCancellationRequested();
					await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
				}
			}
		}
		catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException)
		{
			// Shutdown disposes the listening and receiving pipes.
		}
		finally
		{
			await Task.WhenAll(receipts).ConfigureAwait(false);
		}
	}

	private async Task ReceiveAsync(NamedPipeServerStream pipe, long sequence,
		SemaphoreSlim slots, CancellationToken cancellationToken)
	{
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		deadline.CancelAfter(messageTimeout);

		try
		{
			var filePath = await ExplorerCommandProtocol.ReadFilePathAsync(pipe, deadline.Token).ConfigureAwait(false);
			var accepted = false;

			lock (gate)
			{
				if (!disposed && !deadline.IsCancellationRequested && filePath is not null && sequence > latestAccepted)
				{
					latestAccepted = sequence;
					// The handler queues UI work before returning its task. Never wait
					// for file I/O before accepting another Explorer command.
					_ = ObserveHandlerAsync(selectedFileHandler(filePath));
					accepted = true;
				}
			}

			await ExplorerCommandProtocol.WriteDeliveryResultAsync(pipe, accepted, deadline.Token).ConfigureAwait(false);
		}
		catch (Exception)
		{
			// Malformed, abandoned, or timed-out clients do not monopolize the listener.
		}
		finally
		{
			RemovePipe(pipe);
			pipe.Dispose();
			slots.Release();
		}
	}

	private static async Task ObserveHandlerAsync(Task task)
	{
		try
		{
			await task.ConfigureAwait(false);
		}
		catch (Exception)
		{
			// A failed command must not stop command receipt or log selected paths.
		}
	}

	private void RemovePipe(NamedPipeServerStream pipe)
	{
		lock (gate)
		{
			activePipes.Remove(pipe);
		}
	}
}
