using System.Globalization;
using System.IO;
using System.Net;
using System.Security;
using System.Text;
using System.Threading.Channels;
using ClipDiff.Windows.Updates;

namespace ClipDiff.Windows.Diagnostics;

internal enum DiagnosticEvent
{
	Starting,
	Stopped,
	UnhandledException,
	ClipboardListenerUnavailable,
	ClipboardReadFailed,
	ClipboardWriteFailed,
	HotKeyUnavailable,
	HotKeyChangeFailed,
	BuiltInDiffFailed,
	ExternalDiffFailed,
	ExplorerDeliveryFailed,
	SettingsSaveFailed,
	UpdateRequest,
	UpdateResponse,
	UpdateDecision
}

internal sealed class DiagnosticLog
{
	private const int MaxFileBytes = 256 * 1024;
	private readonly Channel<LogEntry> entries = Channel.CreateBounded<LogEntry>(new BoundedChannelOptions(128)
	{
		SingleReader = true,
		FullMode = BoundedChannelFullMode.DropOldest
	});
	private readonly Task writer;

	public DiagnosticLog(string? logFilePath = null)
	{
		writer = Task.Run(() => WriteEntriesAsync(logFilePath));
	}

	// Do not pass clipboard text or previews as diagnostic messages.
	public void Write(DiagnosticEvent diagnosticEvent, int? errorCode = null, Exception? exception = null) =>
		Enqueue(diagnosticEvent, errorCode is { } code ? $"code=0x{code:X8}" : null, exception);

	public void WriteUpdateRequest() =>
		Enqueue(DiagnosticEvent.UpdateRequest, $"GET {GitHubReleaseClient.LatestReleaseApi}");

	public void WriteUpdateResponse(HttpStatusCode? status, ReleaseVersion? releaseVersion) =>
		Enqueue(DiagnosticEvent.UpdateResponse,
			$"http_status={((int?)status)?.ToString(CultureInfo.InvariantCulture) ?? "none"} release={releaseVersion?.Text ?? "none"}");

	public void WriteUpdateDecision(bool manual, ReleaseVersion? installedVersion, UpdateCheckStatus status, UpdateCheckReason reason, Exception? exception = null) =>
		Enqueue(DiagnosticEvent.UpdateDecision,
			$"mode={(manual ? "manual" : "automatic")} installed={installedVersion?.Text ?? "unknown"} result={status} reason={reason}", exception);

	public Task CompleteAsync()
	{
		entries.Writer.TryComplete();
		return writer;
	}

	private void Enqueue(DiagnosticEvent diagnosticEvent, string? detail, Exception? exception = null)
	{
		if (exception is not null)
		{
			// ToString includes the type, message, stack trace and inner exceptions.
			// Escape newlines so a failure remains one timestamped log entry.
			var exceptionText = exception.ToString().Replace("\r", "\\r").Replace("\n", "\\n");

			// Keep unusually large exceptions within the file rotation limit.
			if (exceptionText.Length > 32768)
			{
				exceptionText = exceptionText[..32768] + " [truncated]";
			}

			detail = $"{detail} exception_code=0x{exception.HResult:X8} exception={exceptionText}".TrimStart();
		}

		entries.Writer.TryWrite(new LogEntry(DateTimeOffset.UtcNow, diagnosticEvent, detail));
	}

	private async Task WriteEntriesAsync(string? logFilePath)
	{
		await foreach (var entry in entries.Reader.ReadAllAsync().ConfigureAwait(false))
		{
			try
			{
				if (logFilePath is null)
				{
					var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
					if (localAppData.Length == 0)
					{
						continue;
					}
					logFilePath = Path.Combine(localAppData, "ClipDiff", "ClipDiff.log");
				}

				Append(logFilePath, entry);
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
			{
				// Try again on the next event if the log is unavailable.
			}
		}
	}

	private static void Append(string logFilePath, LogEntry entry)
	{
		var detail = entry.Detail is { } value ? $" {value}" : string.Empty;
		var line = FormattableString.Invariant($"{entry.Timestamp:O} [{Environment.ProcessId}] {entry.Event}{detail}{Environment.NewLine}");
		var bytes = Encoding.UTF8.GetBytes(line);

		Directory.CreateDirectory(Path.GetDirectoryName(logFilePath)!);
		// Hold the only writable handle during rotation so another ClipDiff session cannot race the size check.
		using var stream = new FileStream(logFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);

		if (stream.Length > MaxFileBytes - bytes.Length)
		{
			if (stream.Length <= MaxFileBytes)
			{
				using var previous = new FileStream(logFilePath + ".previous", FileMode.Create, FileAccess.Write, FileShare.Read);
				stream.CopyTo(previous);
			}
			stream.SetLength(0);
		}

		stream.Seek(0, SeekOrigin.End);
		stream.Write(bytes);
	}

	private readonly record struct LogEntry(DateTimeOffset Timestamp, DiagnosticEvent Event, string? Detail);
}
