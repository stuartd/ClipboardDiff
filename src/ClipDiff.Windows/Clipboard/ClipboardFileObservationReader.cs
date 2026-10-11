using System.IO;

namespace ClipDiff.Windows.Clipboard;

internal sealed class ClipboardFileObservationReader
{
	private readonly Func<string, bool> fileExists;
	private readonly Func<IReadOnlyList<string>, CancellationToken, ValueTask<IReadOnlyList<CopiedFileText>>> readValues;

	public ClipboardFileObservationReader(
		Func<string, bool>? exists = null,
		Func<IReadOnlyList<string>, CancellationToken, ValueTask<IReadOnlyList<CopiedFileText>>>? readFiles = null)
	{
		fileExists = exists ?? File.Exists;
		readValues = readFiles ?? new CopiedFileTextReader().ReadValuesAsync;
	}

	public async ValueTask<ClipboardObservation> ReadAsync(
		ClipboardInspection.CopiedFiles files,
		CancellationToken cancellationToken)
	{
		// File.Exists and opening a file can block on a network share. WaitAsync
		// lets a newer clipboard update proceed even if a probe ignores cancellation.
		var readTask = Task.Run(() => CreateObservationAsync(files, cancellationToken), CancellationToken.None);
		var observation = await readTask.WaitAsync(cancellationToken);
		cancellationToken.ThrowIfCancellationRequested();

		return observation;
	}

	private async Task<ClipboardObservation> CreateObservationAsync(
		ClipboardInspection.CopiedFiles files,
		CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		if (files.OriginalText is { } originalText)
		{
			foreach (var path in files.FilePaths)
			{
				cancellationToken.ThrowIfCancellationRequested();

				if (!fileExists(path))
				{
					return ClipboardObservation.TextValue(files.SequenceNumber, files.ObservedAt, originalText);
				}
			}
		}

		cancellationToken.ThrowIfCancellationRequested();

		if (files.FilePaths.Count is < 1 or > 2)
		{
			return ClipboardObservation.NonText(files.SequenceNumber, files.ObservedAt);
		}

		var values = await readValues(files.FilePaths, cancellationToken);
		cancellationToken.ThrowIfCancellationRequested();

		if (values.Count != files.FilePaths.Count)
		{
			return ClipboardObservation.NonText(files.SequenceNumber, files.ObservedAt);
		}

		return values.Count switch
		{
			1 => ClipboardObservation.TextValue(
				files.SequenceNumber,
				files.ObservedAt,
				values[0].Text,
				values[0].FileName,
				values[0].FilePath),
			2 => ClipboardObservation.TextPair(
				files.SequenceNumber,
				files.ObservedAt,
				values[0].Text,
				values[1].Text,
				values[0].FileName,
				values[1].FileName,
				values[0].FilePath,
				values[1].FilePath),
			_ => ClipboardObservation.NonText(files.SequenceNumber, files.ObservedAt)
		};
	}
}
