using System.IO;
using System.Security;
using System.Text;

namespace ClipDiff.Windows.Clipboard;

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

    public async ValueTask<string?> ReadAsync(
        IReadOnlyList<string> filePaths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filePaths);

        var names = filePaths
            .Select(GetDisplayName)
            .Where(name => name.Length > 0)
            .ToArray();

        if (filePaths.Count != 1)
        {
            return names.Length == 0 ? null : string.Join('\n', names);
        }

        var fallbackName = names.FirstOrDefault();
        if (fallbackName is null)
        {
            return null;
        }

        var path = filePaths[0];
        try
        {
            if (KnownBinaryExecutableExtensions.Contains(Path.GetExtension(path)))
            {
                return fallbackName;
            }

            await using var stream = new FileStream(path, new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.ReadWrite | FileShare.Delete,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan
            });

            if (stream.Length == 0 || stream.Length > _maximumTextFileBytes || stream.Length > int.MaxValue)
            {
                return fallbackName;
            }

            var bytes = new byte[checked((int)stream.Length)];
            await stream.ReadExactlyAsync(bytes, cancellationToken);
            return TryDecodeText(bytes, out var text) && text.Length > 0
                ? text
                : fallbackName;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                          ArgumentException or NotSupportedException or SecurityException)
        {
            return fallbackName;
        }
    }

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
