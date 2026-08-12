namespace ClipDiff;

public enum DiffKind
{
    Equal,
    Inserted,
    Removed,
    Changed
}

public sealed record DiffRow(
    Guid Id,
    int? OldLineNumber,
    int? NewLineNumber,
    string? OldText,
    string? NewText,
    DiffKind Kind);

public sealed record DiffSummary(
    int Inserted,
    int Removed,
    int Changed,
    int Unchanged);

public sealed record DiffDocument(
    Guid Id,
    ClipboardEntry Previous,
    ClipboardEntry Current,
    IReadOnlyList<DiffRow> Rows,
    DiffSummary Summary,
    DateTimeOffset CreatedAt);

public enum DiffViewMode
{
    SideBySide,
    Unified
}
