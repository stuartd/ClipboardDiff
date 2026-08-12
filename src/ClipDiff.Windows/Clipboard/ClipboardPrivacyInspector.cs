namespace ClipDiff.Windows.Clipboard;

public sealed record ClipboardFormatIds(uint ExcludeFromMonitor, uint IncludeInHistory, uint UploadToCloud);

public interface IClipboardDataAccess
{
    bool TryHasAnyFormats(out bool hasAnyFormats);

    bool IsFormatAvailable(uint format);

    bool TryReadDword(uint format, out uint value);

    bool TryReadUnicodeText(out string? text);
}

public sealed class ClipboardPrivacyInspector
{
    private readonly IClipboardDataAccess _clipboard;
    private readonly ClipboardFormatIds _formats;

    public ClipboardPrivacyInspector(IClipboardDataAccess clipboard, ClipboardFormatIds formats)
    {
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        _formats = formats ?? throw new ArgumentNullException(nameof(formats));
    }

    public ClipboardObservation Inspect(uint sequenceNumber, DateTimeOffset observedAt)
    {
        if (!_clipboard.TryHasAnyFormats(out var hasAnyFormats))
        {
            return ClipboardObservation.InspectionFailed(sequenceNumber, observedAt);
        }

        if (!hasAnyFormats)
        {
            return ClipboardObservation.ExplicitClear(sequenceNumber, observedAt);
        }

        if (_clipboard.IsFormatAvailable(_formats.ExcludeFromMonitor))
        {
            return ClipboardObservation.Sensitive(sequenceNumber, observedAt);
        }

        if (IsUnavailableOrZero(_formats.IncludeInHistory) || IsUnavailableOrZero(_formats.UploadToCloud))
        {
            return ClipboardObservation.Sensitive(sequenceNumber, observedAt);
        }

        if (!_clipboard.IsFormatAvailable(NativeUnicodeTextFormat))
        {
            return ClipboardObservation.NonText(sequenceNumber, observedAt);
        }

        if (!_clipboard.TryReadUnicodeText(out var text) || text is null)
        {
            return ClipboardObservation.InspectionFailed(sequenceNumber, observedAt);
        }

        return text.Length == 0
            ? ClipboardObservation.ExplicitClear(sequenceNumber, observedAt)
            : ClipboardObservation.TextValue(sequenceNumber, observedAt, text);
    }

    public const uint NativeUnicodeTextFormat = 13;

    private bool IsUnavailableOrZero(uint format)
    {
        if (!_clipboard.IsFormatAvailable(format))
        {
            return false;
        }

        // A malformed or unreadable marker is excluded conservatively.
        return !_clipboard.TryReadDword(format, out var value) || value == 0;
    }
}
