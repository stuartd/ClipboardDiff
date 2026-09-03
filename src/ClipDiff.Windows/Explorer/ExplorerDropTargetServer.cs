using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using ClipDiff.Windows.Native;
using ComDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace ClipDiff.Windows.Explorer;

internal sealed class ExplorerDropTargetServer : IDisposable
{
    internal static readonly Guid ClassId = new("4D22FA39-9E5D-42BD-BF0A-8AE885704EC7");

    private const uint ClsctxLocalServer = 0x4;
    private const uint RegclsMultipleUse = 0x1;
    private const int CoinitApartmentThreaded = 0x2;
    private const int RpcEChangedMode = unchecked((int)0x80010106);
    private readonly ExplorerDropTargetClassFactory _classFactory;
    private nint _classFactoryPointer;
    private uint _registrationCookie;
    private bool _uninitializeCom;
    private bool _disposed;

    public ExplorerDropTargetServer(Action<IReadOnlyList<string>> selectedFilesHandler)
    {
        ArgumentNullException.ThrowIfNull(selectedFilesHandler);
        _classFactory = new ExplorerDropTargetClassFactory(selectedFilesHandler);

        var initializeResult = CoInitializeEx(nint.Zero, CoinitApartmentThreaded);
        if (initializeResult >= 0)
        {
            _uninitializeCom = true;
        }
        else if (initializeResult != RpcEChangedMode)
        {
            return;
        }

        try
        {
            var classId = ClassId;
            _classFactoryPointer = Marshal.GetIUnknownForObject(_classFactory);
            var registrationResult = CoRegisterClassObject(
                ref classId,
                _classFactoryPointer,
                ClsctxLocalServer,
                RegclsMultipleUse,
                out _registrationCookie);
            if (registrationResult < 0)
            {
                _registrationCookie = 0;
                Marshal.Release(_classFactoryPointer);
                _classFactoryPointer = nint.Zero;
            }
        }
        catch (Exception exception) when (exception is PlatformNotSupportedException or ArgumentException)
        {
            if (_classFactoryPointer != nint.Zero)
            {
                Marshal.Release(_classFactoryPointer);
                _classFactoryPointer = nint.Zero;
            }
        }
    }

    public bool IsRegistered => _registrationCookie != 0;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_registrationCookie != 0)
        {
            CoRevokeClassObject(_registrationCookie);
            _registrationCookie = 0;
        }

        if (_classFactoryPointer != nint.Zero)
        {
            Marshal.Release(_classFactoryPointer);
            _classFactoryPointer = nint.Zero;
        }

        if (_uninitializeCom)
        {
            CoUninitialize();
            _uninitializeCom = false;
        }
    }

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(nint reserved, int coInit);

    [DllImport("ole32.dll")]
    private static extern int CoRegisterClassObject(
        ref Guid classId,
        nint classFactory,
        uint classContext,
        uint flags,
        out uint registrationCookie);

    [DllImport("ole32.dll")]
    private static extern int CoRevokeClassObject(uint registrationCookie);

    [DllImport("ole32.dll")]
    private static extern void CoUninitialize();
}

[ComVisible(true)]
[Guid("00000001-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IClassFactory
{
    [PreserveSig]
    int CreateInstance(nint outer, ref Guid interfaceId, out nint createdObject);

    [PreserveSig]
    int LockServer([MarshalAs(UnmanagedType.Bool)] bool shouldLock);
}

[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
public sealed class ExplorerDropTargetClassFactory : IClassFactory
{
    private const int ClassENoAggregation = unchecked((int)0x80040110);
    private const int ENoInterface = unchecked((int)0x80004002);
    private readonly Action<IReadOnlyList<string>> _selectedFilesHandler;

    internal ExplorerDropTargetClassFactory(Action<IReadOnlyList<string>> selectedFilesHandler)
    {
        _selectedFilesHandler = selectedFilesHandler;
    }

    public int CreateInstance(nint outer, ref Guid interfaceId, out nint createdObject)
    {
        createdObject = nint.Zero;
        if (outer != nint.Zero)
        {
            return ClassENoAggregation;
        }

        var dropTarget = new ExplorerDropTarget(_selectedFilesHandler);
        var unknown = Marshal.GetIUnknownForObject(dropTarget);
        try
        {
            return Marshal.QueryInterface(unknown, in interfaceId, out createdObject);
        }
        catch (PlatformNotSupportedException)
        {
            createdObject = nint.Zero;
            return ENoInterface;
        }
        finally
        {
            Marshal.Release(unknown);
        }
    }

    public int LockServer(bool shouldLock) => 0;
}

[ComVisible(true)]
[Guid("00000122-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IExplorerDropTarget
{
    [PreserveSig]
    int DragEnter(
        [MarshalAs(UnmanagedType.Interface)] ComDataObject dataObject,
        uint keyState,
        NativePoint point,
        ref uint effect);

    [PreserveSig]
    int DragOver(uint keyState, NativePoint point, ref uint effect);

    [PreserveSig]
    int DragLeave();

    [PreserveSig]
    int Drop(
        [MarshalAs(UnmanagedType.Interface)] ComDataObject dataObject,
        uint keyState,
        NativePoint point,
        ref uint effect);
}

[StructLayout(LayoutKind.Sequential)]
public readonly struct NativePoint
{
    public readonly int X;
    public readonly int Y;
}

[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
public sealed class ExplorerDropTarget : IExplorerDropTarget
{
    private const uint DropEffectNone = 0;
    private const uint DropEffectCopy = 1;
    private const int DvAspectContent = 1;
    private readonly Action<IReadOnlyList<string>> _selectedFilesHandler;
    private bool _canDrop;

    internal ExplorerDropTarget(Action<IReadOnlyList<string>> selectedFilesHandler)
    {
        _selectedFilesHandler = selectedFilesHandler;
    }

    public int DragEnter(ComDataObject dataObject, uint keyState, NativePoint point, ref uint effect)
    {
        try
        {
            _canDrop = ContainsExactFilePair(dataObject);
        }
        catch (Exception exception) when (IsDataObjectException(exception))
        {
            _canDrop = false;
        }

        effect = _canDrop ? DropEffectCopy : DropEffectNone;
        return 0;
    }

    public int DragOver(uint keyState, NativePoint point, ref uint effect)
    {
        effect = _canDrop ? DropEffectCopy : DropEffectNone;
        return 0;
    }

    public int DragLeave()
    {
        _canDrop = false;
        return 0;
    }

    public int Drop(ComDataObject dataObject, uint keyState, NativePoint point, ref uint effect)
    {
        try
        {
            var filePaths = ReadFilePaths(dataObject);
            effect = filePaths.Count == 2 ? DropEffectCopy : DropEffectNone;
            if (filePaths.Count == 2)
            {
                _selectedFilesHandler(filePaths);
            }
        }
        catch (Exception exception) when (IsDataObjectException(exception))
        {
            effect = DropEffectNone;
        }
        finally
        {
            _canDrop = false;
        }

        return 0;
    }

    private static bool ContainsExactFilePair(ComDataObject dataObject)
    {
        var filePaths = ReadFilePaths(dataObject);
        return filePaths.Count == 2;
    }

    private static IReadOnlyList<string> ReadFilePaths(ComDataObject dataObject)
    {
        var format = CreateFileDropFormat();
        if (dataObject.QueryGetData(ref format) != 0)
        {
            return [];
        }

        STGMEDIUM medium;
        try
        {
            dataObject.GetData(ref format, out medium);
        }
        catch (COMException)
        {
            return [];
        }

        try
        {
            if (medium.tymed != TYMED.TYMED_HGLOBAL || medium.unionmember == nint.Zero)
            {
                return [];
            }

            var fileCount = NativeMethods.DragQueryFile(
                medium.unionmember,
                uint.MaxValue,
                fileName: null,
                characterCount: 0);
            if (fileCount != 2)
            {
                return [];
            }

            var paths = new string[2];
            for (uint index = 0; index < paths.Length; index++)
            {
                var characterCount = NativeMethods.DragQueryFile(
                    medium.unionmember,
                    index,
                    fileName: null,
                    characterCount: 0);
                if (characterCount == 0)
                {
                    return [];
                }

                var path = new StringBuilder(checked((int)characterCount + 1));
                if (NativeMethods.DragQueryFile(
                        medium.unionmember,
                        index,
                        path,
                        checked(characterCount + 1)) == 0)
                {
                    return [];
                }

                paths[index] = path.ToString();
            }

            return paths;
        }
        finally
        {
            ReleaseStgMedium(ref medium);
        }
    }

    private static FORMATETC CreateFileDropFormat() => new()
    {
        cfFormat = checked((short)NativeMethods.CfHDrop),
        dwAspect = (DVASPECT)DvAspectContent,
        lindex = -1,
        tymed = TYMED.TYMED_HGLOBAL
    };

    private static bool IsDataObjectException(Exception exception) =>
        exception is COMException or InvalidComObjectException or ArgumentException or OverflowException;

    [DllImport("ole32.dll")]
    private static extern void ReleaseStgMedium(ref STGMEDIUM medium);
}
