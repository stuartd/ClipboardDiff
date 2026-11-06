using System.Text.RegularExpressions;

namespace ClipDiff.Windows.Updates;

internal sealed class ReleaseVersion : IComparable<ReleaseVersion>
{
	private static readonly Regex Pattern = new(
		@"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?\z",
		RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
	private readonly string[] numbers;
	private readonly string[] prerelease;

	private ReleaseVersion(string text, Match match)
	{
		Text = text;
		numbers = [match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value];
		prerelease = match.Groups[4].Success ? match.Groups[4].Value.Split('.') : [];
	}

	public string Text { get; }

	public static bool TryParse(string? text, out ReleaseVersion version)
	{
		version = null!;

		if (string.IsNullOrEmpty(text) || text.Length > 256)
		{
			return false;
		}

		text = text.StartsWith('v') ? text[1..] : text;
		var match = Pattern.Match(text);

		if (!match.Success || (match.Groups[4].Success && match.Groups[4].Value.Split('.')
			.Any(part => IsNumeric(part) && part.Length > 1 && part[0] == '0')))
		{
			return false;
		}

		version = new ReleaseVersion(text, match);
		return true;
	}

	public int CompareTo(ReleaseVersion? other)
	{
		if (other is null)
		{
			return 1;
		}

		for (var index = 0; index < numbers.Length; index++)
		{
			var comparison = CompareNumber(numbers[index], other.numbers[index]);

			if (comparison != 0)
			{
				return comparison;
			}
		}

		if (prerelease.Length == 0 || other.prerelease.Length == 0)
		{
			return (prerelease.Length == 0 ? 1 : 0).CompareTo(other.prerelease.Length == 0 ? 1 : 0);
		}

		for (var index = 0; index < Math.Min(prerelease.Length, other.prerelease.Length); index++)
		{
			var left = prerelease[index];
			var right = other.prerelease[index];
			var leftNumeric = IsNumeric(left);
			var rightNumeric = IsNumeric(right);
			var comparison = leftNumeric && rightNumeric
				? CompareNumber(left, right)
				: leftNumeric != rightNumeric ? (leftNumeric ? -1 : 1) : string.CompareOrdinal(left, right);

			if (comparison != 0)
			{
				return comparison;
			}
		}

		return prerelease.Length.CompareTo(other.prerelease.Length);
	}

	private static bool IsNumeric(string text) => text.All(char.IsAsciiDigit);

	private static int CompareNumber(string left, string right) =>
		left.Length != right.Length ? left.Length.CompareTo(right.Length) : string.CompareOrdinal(left, right);
}
