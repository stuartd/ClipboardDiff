namespace ClipDiff;

public static class DiffFormatting
{
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
            "--- Previous clipboard",
            "+++ Current clipboard"
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

    private static void Add(List<string> parts, int count, string label)
    {
        if (count > 0)
        {
            parts.Add($"{count} {label} line{(count == 1 ? string.Empty : "s")}");
        }
    }
}
