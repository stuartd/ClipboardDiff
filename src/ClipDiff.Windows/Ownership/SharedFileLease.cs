using System.IO;

namespace ClipDiff.Windows.Ownership;

internal sealed class SharedFileLease : IDisposable
{
	private readonly FileStream fileStream;

	private SharedFileLease(FileStream stream)
	{
		fileStream = stream;
	}

	public static SharedFileLease Acquire(string path)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		return new SharedFileLease(new FileStream(
			path,
			FileMode.OpenOrCreate,
			FileAccess.ReadWrite,
			FileShare.None,
			bufferSize: 1,
			FileOptions.DeleteOnClose));
	}

	public static SharedFileLease? TryAcquire(string path)
	{
		try
		{
			return Acquire(path);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			return null;
		}
	}

	public static SharedFileLease? TryAcquire(string path, TimeSpan timeout)
	{
		SharedFileLease? ownership = null;
		SpinWait.SpinUntil(() => (ownership = TryAcquire(path)) is not null, timeout);
		return ownership;
	}

	public void Dispose() => fileStream.Dispose();
}
