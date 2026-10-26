namespace ClipDiff.Windows.Clipboard;

internal sealed class ClipboardInspectionReader(
	Func<bool> tryOpen,
	Action close,
	Func<uint, DateTimeOffset, ClipboardInspection> inspect)
{
	public Task<ClipboardInspection?> ReadAsync(
		uint sequenceNumber,
		DateTimeOffset observedAt,
		CancellationToken cancellationToken)
	{
		// GetClipboardData can wait for another process to render a format. Keep
		// Open/Inspect/Close on one worker thread, away from the UI message loop.
		// The monitor retains its read gate until this completes: cancellation
		// must not start another native read while this one owns the clipboard.
		return Task.Run<ClipboardInspection?>(() =>
		{
			if (!tryOpen())
			{
				return null;
			}

			try
			{
				cancellationToken.ThrowIfCancellationRequested();
				var inspection = inspect(sequenceNumber, observedAt);
				cancellationToken.ThrowIfCancellationRequested();
				return inspection;
			}
			finally
			{
				close();
			}
		}, cancellationToken);
	}
}
