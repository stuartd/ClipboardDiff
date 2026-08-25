using System.Diagnostics;
using System.IO;
using System.IO.Pipes;

namespace ClipDiff.Windows.Explorer;

internal static class ExplorerCommandClient
{
    private const int ConnectionTimeoutMilliseconds = 2_000;
    private static readonly string PipeName = CreatePipeName();

    public static bool TrySendSelectedFile(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        try
        {
            using var pipe = new NamedPipeClientStream(
                ".",
                PipeName,
                PipeDirection.Out,
                PipeOptions.Asynchronous);
            pipe.Connect(ConnectionTimeoutMilliseconds);
            ExplorerCommandProtocol.WriteFilePathAsync(pipe, filePath).AsTask().GetAwaiter().GetResult();
            return true;
        }
        catch (Exception exception) when (exception is TimeoutException or IOException or
                                          UnauthorizedAccessException or InvalidOperationException or
                                          ArgumentException or ObjectDisposedException)
        {
            return false;
        }
    }

    internal static string GetPipeName() => PipeName;

    private static string CreatePipeName()
    {
        using var process = Process.GetCurrentProcess();
        return $"ClipDiff.ExplorerCommand.{process.SessionId}";
    }
}
