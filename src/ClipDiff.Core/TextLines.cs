using System.Globalization;
using System.Text;

namespace ClipDiff;

public static class TextLines
{
    public const int PreviewCharacterLimit = 120;

    public static string[] Split(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
    }

    public static string Preview(string text, int characterLimit = PreviewCharacterLimit)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegative(characterLimit);

        var flattened = text.Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace('\t', ' ')
            .Trim();

        if (flattened.Length == 0)
        {
            return "Blank text";
        }

        var elements = StringInfo.GetTextElementEnumerator(flattened);
        var builder = new StringBuilder();
        var count = 0;
        while (elements.MoveNext())
        {
            if (count == characterLimit)
            {
                return builder.Append("...").ToString();
            }

            builder.Append(elements.GetTextElement());
            count++;
        }

        return builder.ToString();
    }

    public static string EntryPreview(ClipboardEntry entry, int characterLimit = PreviewCharacterLimit)
        => EntryPreview(entry, entry.SourceFileName, characterLimit);

    public static string EntryPreview(
        ClipboardEntry entry,
        string? fileLabel,
        int characterLimit = PreviewCharacterLimit)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var preview = Preview(entry.Text, characterLimit);
        return string.IsNullOrWhiteSpace(fileLabel)
            ? preview
            : $"{fileLabel} — {preview}";
    }
}
