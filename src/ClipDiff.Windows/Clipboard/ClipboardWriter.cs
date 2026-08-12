using System.Runtime.InteropServices;
using System.Text;
using ClipDiff.Windows.Native;

namespace ClipDiff.Windows.Clipboard;

internal sealed class ClipboardWriter
{
    private readonly nint _owner;
    private readonly ClipboardFormatIds _formats;

    public ClipboardWriter(nint owner)
    {
        _owner = owner;
        _formats = new ClipboardFormatIds(
            RegisterFormat("ExcludeClipboardContentFromMonitorProcessing"),
            RegisterFormat("CanIncludeInClipboardHistory"),
            RegisterFormat("CanUploadToCloudClipboard"));
    }

    public bool TryWriteProtectedText(string text, out uint resultingSequenceNumber)
    {
        ArgumentNullException.ThrowIfNull(text);
        resultingSequenceNumber = 0;

        if (!NativeMethods.OpenClipboard(_owner))
        {
            return false;
        }

        try
        {
            if (!NativeMethods.EmptyClipboard() ||
                !TrySetDword(_formats.ExcludeFromMonitor, 0) ||
                !TrySetDword(_formats.IncludeInHistory, 0) ||
                !TrySetDword(_formats.UploadToCloud, 0) ||
                !TrySetUnicodeText(text))
            {
                NativeMethods.EmptyClipboard();
                return false;
            }

            resultingSequenceNumber = NativeMethods.GetClipboardSequenceNumber();
            return true;
        }
        finally
        {
            NativeMethods.CloseClipboard();
        }
    }

    private static uint RegisterFormat(string name)
    {
        var format = NativeMethods.RegisterClipboardFormat(name);
        return format != 0
            ? format
            : throw new InvalidOperationException("A required clipboard format could not be registered.");
    }

    private static bool TrySetDword(uint format, uint value)
    {
        var memory = NativeMethods.GlobalAlloc(
            NativeMethods.GmemMoveable | NativeMethods.GmemZeroInit,
            sizeof(uint));
        if (memory == nint.Zero)
        {
            return false;
        }

        var transferred = false;
        try
        {
            var pointer = NativeMethods.GlobalLock(memory);
            if (pointer == nint.Zero)
            {
                return false;
            }

            try
            {
                Marshal.WriteInt32(pointer, unchecked((int)value));
            }
            finally
            {
                NativeMethods.GlobalUnlock(memory);
            }

            transferred = NativeMethods.SetClipboardData(format, memory) != nint.Zero;
            return transferred;
        }
        finally
        {
            if (!transferred)
            {
                NativeMethods.GlobalFree(memory);
            }
        }
    }

    private static bool TrySetUnicodeText(string text)
    {
        var bytes = Encoding.Unicode.GetBytes(text + '\0');
        var memory = NativeMethods.GlobalAlloc(
            NativeMethods.GmemMoveable | NativeMethods.GmemZeroInit,
            checked((nuint)bytes.Length));
        if (memory == nint.Zero)
        {
            return false;
        }

        var transferred = false;
        try
        {
            var pointer = NativeMethods.GlobalLock(memory);
            if (pointer == nint.Zero)
            {
                return false;
            }

            try
            {
                Marshal.Copy(bytes, 0, pointer, bytes.Length);
            }
            finally
            {
                NativeMethods.GlobalUnlock(memory);
            }

            transferred = NativeMethods.SetClipboardData(NativeMethods.CfUnicodeText, memory) != nint.Zero;
            return transferred;
        }
        finally
        {
            if (!transferred)
            {
                NativeMethods.GlobalFree(memory);
            }
        }
    }
}
