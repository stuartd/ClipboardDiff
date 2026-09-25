using System.IO;
using System.IO.Pipes;

namespace ClipDiff.Windows.Explorer;

internal sealed class ExplorerCommandServer : IDisposable
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(100);
    private readonly Func<string, Task> selectedFileHandler;
    private readonly CancellationTokenSource shutdown = new();
    private readonly Lock gate = new();
    private readonly Task listenTask;
    private NamedPipeServerStream? activePipe;
    private bool disposed;

    public ExplorerCommandServer(Func<string, Task> selectedFileHandler)
    {
        this.selectedFileHandler = selectedFileHandler ??
								   throw new ArgumentNullException(nameof(selectedFileHandler));
        var cancellationToken = shutdown.Token;
        listenTask = Task.Run(() => ListenAsync(cancellationToken), CancellationToken.None);
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
            activePipe?.Dispose();
            activePipe = null;
        }

        _ = listenTask.ContinueWith(
            static (_, state) => ((CancellationTokenSource)state!).Dispose(),
            shutdown,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(
                    ExplorerCommandClient.GetPipeName(),
                    PipeDirection.In,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                SetActivePipe(pipe);
                try
                {
                    await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                    var filePath = await ExplorerCommandProtocol.ReadFilePathAsync(
                        pipe,
                        cancellationToken).ConfigureAwait(false);
                    if (filePath is not null)
                    {
                        await selectedFileHandler(filePath).ConfigureAwait(false);
                    }
                }
                finally
                {
                    ClearActivePipe(pipe);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private void SetActivePipe(NamedPipeServerStream pipe)
    {
        lock (gate)
        {
            if (disposed)
            {
                pipe.Dispose();
                return;
            }

            activePipe = pipe;
        }
    }

    private void ClearActivePipe(NamedPipeServerStream pipe)
    {
        lock (gate)
        {
            if (ReferenceEquals(activePipe, pipe))
            {
                activePipe = null;
            }
        }
    }
}
