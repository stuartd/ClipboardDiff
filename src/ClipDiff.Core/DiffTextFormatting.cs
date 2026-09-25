using System.Globalization;

namespace ClipDiff;

public readonly record struct DiffTextSlice(string Text, bool Highlighted);

public static class DiffTextFormatting
{
    // Convert grapheme offsets to UTF-16 before expanding tabs. Each slice becomes a Run
    // in the same flowing paragraph, never a separate layout element.
    public static IReadOnlyList<DiffTextSlice> Slices(string? source, IReadOnlyList<HighlightRange> highlights)
    {
        source ??= string.Empty;
        if (highlights.Count == 0)
		{
			return [new(source.Replace("\t", "    ", StringComparison.Ordinal), false)];
		}

		var starts = StringInfo.ParseCombiningCharacters(source);
        var slices = new List<DiffTextSlice>();
        var position = 0;
        foreach (var range in highlights)
        {
            var start = starts[range.Start];
            var end = range.End == starts.Length ? source.Length : starts[range.End];
            Add(position, start, false);
            Add(start, end, true);
            position = end;
        }
        Add(position, source.Length, false);
        return slices;

        void Add(int start, int end, bool highlighted)
        {
            if (end > start)
			{
				slices.Add(new(source[start..end].Replace("\t", "    ", StringComparison.Ordinal), highlighted));
			}
		}
    }
}
