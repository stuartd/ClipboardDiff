using System.Diagnostics;
using System.IO;
using System.IO.Pipes;

namespace ClipDiff.Windows.Explorer;

internal static class ExplorerCommandClient
{
	private static readonly TimeSpan DeliveryTimeout = TimeSpan.FromSeconds(2);
	private static readonly string PipeName = CreatePipeName();

	public static bool TrySendSelectedFile(string filePath) =>
		TrySendSelectedFileAsync(filePath, PipeName).GetAwaiter().GetResult();

	internal static async Task<bool> TrySendSelectedFileAsync(string filePath, string name)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

		try
		{
			using var deadline = new CancellationTokenSource(DeliveryTimeout);
			using var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
			await pipe.ConnectAsync(deadline.Token).ConfigureAwait(false);
			await ExplorerCommandProtocol.WriteFilePathAsync(pipe, filePath, deadline.Token).ConfigureAwait(false);
			return await ExplorerCommandProtocol.ReadDeliveryResultAsync(pipe, deadline.Token).ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is TimeoutException or IOException or
			UnauthorizedAccessException or InvalidOperationException or ArgumentException or
			ObjectDisposedException or OperationCanceledException)
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
