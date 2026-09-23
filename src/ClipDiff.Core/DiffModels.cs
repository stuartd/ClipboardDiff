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
    DiffKind Kind)
{
    public IReadOnlyList<HighlightRange> OldHighlights { get; init; } = [];
    public IReadOnlyList<HighlightRange> NewHighlights { get; init; } = [];
}

/// <summary>Zero-based extended grapheme cluster offsets; End is exclusive.</summary>
public readonly record struct HighlightRange(int Start, int End);

public sealed record DiffSummary(
    int Inserted,
    int Removed,
    int Changed,
    int Unchanged);

public sealed record DiffSideLabels(string Previous, string Current);

public sealed record DiffDocument(
    Guid Id,
    ClipboardEntry Previous,
    ClipboardEntry Current,
    IReadOnlyList<DiffRow> Rows,
    DiffSummary Summary,
    DateTimeOffset CreatedAt,
    DiffSideLabels Labels);

public enum DiffViewMode
{
    SideBySide,
    Unified
}
