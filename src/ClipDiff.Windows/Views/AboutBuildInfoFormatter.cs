using System.Globalization;

namespace ClipDiff.Windows.Views;

internal static class AboutBuildInfoFormatter
{
	private const int ShortCommitLength = 7;

	public static string Format(string? informationalVersion, string? buildDateUtc)
	{
		var parts = informationalVersion?.Split('+', 2);
		var commit = parts is { Length: 2 }
			? parts[1].Split('.').LastOrDefault(IsCommitHash)
			: null;
		var shortCommit = commit is null ? "Unknown" : commit[..ShortCommitLength];
		var buildDate = DateTimeOffset.TryParse(buildDateUtc, CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal, out var timestamp)
			? timestamp.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture)
			: "Unknown";

		return $"Last commit: {shortCommit}\nBuild date: {buildDate}";
	}

	private static bool IsCommitHash(string value) =>
		value.Length >= ShortCommitLength && value.All(IsHexDigit);

	private static bool IsHexDigit(char value) =>
		value is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
}
