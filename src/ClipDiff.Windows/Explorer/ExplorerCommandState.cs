using System.Runtime.InteropServices;

namespace ClipDiff.Windows.Explorer;

internal static class ExplorerCommandState
{
    internal const uint Enabled = 0;
    internal const uint Hidden = 0x2;
    private const uint SiAttribAnd = 0x1;
    private const uint SiAttribOr = 0x2;
    private const uint SfgaoFileSystem = 0x40000000;
    private const uint SfgaoFolder = 0x20000000;
    private const uint SfgaoStream = 0x00400000;

    public static uint GetState(IShellItemArray? selection, bool canCompare)
    {
        if (!canCompare || selection is null)
        {
            return Hidden;
        }

        try
        {
            // Inspect only Shell metadata. Opening a menu must not read file contents or paths.
            if (selection.GetCount(out var count) < 0 || count != 2 ||
                selection.GetAttributes(SiAttribAnd, SfgaoFileSystem | SfgaoStream, out var commonAttributes) < 0 ||
                (commonAttributes & SfgaoFileSystem) == 0 ||
                selection.GetAttributes(SiAttribOr, SfgaoFolder, out var anyAttributes) < 0)
            {
                return Hidden;
            }

            // ZIP files can have both FOLDER and STREAM set; physical directories have no stream.
            return (anyAttributes & SfgaoFolder) == 0 || (commonAttributes & SfgaoStream) != 0
                ? Enabled
                : Hidden;
        }
        catch (Exception exception) when (exception is COMException or InvalidComObjectException or ArgumentException)
        {
            return Hidden;
        }
    }
}

[ComVisible(true)]
[Guid("BDDACB60-7657-47AE-8445-D23E1ACF82AE")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IExplorerCommandState
{
    [PreserveSig]
    int GetState(
        [MarshalAs(UnmanagedType.Interface)] IShellItemArray? selection,
        [MarshalAs(UnmanagedType.Bool)] bool okToBeSlow,
        out uint state);
}

[ComImport]
[Guid("B63EA76D-1F85-456F-A19C-48159EFA858B")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IShellItemArray
{
    // Keep the complete native vtable order, including methods the state check never calls.
    [PreserveSig]
    int BindToHandler(nint bindContext, in Guid handlerId, in Guid interfaceId, out nint result);

    [PreserveSig]
    int GetPropertyStore(uint flags, in Guid interfaceId, out nint result);

    [PreserveSig]
    int GetPropertyDescriptionList(nint propertyKey, in Guid interfaceId, out nint result);

    [PreserveSig]
    int GetAttributes(uint flags, uint mask, out uint attributes);

    [PreserveSig]
    int GetCount(out uint count);

    [PreserveSig]
    int GetItemAt(uint index, out nint item);

    [PreserveSig]
    int EnumItems(out nint items);
}
