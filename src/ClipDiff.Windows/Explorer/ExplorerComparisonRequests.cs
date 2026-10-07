using System.IO;
using System.Security;
using ClipDiff.Windows.Clipboard;

namespace ClipDiff.Windows.Explorer;

// Start, cancellation and publication run on the UI thread. The reader can
// ignore cancellation; request identity still prevents stale history updates.
internal sealed class ExplorerComparisonRequests
{
	private readonly ClipboardHistory clipboardHistory;
	private readonly Func<IReadOnlyList<string>, CancellationToken, ValueTask<IReadOnlyList<CopiedFileText>>> readValues;
	private readonly Action started;
	private readonly Action publish;
	private readonly Action unavailable;
	private CancellationTokenSource? active;

	public ExplorerComparisonRequests(
		ClipboardHistory history,
		Func<IReadOnlyList<string>, CancellationToken, ValueTask<IReadOnlyList<CopiedFileText>>> readFiles,
		Action onStarted,
		Action onPublish,
		Action onUnavailable)
	{
		clipboardHistory = history;
		readValues = readFiles;
		started = onStarted;
		publish = onPublish;
		unavailable = onUnavailable;
	}

	public void Cancel()
	{
		active?.Cancel();
		active = null;
	}

	public async Task RunAsync(IReadOnlyList<string> selectedPaths, bool useCurrentCapture)
	{
		if (!clipboardHistory.IsMonitoring ||
			selectedPaths.Count != (useCurrentCapture ? 1 : 2) ||
			(useCurrentCapture && clipboardHistory.Current is null))
		{
			unavailable();
			return;
		}

		string[] fullPaths;

		try
		{
			fullPaths = selectedPaths.Select(Path.GetFullPath).ToArray();
		}
		catch (Exception exception) when (exception is ArgumentException or NotSupportedException or
			PathTooLongException or SecurityException)
		{
			unavailable();
			return;
		}

		Cancel();
		using var cancellation = new CancellationTokenSource();
		active = cancellation;
		var sourceId = useCurrentCapture ? clipboardHistory.Current!.Id : (Guid?)null;
		started();

		try
		{
			var values = await readValues(fullPaths, cancellation.Token);

			if (!ReferenceEquals(active, cancellation) || cancellation.IsCancellationRequested ||
				!clipboardHistory.IsMonitoring ||
				(useCurrentCapture && clipboardHistory.Current?.Id != sourceId))
			{
				return;
			}

			if (values.Count != fullPaths.Length || values.Any(value => value.Text.Length == 0))
			{
				unavailable();
				return;
			}

			if (useCurrentCapture)
			{
				var value = values[0];
				clipboardHistory.AcceptDirectText(value.Text, DateTimeOffset.Now, value.FileName, value.FilePath);
			}
			else
			{
				var previous = values[0];
				var current = values[1];
				clipboardHistory.AcceptDirectPair(previous.Text, current.Text, DateTimeOffset.Now,
					previous.FileName, current.FileName, previous.FilePath, current.FilePath);
			}

			publish();
		}
		catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
		{
			// Superseded, privacy-cleared or shutting down.
		}
		finally
		{
			if (ReferenceEquals(active, cancellation))
			{
				active = null;
			}
		}
	}
}
