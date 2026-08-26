namespace ClipDiff;

public enum ClipboardObservationKind
{
    Text,
    TextPair,
    ExplicitClear,
    NonText,
    Sensitive,
    InspectionFailed,
    OwnWrite
}

public sealed record ClipboardObservation(
    ClipboardObservationKind Kind,
    uint SequenceNumber,
    DateTimeOffset ObservedAt,
    string? Text = null,
    string? PreviousText = null,
    string? SourceFileName = null,
    string? PreviousSourceFileName = null,
    string? SourceFilePath = null,
    string? PreviousSourceFilePath = null)
{
    public static ClipboardObservation TextValue(
        uint sequenceNumber,
        DateTimeOffset observedAt,
        string text,
        string? sourceFileName = null,
        string? sourceFilePath = null) =>
        new(
            ClipboardObservationKind.Text,
            sequenceNumber,
            observedAt,
            text,
            SourceFileName: sourceFileName,
            SourceFilePath: sourceFilePath);

    public static ClipboardObservation TextPair(
        uint sequenceNumber,
        DateTimeOffset observedAt,
        string previousText,
        string currentText,
        string? previousSourceFileName = null,
        string? currentSourceFileName = null,
        string? previousSourceFilePath = null,
        string? currentSourceFilePath = null) =>
        new(
            ClipboardObservationKind.TextPair,
            sequenceNumber,
            observedAt,
            Text: currentText,
            PreviousText: previousText,
            SourceFileName: currentSourceFileName,
            PreviousSourceFileName: previousSourceFileName,
            SourceFilePath: currentSourceFilePath,
            PreviousSourceFilePath: previousSourceFilePath);

    public static ClipboardObservation ExplicitClear(uint sequenceNumber, DateTimeOffset observedAt) =>
        new(ClipboardObservationKind.ExplicitClear, sequenceNumber, observedAt);

    public static ClipboardObservation NonText(uint sequenceNumber, DateTimeOffset observedAt) =>
        new(ClipboardObservationKind.NonText, sequenceNumber, observedAt);

    public static ClipboardObservation Sensitive(uint sequenceNumber, DateTimeOffset observedAt) =>
        new(ClipboardObservationKind.Sensitive, sequenceNumber, observedAt);

    public static ClipboardObservation InspectionFailed(uint sequenceNumber, DateTimeOffset observedAt) =>
        new(ClipboardObservationKind.InspectionFailed, sequenceNumber, observedAt);

    public static ClipboardObservation OwnWrite(uint sequenceNumber, DateTimeOffset observedAt) =>
        new(ClipboardObservationKind.OwnWrite, sequenceNumber, observedAt);
}

public enum ClipboardHistoryChange
{
    None,
    Accepted,
    RemovedByRecentClear
}
