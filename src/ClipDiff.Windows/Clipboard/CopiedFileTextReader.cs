using System.IO;
using System.Security;
using System.Text;

namespace ClipDiff.Windows.Clipboard;

public sealed record CopiedFileText(string Text, string FileName);

public sealed class CopiedFileTextReader
{
    public const long DefaultMaximumTextFileBytes = 16 * 1024 * 1024;

    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);
    private static readonly Encoding StrictUtf16LittleEndian = new UnicodeEncoding(false, false, true);
    private static readonly Encoding StrictUtf16BigEndian = new UnicodeEncoding(true, false, true);
    private static readonly Encoding StrictUtf32LittleEndian = new UTF32Encoding(false, false, true);
    private static readonly Encoding StrictUtf32BigEndian = new UTF32Encoding(true, false, true);
    private static readonly Encoding Windows1252 = CreateWindows1252Encoding();
    private static readonly HashSet<string> KnownBinaryExecutableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".appx", ".appxbundle", ".class", ".com", ".cpl", ".dll", ".drv", ".efi", ".exe", ".jar",
        ".msi", ".msix", ".msixbundle", ".msp", ".mst", ".mui", ".ocx", ".scr", ".sys"
    };
    private readonly long _maximumTextFileBytes;

    public CopiedFileTextReader(long maximumTextFileBytes = DefaultMaximumTextFileBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumTextFileBytes);
        _maximumTextFileBytes = maximumTextFileBytes;
    }

    public async ValueTask<IReadOnlyList<CopiedFileText>> ReadValuesAsync(
        IReadOnlyList<string> filePaths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filePaths);

        if (filePaths.Count > 2)
        {
            return [];
        }

        if (filePaths.Count == 2)
        {
            var previous = await ReadFileAsync(filePaths[0], cancellationToken);
            var current = await ReadFileAsync(filePaths[1], cancellationToken);
            return previous is null || current is null
                ? []
                : [previous, current];
        }

        if (filePaths.Count == 1)
        {
            var value = await ReadFileAsync(filePaths[0], cancellationToken);
            return value is null ? [] : [value];
        }

        return [];
    }

    public async ValueTask<string?> ReadAsync(
        IReadOnlyList<string> filePaths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filePaths);

        if (filePaths.Count != 1)
        {
            var names = filePaths
                .Select(GetDisplayName)
                .Where(name => name.Length > 0)
                .ToArray();
            return names.Length == 0 ? null : string.Join('\n', names);
        }

        return (await ReadFileAsync(filePaths[0], cancellationToken))?.Text;
    }

    public async ValueTask<CopiedFileText?> ReadFileAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        var fileName = GetDisplayName(filePath);
        if (fileName.Length == 0)
        {
            return null;
        }

        try
        {
            if (KnownBinaryExecutableExtensions.Contains(Path.GetExtension(filePath)))
            {
                return CreateFallback(fileName, "binary file");
            }

            if (Directory.Exists(filePath))
            {
                return CreateFallback(fileName, "directory");
            }

            await using var stream = new FileStream(filePath, new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.ReadWrite | FileShare.Delete,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan
            });

            if (stream.Length == 0)
            {
                return CreateFallback(fileName, "empty file");
            }

            if (stream.Length > _maximumTextFileBytes || stream.Length > int.MaxValue)
            {
                return CreateFallback(fileName, "file too large");
            }

            var bytes = new byte[checked((int)stream.Length)];
            await stream.ReadExactlyAsync(bytes, cancellationToken);
            if (!TryDecodeText(bytes, out var text))
            {
                return CreateFallback(fileName, "binary file");
            }

            return text.Length > 0
                ? new CopiedFileText(text, fileName)
                : CreateFallback(fileName, "empty file");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return CreateFallback(fileName, "file not found");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                          ArgumentException or NotSupportedException or SecurityException)
        {
            return CreateFallback(fileName, "file unreadable");
        }
    }

    private static CopiedFileText CreateFallback(string fileName, string reason) =>
        new($"{fileName} ({reason})", fileName);

    private static string GetDisplayName(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            var trimmed = Path.TrimEndingDirectorySeparator(path);
            var name = Path.GetFileName(trimmed);
            return name.Length > 0 ? name : trimmed;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return string.Empty;
        }
    }

    private static bool TryDecodeText(byte[] bytes, out string text)
    {
        if (TryDecodeBom(bytes, out text))
        {
            return IsTextLike(text);
        }

        if (TryDetectBomlessUtf16(bytes, out var utf16Encoding))
        {
            return TryDecode(utf16Encoding, bytes, 0, out text) && IsTextLike(text);
        }

        if (LooksBinary(bytes))
        {
            text = string.Empty;
            return false;
        }

        if (TryDecode(StrictUtf8, bytes, 0, out text) && IsTextLike(text))
        {
            return true;
        }

        return TryDecode(Windows1252, bytes, 0, out text) && IsTextLike(text);
    }

    private static bool TryDecodeBom(byte[] bytes, out string text)
    {
        if (bytes.AsSpan().StartsWith(new byte[] { 0x00, 0x00, 0xFE, 0xFF }))
        {
            return TryDecode(StrictUtf32BigEndian, bytes, 4, out text);
        }

        if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE, 0x00, 0x00 }))
        {
            return TryDecode(StrictUtf32LittleEndian, bytes, 4, out text);
        }

        if (bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }))
        {
            return TryDecode(StrictUtf8, bytes, 3, out text);
        }

        if (bytes.AsSpan().StartsWith(new byte[] { 0xFE, 0xFF }))
        {
            return TryDecode(StrictUtf16BigEndian, bytes, 2, out text);
        }

        if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE }))
        {
            return TryDecode(StrictUtf16LittleEndian, bytes, 2, out text);
        }

        text = string.Empty;
        return false;
    }

    private static bool TryDetectBomlessUtf16(byte[] bytes, out Encoding encoding)
    {
        encoding = StrictUtf16LittleEndian;
        if (bytes.Length < 4 || bytes.Length % 2 != 0)
        {
            return false;
        }

        var pairCount = Math.Min(bytes.Length / 2, 2048);
        var evenZeros = 0;
        var oddZeros = 0;
        for (var pair = 0; pair < pairCount; pair++)
        {
            if (bytes[pair * 2] == 0)
            {
                evenZeros++;
            }

            if (bytes[(pair * 2) + 1] == 0)
            {
                oddZeros++;
            }
        }

        if (oddZeros * 10 >= pairCount * 6 && evenZeros * 10 <= pairCount)
        {
            encoding = StrictUtf16LittleEndian;
            return true;
        }

        if (evenZeros * 10 >= pairCount * 6 && oddZeros * 10 <= pairCount)
        {
            encoding = StrictUtf16BigEndian;
            return true;
        }

        return false;
    }

    private static bool LooksBinary(byte[] bytes)
    {
        var inspectedLength = Math.Min(bytes.Length, 8192);
        var suspiciousControls = 0;
        for (var index = 0; index < inspectedLength; index++)
        {
            var value = bytes[index];
            if (value == 0)
            {
                return true;
            }

            if (value < 0x20 && value is not (0x09 or 0x0A or 0x0D) || value == 0x7F)
            {
                suspiciousControls++;
            }
        }

        return suspiciousControls > Math.Max(1, inspectedLength / 100);
    }

    private static bool IsTextLike(string text)
    {
        var suspiciousControls = text.Count(character =>
            char.IsControl(character) && character is not ('\t' or '\n' or '\r'));
        return !text.Contains('\0') && suspiciousControls <= Math.Max(1, text.Length / 100);
    }

    private static bool TryDecode(Encoding encoding, byte[] bytes, int offset, out string text)
    {
        try
        {
            text = encoding.GetString(bytes, offset, bytes.Length - offset);
            return true;
        }
        catch (DecoderFallbackException)
        {
            text = string.Empty;
            return false;
        }
    }

    private static Encoding CreateWindows1252Encoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    }
}
