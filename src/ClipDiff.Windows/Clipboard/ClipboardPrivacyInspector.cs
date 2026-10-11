namespace ClipDiff.Windows.Clipboard;

public sealed record ClipboardFormatIds(uint ExcludeFromMonitor, uint IncludeInHistory, uint UploadToCloud);

public interface IClipboardDataAccess
{
	bool TryHasAnyFormats(out bool hasAnyFormats);

	bool IsFormatAvailable(uint format);

	bool TryReadDword(uint format, out uint value);

	bool TryReadUnicodeText(out string? text);

	bool TryReadFilePaths(out IReadOnlyList<string>? filePaths);
}

public abstract record ClipboardInspection
{
	private ClipboardInspection()
	{
	}

	public sealed record Completed(ClipboardObservation Observation) : ClipboardInspection;

	public sealed record CopiedFiles(
		uint SequenceNumber,
		DateTimeOffset ObservedAt,
		IReadOnlyList<string> FilePaths,
		string? OriginalText = null) : ClipboardInspection;
}

public sealed class ClipboardPrivacyInspector
{
	private readonly IClipboardDataAccess clipboardDataAccess;
	private readonly ClipboardFormatIds clipboardFormatIds;

	public ClipboardPrivacyInspector(IClipboardDataAccess dataAccess, ClipboardFormatIds formatIds)
	{
		clipboardDataAccess = dataAccess ?? throw new ArgumentNullException(nameof(dataAccess));
		clipboardFormatIds = formatIds ?? throw new ArgumentNullException(nameof(formatIds));
	}

	public ClipboardInspection Inspect(uint sequenceNumber, DateTimeOffset observedAt)
	{
		if (!clipboardDataAccess.TryHasAnyFormats(out var hasAnyFormats))
		{
			return Completed(ClipboardObservation.InspectionFailed(sequenceNumber, observedAt));
		}

		if (!hasAnyFormats)
		{
			return Completed(ClipboardObservation.ExplicitClear(sequenceNumber, observedAt));
		}

		if (clipboardDataAccess.IsFormatAvailable(clipboardFormatIds.ExcludeFromMonitor))
		{
			return Completed(ClipboardObservation.Sensitive(sequenceNumber, observedAt));
		}

		if (IsUnavailableOrZero(clipboardFormatIds.IncludeInHistory) || IsUnavailableOrZero(clipboardFormatIds.UploadToCloud))
		{
			return Completed(ClipboardObservation.Sensitive(sequenceNumber, observedAt));
		}

		if (clipboardDataAccess.IsFormatAvailable(NativeFileDropFormat))
		{
			if (!clipboardDataAccess.TryReadFilePaths(out var filePaths) || filePaths is null)
			{
				return Completed(ClipboardObservation.InspectionFailed(sequenceNumber, observedAt));
			}

			return filePaths.Count == 0
				? Completed(ClipboardObservation.NonText(sequenceNumber, observedAt))
				: new ClipboardInspection.CopiedFiles(sequenceNumber, observedAt, filePaths);
		}

		if (!clipboardDataAccess.IsFormatAvailable(NativeUnicodeTextFormat))
		{
			return Completed(ClipboardObservation.NonText(sequenceNumber, observedAt));
		}

		if (!clipboardDataAccess.TryReadUnicodeText(out var text) || text is null)
		{
			return Completed(ClipboardObservation.InspectionFailed(sequenceNumber, observedAt));
		}

		if (text.Length == 0)
		{
			return Completed(ClipboardObservation.ExplicitClear(sequenceNumber, observedAt));
		}

		// Parsing only checks syntax. File probes happen after the clipboard
		// is closed, on the same worker as file conversion.
		return ExplorerCopyAsPathParser.TryParse(text, out var copyAsPathFiles, out var requiresExistingFiles)
			? new ClipboardInspection.CopiedFiles(
				sequenceNumber, observedAt, copyAsPathFiles, requiresExistingFiles ? text : null)
			: Completed(ClipboardObservation.TextValue(sequenceNumber, observedAt, text));
	}

	public const uint NativeUnicodeTextFormat = 13;
	public const uint NativeFileDropFormat = 15;

	private static ClipboardInspection.Completed Completed(ClipboardObservation observation) => new(observation);

	private bool IsUnavailableOrZero(uint format)
	{
		if (!clipboardDataAccess.IsFormatAvailable(format))
		{
			return false;
		}

		// A malformed or unreadable marker is excluded conservatively.
		return !clipboardDataAccess.TryReadDword(format, out var value) || value == 0;
	}
}
