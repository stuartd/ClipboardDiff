using System.Buffers.Binary;
using System.Text;

namespace ClipDiff.Stress;

internal sealed record StressItem(uint Format, byte[] Bytes);

internal static class StressWorkload
{
	internal const uint UnicodeText = 13;
	internal const uint FileDrop = 15;

	public static string CreateText(int sizeKiB, char variant, bool singleLine)
	{
		var size = checked(sizeKiB * 1024);
		var text = new StringBuilder(size);
		var line = $"ClipDiff synthetic {variant}: " + new string(variant, 100) + (singleLine ? " " : "\r\n");

		while (text.Length < size)
		{
			text.Append(line.AsSpan(0, Math.Min(line.Length, size - text.Length)));
		}

		return text.ToString();
	}

	public static StressItem Text(string value) => new(UnicodeText, Encoding.Unicode.GetBytes(value + '\0'));

	public static StressItem Files(params string[] paths)
	{
		// DROPFILES: 20-byte header followed by a double-null-terminated UTF-16 list.
		var names = Encoding.Unicode.GetBytes(string.Join('\0', paths) + "\0\0");
		var bytes = new byte[20 + names.Length];
		BinaryPrimitives.WriteInt32LittleEndian(bytes, 20);
		BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), 1);
		names.CopyTo(bytes, 20);
		return new(FileDrop, bytes);
	}

	public static StressItem[] Create(StressMode mode, string previous, string current,
		string previousPath, string currentPath)
	{
		return mode switch
		{
			StressMode.Text => [Text(previous), Text(previous), Text(current)],
			StressMode.Files => [Files(previousPath), Files(currentPath),
				Files(previousPath, currentPath), Files(currentPath, previousPath)],
			StressMode.Paths => [Text(previousPath), Text(currentPath),
				Text($"\"{previousPath}\"\r\n\"{currentPath}\""), Text($"{currentPath}\n{previousPath}\n")],
			StressMode.Mixed => [.. Create(StressMode.Text, previous, current, previousPath, currentPath),
				.. Create(StressMode.Files, previous, current, previousPath, currentPath),
				.. Create(StressMode.Paths, previous, current, previousPath, currentPath)],
			_ => throw new ArgumentOutOfRangeException(nameof(mode))
		};
	}
}
