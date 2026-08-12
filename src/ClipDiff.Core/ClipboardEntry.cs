namespace ClipDiff;

public sealed record ClipboardEntry(
    Guid Id,
    string Text,
    DateTimeOffset CapturedAt);
