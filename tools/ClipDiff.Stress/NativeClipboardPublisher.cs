using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ClipDiff.Stress;

[SupportedOSPlatform("windows")]
internal sealed class NativeClipboardPublisher : IDisposable
{
	private readonly nint windowHandle;

	public NativeClipboardPublisher()
	{
		// A real owner is required: EmptyClipboard after OpenClipboard(NULL) would
		// leave no owner and cause SetClipboardData to fail. Data is eagerly rendered.
		windowHandle = CreateWindowEx(0, "STATIC", "ClipDiff stress", 0, 0, 0, 0, 0,
			new nint(-3), 0, 0, 0);

		if (windowHandle == 0)
		{
			throw new Win32Exception();
		}
	}

	public bool TryWrite(StressItem item, CancellationToken cancellationToken)
	{
		for (var attempt = 0; attempt < 10; attempt++)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (OpenClipboard(windowHandle))
			{
				try
				{
					Write(item);
					return true;
				}
				finally
				{
					CloseClipboard();
				}
			}

			cancellationToken.WaitHandle.WaitOne(25);
		}

		return false;
	}

	public void Dispose() => DestroyWindow(windowHandle);

	private static void Write(StressItem item)
	{
		var memory = GlobalAlloc(0x42, (nuint)item.Bytes.Length);

		if (memory == 0)
		{
			throw new Win32Exception();
		}

		try
		{
			var pointer = GlobalLock(memory);

			if (pointer == 0)
			{
				throw new Win32Exception();
			}

			try
			{
				Marshal.Copy(item.Bytes, 0, pointer, item.Bytes.Length);
			}
			finally
			{
				GlobalUnlock(memory);
			}

			if (!EmptyClipboard() || SetClipboardData(item.Format, memory) == 0)
			{
				throw new Win32Exception();
			}

			memory = 0; // Windows now owns this allocation.
		}
		finally
		{
			if (memory != 0)
			{
				GlobalFree(memory);
			}
		}
	}

	[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern nint CreateWindowEx(uint extendedStyle, string className, string title,
		uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);

	[DllImport("user32.dll")]
	private static extern bool DestroyWindow(nint window);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool OpenClipboard(nint owner);

	[DllImport("user32.dll")]
	private static extern bool CloseClipboard();

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool EmptyClipboard();

	[DllImport("user32.dll", SetLastError = true)]
	private static extern nint SetClipboardData(uint format, nint memory);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern nint GlobalAlloc(uint flags, nuint bytes);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern nint GlobalLock(nint memory);

	[DllImport("kernel32.dll")]
	private static extern bool GlobalUnlock(nint memory);

	[DllImport("kernel32.dll")]
	private static extern nint GlobalFree(nint memory);
}
