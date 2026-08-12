namespace ClipDiff;

public enum ClipboardObservationKind
{
    Text,
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
    string? Text = null)
{
    public static ClipboardObservation TextValue(uint sequenceNumber, DateTimeOffset observedAt, string text) =>
        new(ClipboardObservationKind.Text, sequenceNumber, observedAt, text);

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
