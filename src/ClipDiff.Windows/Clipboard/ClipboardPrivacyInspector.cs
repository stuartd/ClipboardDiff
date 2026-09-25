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
        IReadOnlyList<string> FilePaths) : ClipboardInspection;
}

public sealed class ClipboardPrivacyInspector
{
    private readonly IClipboardDataAccess clipboard;
    private readonly ClipboardFormatIds formats;

    public ClipboardPrivacyInspector(IClipboardDataAccess clipboard, ClipboardFormatIds formats)
    {
        this.clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        this.formats = formats ?? throw new ArgumentNullException(nameof(formats));
    }

    public ClipboardInspection Inspect(uint sequenceNumber, DateTimeOffset observedAt)
    {
        if (!clipboard.TryHasAnyFormats(out var hasAnyFormats))
        {
            return Completed(ClipboardObservation.InspectionFailed(sequenceNumber, observedAt));
        }

        if (!hasAnyFormats)
        {
            return Completed(ClipboardObservation.ExplicitClear(sequenceNumber, observedAt));
        }

        if (clipboard.IsFormatAvailable(formats.ExcludeFromMonitor))
        {
            return Completed(ClipboardObservation.Sensitive(sequenceNumber, observedAt));
        }

        if (IsUnavailableOrZero(formats.IncludeInHistory) || IsUnavailableOrZero(formats.UploadToCloud))
        {
            return Completed(ClipboardObservation.Sensitive(sequenceNumber, observedAt));
        }

        if (clipboard.IsFormatAvailable(NativeFileDropFormat))
        {
            if (!clipboard.TryReadFilePaths(out var filePaths) || filePaths is null)
            {
                return Completed(ClipboardObservation.InspectionFailed(sequenceNumber, observedAt));
            }

            return filePaths.Count == 0
                ? Completed(ClipboardObservation.NonText(sequenceNumber, observedAt))
                : new ClipboardInspection.CopiedFiles(sequenceNumber, observedAt, filePaths);
        }

        if (!clipboard.IsFormatAvailable(NativeUnicodeTextFormat))
        {
            return Completed(ClipboardObservation.NonText(sequenceNumber, observedAt));
        }

        if (!clipboard.TryReadUnicodeText(out var text) || text is null)
        {
            return Completed(ClipboardObservation.InspectionFailed(sequenceNumber, observedAt));
        }

        if (text.Length == 0)
        {
            return Completed(ClipboardObservation.ExplicitClear(sequenceNumber, observedAt));
        }

        return ExplorerCopyAsPathParser.TryParse(text, out var copyAsPathFiles)
            ? new ClipboardInspection.CopiedFiles(sequenceNumber, observedAt, copyAsPathFiles)
            : Completed(ClipboardObservation.TextValue(sequenceNumber, observedAt, text));
    }

    public const uint NativeUnicodeTextFormat = 13;
    public const uint NativeFileDropFormat = 15;

    private static ClipboardInspection.Completed Completed(ClipboardObservation observation) => new(observation);

    private bool IsUnavailableOrZero(uint format)
    {
        if (!clipboard.IsFormatAvailable(format))
        {
            return false;
        }

        // A malformed or unreadable marker is excluded conservatively.
        return !clipboard.TryReadDword(format, out var value) || value == 0;
    }
}
