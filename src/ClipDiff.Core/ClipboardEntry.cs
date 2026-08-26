namespace ClipDiff;

public sealed record ClipboardEntry(
    Guid Id,
    string Text,
    DateTimeOffset CapturedAt,
    string? SourceFileName = null,
    string? SourceFilePath = null);
