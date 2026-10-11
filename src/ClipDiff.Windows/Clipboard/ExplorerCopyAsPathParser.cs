namespace ClipDiff.Windows.Clipboard;

internal static class ExplorerCopyAsPathParser
{
	public static bool TryParse(
		string text,
		out IReadOnlyList<string> filePaths,
		out bool requiresExistingFiles)
	{
		ArgumentNullException.ThrowIfNull(text);
		filePaths = [];
		requiresExistingFiles = false;

		var lines = text
			.Replace("\r\n", "\n", StringComparison.Ordinal)
			.Replace('\r', '\n')
			.Split('\n');
		var lineCount = lines.Length;

		while (lineCount > 0 && lines[lineCount - 1].Length == 0)
		{
			lineCount--;
		}

		var paths = new List<string>(lineCount);
		var containsUnquotedPath = false;

		for (var index = 0; index < lineCount; index++)
		{
			var line = lines[index];
			var quoted = line.Length >= 2 && line[0] == '"' && line[^1] == '"';
			var path = quoted ? line[1..^1] : line;

			if (!IsAbsoluteWindowsPath(path) || !HasValidPathCharacters(path) || HasLineColumnSuffix(path))
			{
				return false;
			}

			paths.Add(path);
			containsUnquotedPath |= !quoted;
		}

		filePaths = paths;
		requiresExistingFiles = containsUnquotedPath;
		return lineCount > 0;
	}

	private static bool HasValidPathCharacters(string path)
	{
		for (var index = 0; index < path.Length; index++)
		{
			var character = path[index];

			if (character < ' ' || character is '"' or '<' or '>' or '|' or '*' or '?' ||
				(character == ':' && index != 1))
			{
				return false;
			}
		}

		// Do not interpret environment-variable expressions, even if a literal
		// filename with those characters happens to exist.
		var percent = path.IndexOf('%');
		return percent < 0 || path.IndexOf('%', percent + 1) < 0;
	}

	private static bool HasLineColumnSuffix(string path)
	{
		if (!path.EndsWith(')'))
		{
			return false;
		}

		var opening = path.LastIndexOf('(');

		if (opening < 0)
		{
			return false;
		}

		var suffix = path.AsSpan(opening + 1, path.Length - opening - 2);
		var comma = suffix.IndexOf(',');

		return comma < 0
			? IsLineNumber(suffix)
			: IsLineNumber(suffix[..comma]) && IsLineNumber(suffix[(comma + 1)..]);
	}

	private static bool IsLineNumber(ReadOnlySpan<char> value)
	{
		foreach (var character in value)
		{
			if (character is < '0' or > '9')
			{
				return false;
			}
		}

		return !value.IsEmpty;
	}

	private static bool IsAbsoluteWindowsPath(string path)
	{
		if (path.Length >= 3 &&
			IsAsciiLetter(path[0]) &&
			path[1] == ':' &&
			IsDirectorySeparator(path[2]))
		{
			return true;
		}

		if (path.Length < 5 ||
			!IsDirectorySeparator(path[0]) ||
			!IsDirectorySeparator(path[1]) ||
			IsDirectorySeparator(path[2]))
		{
			return false;
		}

		for (var index = 3; index < path.Length - 1; index++)
		{
			if (IsDirectorySeparator(path[index]) && !IsDirectorySeparator(path[index + 1]))
			{
				return true;
			}
		}

		return false;
	}

	private static bool IsAsciiLetter(char value) =>
		value is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

	private static bool IsDirectorySeparator(char value) => value is '\\' or '/';
}
