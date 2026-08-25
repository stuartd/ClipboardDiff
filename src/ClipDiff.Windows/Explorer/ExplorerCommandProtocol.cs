using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace ClipDiff.Windows.Explorer;

internal static class ExplorerCommandProtocol
{
    private const int MaximumPathBytes = 256 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static async ValueTask WriteFilePathAsync(
        Stream stream,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var payload = StrictUtf8.GetBytes(filePath);
        if (payload.Length > MaximumPathBytes)
        {
            throw new ArgumentException("The selected file path is too long.", nameof(filePath));
        }

        var header = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async ValueTask<string?> ReadFilePathAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var header = new byte[sizeof(int)];
        try
        {
            await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        }
        catch (EndOfStreamException)
        {
            return null;
        }

        var payloadLength = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (payloadLength <= 0 || payloadLength > MaximumPathBytes)
        {
            return null;
        }

        var payload = new byte[payloadLength];
        try
        {
            await stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
            var filePath = StrictUtf8.GetString(payload);
            return string.IsNullOrWhiteSpace(filePath) || filePath.Contains('\0')
                ? null
                : filePath;
        }
        catch (EndOfStreamException)
        {
            return null;
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }
}
