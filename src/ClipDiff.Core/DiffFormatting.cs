namespace ClipDiff;

public static class DiffFormatting
{
    public const string DefaultPreviousLabel = "Previous clipboard";
    public const string DefaultCurrentLabel = "Current clipboard";

    public static string Summary(DiffSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        var parts = new List<string>(3);
        Add(parts, summary.Changed, "changed");
        Add(parts, summary.Inserted, "added");
        Add(parts, summary.Removed, "removed");
        return parts.Count == 0 ? "No differences" : string.Join(", ", parts);
    }

    public static string Unified(DiffDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var lines = new List<string>(document.Rows.Count * 2 + 2)
        {
            "--- " + PreviousLabel(document.Previous),
            "+++ " + CurrentLabel(document.Current)
        };

        foreach (var row in document.Rows)
        {
            switch (row.Kind)
            {
                case DiffKind.Equal:
                    lines.Add("  " + row.OldText);
                    break;
                case DiffKind.Removed:
                    lines.Add("- " + row.OldText);
                    break;
                case DiffKind.Inserted:
                    lines.Add("+ " + row.NewText);
                    break;
                case DiffKind.Changed:
                    lines.Add("- " + row.OldText);
                    lines.Add("+ " + row.NewText);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(document));
            }
        }

        return string.Join('\n', lines);
    }

    public static string PreviousLabel(ClipboardEntry entry) =>
        EntryLabel(DefaultPreviousLabel, entry);

    public static string CurrentLabel(ClipboardEntry entry) =>
        EntryLabel(DefaultCurrentLabel, entry);

    private static string EntryLabel(string defaultLabel, ClipboardEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return string.IsNullOrWhiteSpace(entry.SourceFileName)
            ? defaultLabel
            : $"{defaultLabel} — {entry.SourceFileName}";
    }

    private static void Add(List<string> parts, int count, string label)
    {
        if (count > 0)
        {
            parts.Add($"{count} {label} line{(count == 1 ? string.Empty : "s")}");
        }
    }
}
