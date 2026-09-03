using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32;

namespace ClipDiff.Windows.Explorer;

internal sealed class ExplorerContextMenuRegistration : IDisposable
{
    private const string SingleVerbKeyPath = @"Software\Classes\*\shell\ClipDiff.CompareWithCurrent";
    private const string SingleCommandKeyPath = SingleVerbKeyPath + @"\command";
    private const string PairVerbKeyPath = @"Software\Classes\*\shell\ClipDiff.CompareSelected";
    private const string PairDropTargetKeyPath = PairVerbKeyPath + @"\DropTarget";
    private const string OwnerValueName = "ClipDiffOwner";
    private const string OwnerValue = "ClipDiff.ExplorerContextMenu.v1";
    private const uint ShcneAssocChanged = 0x08000000;
    private const uint ShcnfIdList = 0x0000;
    private static readonly string ClassIdText = ExplorerDropTargetServer.ClassId.ToString("B").ToUpperInvariant();
    private static readonly string ClassKeyPath = $@"Software\Classes\CLSID\{ClassIdText}";
    private static readonly string LocalServerKeyPath = ClassKeyPath + @"\LocalServer32";
    private readonly string _singleCommandLine;
    private readonly string _comServerCommandLine;
    private readonly string _iconPath;
    private bool _singleStateKnown;
    private bool _singleEnabled;
    private string? _singleDisplayName;
    private bool _pairStateKnown;
    private bool _pairEnabled;
    private bool _disposed;

    public ExplorerContextMenuRegistration()
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
        {
            _iconPath = string.Empty;
            _singleCommandLine = string.Empty;
            _comServerCommandLine = string.Empty;
            return;
        }

        var entryAssemblyPath = Environment.GetCommandLineArgs().FirstOrDefault();
        _iconPath = processPath;
        _singleCommandLine = ExplorerContextCommandLine.BuildShellCommand(processPath, entryAssemblyPath);
        _comServerCommandLine = ExplorerContextCommandLine.BuildComServerCommand(processPath, entryAssemblyPath);
    }

    public void SetState(
        bool monitoringEnabled,
        bool hasCurrentCapture,
        bool pairHandlerAvailable,
        string? currentSourceFileName = null)
    {
        if (_disposed)
        {
            return;
        }

        var changed = UpdateSingleVerb(
            monitoringEnabled && hasCurrentCapture,
            ExplorerContextCommandLine.BuildDisplayName(currentSourceFileName));
        changed |= UpdatePairVerb(monitoringEnabled && pairHandlerAvailable);
        if (changed)
        {
            NotifyShellChanged();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        var changed = TryRemoveOwnedSingleRegistration();
        changed |= TryRemoveOwnedPairRegistration();
        if (changed)
        {
            NotifyShellChanged();
        }
    }

    private bool UpdateSingleVerb(bool enabled, string displayName)
    {
        if (_singleStateKnown && _singleEnabled == enabled &&
            (!enabled || string.Equals(_singleDisplayName, displayName, StringComparison.Ordinal)))
        {
            return false;
        }

        _singleStateKnown = true;
        if (enabled)
        {
            _singleEnabled = TryRegisterSingle(displayName);
            _singleDisplayName = _singleEnabled ? displayName : null;
            return _singleEnabled;
        }

        _singleEnabled = false;
        _singleDisplayName = null;
        return TryRemoveOwnedSingleRegistration();
    }

    private bool UpdatePairVerb(bool enabled)
    {
        if (_pairStateKnown && _pairEnabled == enabled)
        {
            return false;
        }

        _pairStateKnown = true;
        if (enabled)
        {
            _pairEnabled = TryRegisterPair();
            return _pairEnabled;
        }

        _pairEnabled = false;
        return TryRemoveOwnedPairRegistration();
    }

    private bool TryRegisterSingle(string displayName)
    {
        if (_singleCommandLine.Length == 0)
        {
            return false;
        }

        try
        {
            using var verbKey = Registry.CurrentUser.CreateSubKey(SingleVerbKeyPath, writable: true);
            using var commandKey = Registry.CurrentUser.CreateSubKey(SingleCommandKeyPath, writable: true);
            if (verbKey is null || commandKey is null)
            {
                return false;
            }

            commandKey.SetValue(null, _singleCommandLine, RegistryValueKind.String);
            verbKey.SetValue(null, displayName, RegistryValueKind.String);
            verbKey.SetValue("Icon", $"{Quote(_iconPath)},0", RegistryValueKind.String);
            verbKey.SetValue("MultiSelectModel", "Single", RegistryValueKind.String);
            verbKey.SetValue(OwnerValueName, OwnerValue, RegistryValueKind.String);
            return true;
        }
        catch (Exception exception) when (IsRegistryException(exception))
        {
            TryRemoveOwnedSingleRegistration();
            return false;
        }
    }

    private bool TryRegisterPair()
    {
        if (_comServerCommandLine.Length == 0)
        {
            return false;
        }

        try
        {
            using var verbKey = Registry.CurrentUser.CreateSubKey(PairVerbKeyPath, writable: true);
            using var dropTargetKey = Registry.CurrentUser.CreateSubKey(PairDropTargetKeyPath, writable: true);
            using var classKey = Registry.CurrentUser.CreateSubKey(ClassKeyPath, writable: true);
            using var localServerKey = Registry.CurrentUser.CreateSubKey(LocalServerKeyPath, writable: true);
            if (verbKey is null || dropTargetKey is null || classKey is null || localServerKey is null)
            {
                return false;
            }

            verbKey.SetValue(null, ExplorerContextCommandLine.CompareSelectedDisplayName, RegistryValueKind.String);
            verbKey.SetValue("Icon", $"{Quote(_iconPath)},0", RegistryValueKind.String);
            verbKey.SetValue("MultiSelectModel", "Player", RegistryValueKind.String);
            verbKey.SetValue(OwnerValueName, OwnerValue, RegistryValueKind.String);
            dropTargetKey.SetValue("Clsid", ClassIdText, RegistryValueKind.String);
            classKey.SetValue(null, "ClipDiff Explorer comparison", RegistryValueKind.String);
            classKey.SetValue(OwnerValueName, OwnerValue, RegistryValueKind.String);
            localServerKey.SetValue(null, _comServerCommandLine, RegistryValueKind.String);
            return true;
        }
        catch (Exception exception) when (IsRegistryException(exception))
        {
            TryRemoveOwnedPairRegistration();
            return false;
        }
    }

    private bool TryRemoveOwnedSingleRegistration()
    {
        if (_singleCommandLine.Length == 0)
        {
            return false;
        }

        try
        {
            using var verbKey = Registry.CurrentUser.OpenSubKey(SingleVerbKeyPath, writable: false);
            using var commandKey = Registry.CurrentUser.OpenSubKey(SingleCommandKeyPath, writable: false);
            var hasOwnerMarker = string.Equals(
                verbKey?.GetValue(OwnerValueName) as string,
                OwnerValue,
                StringComparison.Ordinal);
            var hasCurrentCommand = string.Equals(
                commandKey?.GetValue(null) as string,
                _singleCommandLine,
                StringComparison.Ordinal);
            if (!hasOwnerMarker && !hasCurrentCommand)
            {
                return false;
            }

            verbKey?.Close();
            commandKey?.Close();
            Registry.CurrentUser.DeleteSubKeyTree(SingleVerbKeyPath, throwOnMissingSubKey: false);
            return true;
        }
        catch (Exception exception) when (IsRegistryException(exception))
        {
            return false;
        }
    }

    private bool TryRemoveOwnedPairRegistration()
    {
        if (_comServerCommandLine.Length == 0)
        {
            return false;
        }

        try
        {
            var changed = false;
            using (var verbKey = Registry.CurrentUser.OpenSubKey(PairVerbKeyPath, writable: false))
            {
                if (string.Equals(
                        verbKey?.GetValue(OwnerValueName) as string,
                        OwnerValue,
                        StringComparison.Ordinal))
                {
                    verbKey!.Close();
                    Registry.CurrentUser.DeleteSubKeyTree(PairVerbKeyPath, throwOnMissingSubKey: false);
                    changed = true;
                }
            }

            using (var classKey = Registry.CurrentUser.OpenSubKey(ClassKeyPath, writable: false))
            using (var localServerKey = Registry.CurrentUser.OpenSubKey(LocalServerKeyPath, writable: false))
            {
                var hasOwnerMarker = string.Equals(
                    classKey?.GetValue(OwnerValueName) as string,
                    OwnerValue,
                    StringComparison.Ordinal);
                var hasCurrentCommand = string.Equals(
                    localServerKey?.GetValue(null) as string,
                    _comServerCommandLine,
                    StringComparison.Ordinal);
                if (hasOwnerMarker || hasCurrentCommand)
                {
                    classKey?.Close();
                    localServerKey?.Close();
                    Registry.CurrentUser.DeleteSubKeyTree(ClassKeyPath, throwOnMissingSubKey: false);
                    changed = true;
                }
            }

            return changed;
        }
        catch (Exception exception) when (IsRegistryException(exception))
        {
            return false;
        }
    }

    private static bool IsRegistryException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or SecurityException or PlatformNotSupportedException;

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";

    private static void NotifyShellChanged() =>
        SHChangeNotify(ShcneAssocChanged, ShcnfIdList, nint.Zero, nint.Zero);

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint eventId, uint flags, nint item1, nint item2);
}
