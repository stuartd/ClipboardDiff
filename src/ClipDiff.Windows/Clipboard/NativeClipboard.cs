using System.Runtime.InteropServices;
using System.Text;
using ClipDiff.Windows.Native;

namespace ClipDiff.Windows.Clipboard;

internal sealed class NativeClipboard : IClipboardDataAccess
{
    public bool TryOpen(nint owner) => NativeMethods.OpenClipboard(owner);

    public void Close() => NativeMethods.CloseClipboard();

    public bool TryHasAnyFormats(out bool hasAnyFormats)
    {
        Marshal.SetLastPInvokeError(0);
        var firstFormat = NativeMethods.EnumClipboardFormats(0);
        if (firstFormat != 0)
        {
            hasAnyFormats = true;
            return true;
        }

        var error = Marshal.GetLastPInvokeError();
        hasAnyFormats = false;
        return error == 0;
    }

    public bool IsFormatAvailable(uint format) => NativeMethods.IsClipboardFormatAvailable(format);

    public bool TryReadDword(uint format, out uint value)
    {
        value = 0;
        var memory = NativeMethods.GetClipboardData(format);
        if (memory == nint.Zero || NativeMethods.GlobalSize(memory) != sizeof(uint))
        {
            return false;
        }

        var pointer = NativeMethods.GlobalLock(memory);
        if (pointer == nint.Zero)
        {
            return false;
        }

        try
        {
            value = unchecked((uint)Marshal.ReadInt32(pointer));
            return true;
        }
        finally
        {
            NativeMethods.GlobalUnlock(memory);
        }
    }

    public bool TryReadUnicodeText(out string? text)
    {
        text = null;
        var memory = NativeMethods.GetClipboardData(NativeMethods.CfUnicodeText);
        if (memory == nint.Zero)
        {
            return false;
        }

        var size = NativeMethods.GlobalSize(memory);
        if (size < 2 || size % 2 != 0 || size > int.MaxValue)
        {
            return false;
        }

        var pointer = NativeMethods.GlobalLock(memory);
        if (pointer == nint.Zero)
        {
            return false;
        }

        try
        {
            var maximumCharacters = checked((int)(size / 2));
            var value = Marshal.PtrToStringUni(pointer, maximumCharacters);
            var terminator = value?.IndexOf('\0') ?? -1;
            if (terminator < 0)
            {
                return false;
            }

            text = value![..terminator];
            return true;
        }
        finally
        {
            NativeMethods.GlobalUnlock(memory);
        }
    }

    public bool TryReadFilePaths(out IReadOnlyList<string>? filePaths)
    {
        filePaths = null;
        var dropHandle = NativeMethods.GetClipboardData(NativeMethods.CfHDrop);
        if (dropHandle == nint.Zero)
        {
            return false;
        }

        var fileCount = NativeMethods.DragQueryFile(dropHandle, uint.MaxValue, null, 0);
        if (fileCount > int.MaxValue)
        {
            return false;
        }

        var paths = new List<string>(checked((int)fileCount));
        for (uint index = 0; index < fileCount; index++)
        {
            var pathLength = NativeMethods.DragQueryFile(dropHandle, index, null, 0);
            if (pathLength == 0 || pathLength >= int.MaxValue)
            {
                return false;
            }

            var path = new StringBuilder(checked((int)pathLength + 1));
            if (NativeMethods.DragQueryFile(dropHandle, index, path, pathLength + 1) != pathLength)
            {
                return false;
            }

            paths.Add(path.ToString());
        }

        filePaths = paths;
        return true;
    }
}
