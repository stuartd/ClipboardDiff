using System.Globalization;
using System.Text;

namespace ClipDiff;

// One instance per comparison: the budget is shared by changed rows in document order.
internal sealed class InlineDiff(bool ignoreSpacing, CancellationToken cancellationToken)
{
	private const int MaximumPairBytes = 16_384;
	private const int MaximumSharedWork = 2_000_000;
	private const int MaximumSearchCells = 65_536;
	private int remainingWork = MaximumSharedWork;

	internal DiffRow Highlight(DiffRow row)
	{
		cancellationToken.ThrowIfCancellationRequested();

		if (row.Kind != DiffKind.Changed || row.OldText is null || row.NewText is null)
		{
			return row;
		}

		// UTF-16 length is a cheap lower bound that also avoids scanning oversized lines.
		if ((long)row.OldText.Length + row.NewText.Length > MaximumPairBytes)
		{
			return row;
		}

		int bytes = Encoding.UTF8.GetByteCount(row.OldText) + Encoding.UTF8.GetByteCount(row.NewText);

		if (bytes > MaximumPairBytes || bytes > remainingWork)
		{
			return row;
		}

		remainingWork -= bytes;

		int searchBudget = Math.Min(MaximumSearchCells, remainingWork);
		int initialBudget = searchBudget;
		var oldUnits = Units(row.OldText, ignoreSpacing);
		var newUnits = Units(row.NewText, ignoreSpacing);
		var oldTokens = Tokens(oldUnits);
		var newTokens = Tokens(newUnits);
		var tokenMatches = Align(oldTokens, newTokens, ref searchBudget);
		var oldMatched = new bool[oldUnits.Count];
		var newMatched = new bool[newUnits.Count];
		var oldStart = 0;
		var newStart = 0;

		foreach ((int oldIndex, int newIndex) in tokenMatches.Append((oldTokens.Count, newTokens.Count)))
		{
			cancellationToken.ThrowIfCancellationRequested();

			int oldEnd = oldIndex == oldTokens.Count ? oldUnits.Count : oldTokens[oldIndex].Start;
			int newEnd = newIndex == newTokens.Count ? newUnits.Count : newTokens[newIndex].Start;
			var matches = Align(oldUnits.GetRange(oldStart, oldEnd - oldStart),
				newUnits.GetRange(newStart, newEnd - newStart), ref searchBudget, false);

			foreach ((int oldUnit, int newUnit) in matches)
			{
				oldMatched[oldStart + oldUnit] = true;
				newMatched[newStart + newUnit] = true;
			}

			if (oldIndex == oldTokens.Count)
			{
				break;
			}

			var oldToken = oldTokens[oldIndex];
			var newToken = newTokens[newIndex];
			Array.Fill(oldMatched, true, oldToken.Start, oldToken.End - oldToken.Start);
			Array.Fill(newMatched, true, newToken.Start, newToken.End - newToken.Start);
			oldStart = oldToken.End;
			newStart = newToken.End;
		}

		remainingWork -= initialBudget - searchBudget;

		return row with
		{
			OldHighlights = ChangedRanges(oldUnits, oldMatched),
			NewHighlights = ChangedRanges(newUnits, newMatched),
		};
	}

	internal static string NormalizeSpacing(string text)
	{
		return string.Concat(Units(text, true).Select(unit => unit.Text));
	}

	private static List<Unit> Units(string text, bool normalize)
	{
		int[] boundaries = StringInfo.ParseCombiningCharacters(text);
		var units = new List<Unit>(boundaries.Length);

		for (var index = 0; index < boundaries.Length; index++)
		{
			int end = index + 1 < boundaries.Length ? boundaries[index + 1] : text.Length;
			string value = text[boundaries[index]..end];

			if (normalize && IsWhitespace(value))
			{
				if (units.Count > 0)
				{
					if (units[^1].Text == " ")
					{
						units[^1] = units[^1] with { End = index + 1 };
					}
					else
					{
						units.Add(new(" ", index, index + 1, 1));
					}
				}
			}
			else
			{
				units.Add(new(value, index, index + 1, 1));
			}
		}

		if (normalize && units.Count > 0 && units[^1].Text == " ")
		{
			units.RemoveAt(units.Count - 1);
		}

		return units;
	}

	private static bool IsWhitespace(string text)
	{
		return text.All(char.IsWhiteSpace);
	}

	private static int Category(string text)
	{
		return IsWhitespace(text) ? 0 :
			text[0] == '_' || (Rune.TryGetRuneAt(text, 0, out var rune) && (Rune.IsLetter(rune) || Rune.IsNumber(rune))) ? 1 : 2;
	}

	private static List<Unit> Tokens(List<Unit> units)
	{
		var tokens = new List<Unit>();
		for (var start = 0; start < units.Count;)
		{
			int category = Category(units[start].Text);
			int end = start + 1;
			while (category != 2 && end < units.Count && Category(units[end].Text) == category)
			{
				end++;
			}

			tokens.Add(new(string.Concat(units.GetRange(start, end - start).Select(unit => unit.Text)),
				start, end, category == 0 ? 1 : Math.Max(2, end - start)));
			start = end;
		}

		return tokens;
	}

	private List<(int Old, int New)> Align(List<Unit> old, List<Unit> current, ref int budget, bool weighted = true)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var matches = new List<(int, int)>();
		var prefix = 0;
		while (prefix < old.Count && prefix < current.Count && old[prefix].Text == current[prefix].Text)
		{
			matches.Add((prefix, prefix));
			prefix++;
		}

		int oldEnd = old.Count;
		int newEnd = current.Count;
		while (oldEnd > prefix && newEnd > prefix && old[oldEnd - 1].Text == current[newEnd - 1].Text)
		{
			oldEnd--;
			newEnd--;
		}

		int n = oldEnd - prefix;
		int m = newEnd - prefix;
		long cells = (long)(n + 1) * (m + 1);
		if (n > 0 && m > 0 && cells <= budget)
		{
			budget -= (int)cells;
			var scores = new int[(int)cells];
			int width = m + 1;
			for (int i = n - 1; i >= 0; i--)
			{
				cancellationToken.ThrowIfCancellationRequested();
				for (int j = m - 1; j >= 0; j--)
				{
					scores[i * width + j] = old[prefix + i].Text == current[prefix + j].Text
						? scores[(i + 1) * width + j + 1] + (weighted ? old[prefix + i].Weight : 1)
						: Math.Max(scores[(i + 1) * width + j], scores[i * width + j + 1]);
				}
			}

			var x = 0;
			var y = 0;
			while (x < n && y < m)
			{
				if (old[prefix + x].Text == current[prefix + y].Text)
				{
					matches.Add((prefix + x++, prefix + y++));
				}
				else if (scores[(x + 1) * width + y] >= scores[x * width + y + 1])
				{
					x++; // Equal reconstruction scores always advance the original side first.
				}
				else
				{
					y++;
				}
			}
		}

		while (oldEnd < old.Count)
		{
			matches.Add((oldEnd++, newEnd++));
		}

		return matches;
	}

	private static IReadOnlyList<HighlightRange> ChangedRanges(List<Unit> units, bool[] matched)
	{
		var ranges = new List<HighlightRange>();
		for (var index = 0; index < units.Count; index++)
		{
			if (matched[index])
			{
				continue;
			}

			var unit = units[index];
			if (ranges.Count > 0 && ranges[^1].End == unit.Start)
			{
				ranges[^1] = ranges[^1] with { End = unit.End };
			}
			else
			{
				ranges.Add(new(unit.Start, unit.End));
			}
		}

		return ranges.ToArray();
	}

	private sealed record Unit(string Text, int Start, int End, int Weight);
}