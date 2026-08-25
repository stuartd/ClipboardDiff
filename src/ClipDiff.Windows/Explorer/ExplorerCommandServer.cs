using System.IO;
using System.IO.Pipes;

namespace ClipDiff.Windows.Explorer;

internal sealed class ExplorerCommandServer : IDisposable
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(100);
    private readonly Func<string, Task> _selectedFileHandler;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _gate = new();
    private readonly Task _listenTask;
    private NamedPipeServerStream? _activePipe;
    private bool _disposed;

    public ExplorerCommandServer(Func<string, Task> selectedFileHandler)
    {
        _selectedFileHandler = selectedFileHandler ??
            throw new ArgumentNullException(nameof(selectedFileHandler));
        var cancellationToken = _shutdown.Token;
        _listenTask = Task.Run(() => ListenAsync(cancellationToken), CancellationToken.None);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _shutdown.Cancel();
            _activePipe?.Dispose();
            _activePipe = null;
        }

        _ = _listenTask.ContinueWith(
            static (_, state) => ((CancellationTokenSource)state!).Dispose(),
            _shutdown,
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
                        await _selectedFileHandler(filePath).ConfigureAwait(false);
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
        lock (_gate)
        {
            if (_disposed)
            {
                pipe.Dispose();
                return;
            }

            _activePipe = pipe;
        }
    }

    private void ClearActivePipe(NamedPipeServerStream pipe)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_activePipe, pipe))
            {
                _activePipe = null;
            }
        }
    }
}
