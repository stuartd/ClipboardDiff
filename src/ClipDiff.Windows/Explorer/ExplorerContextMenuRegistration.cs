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
    private const string PairHandlerKeyPath = @"Software\Classes\*\shellex\ContextMenuHandlers\ClipDiff.CompareSelected";
    private const string ExtensionClassId = "{6B46A974-40E2-4AD4-9F68-E534202B11E8}";
    private const string ExtensionClassKeyPath = @"Software\Classes\CLSID\" + ExtensionClassId;
    private const string ExtensionServerKeyPath = ExtensionClassKeyPath + @"\InprocServer32";
    private const string ReadyEventName = @"Local\ClipDiff.ExplorerPairReady";
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
    private readonly string _extensionPath = Path.Combine(AppContext.BaseDirectory, "ClipDiff.ShellExtension.dll");
    private readonly EventWaitHandle? _pairReady;
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
        try
        {
            _pairReady = new EventWaitHandle(false, EventResetMode.ManualReset, ReadyEventName);
            _pairReady.Reset();
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or
                                          WaitHandleCannotBeOpenedException or PlatformNotSupportedException)
        {
            // The clipboard/tray workflows remain available if native integration is unavailable.
        }
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
        var pairEnabled = monitoringEnabled && pairHandlerAvailable && _pairReady is not null;
        if (!pairEnabled) _pairReady?.Reset();
        changed |= UpdatePairVerb(pairEnabled);
        if (_pairEnabled) _pairReady?.Set();
        else _pairReady?.Reset();
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
        _pairReady?.Reset();
        _pairReady?.Dispose();
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

        // Remove the obsolete static verb (including CommandStateHandler) on upgrade, even
        // when the previous process crashed or the new native DLL is missing.
        var changed = !_pairStateKnown && TryRemoveOwnedPairRegistration();
        _pairStateKnown = true;
        if (enabled)
        {
            _pairEnabled = TryRegisterPair();
            return changed || _pairEnabled;
        }

        _pairEnabled = false;
        return TryRemoveOwnedPairRegistration() || changed;
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
        if (_comServerCommandLine.Length == 0 || !File.Exists(_extensionPath) || !Environment.Is64BitProcess)
        {
            return false;
        }

        try
        {
            using var classKey = Registry.CurrentUser.CreateSubKey(ClassKeyPath, writable: true);
            classKey.SetValue(OwnerValueName, OwnerValue, RegistryValueKind.String);
            using var localServerKey = Registry.CurrentUser.CreateSubKey(LocalServerKeyPath, writable: true);
            classKey.SetValue(null, "ClipDiff Explorer comparison", RegistryValueKind.String);
            localServerKey.SetValue(null, _comServerCommandLine, RegistryValueKind.String);

            using var extensionClassKey = Registry.CurrentUser.CreateSubKey(ExtensionClassKeyPath, writable: true);
            extensionClassKey.SetValue(OwnerValueName, OwnerValue, RegistryValueKind.String);
            extensionClassKey.SetValue(null, "ClipDiff selection menu", RegistryValueKind.String);
            using var extensionServerKey = Registry.CurrentUser.CreateSubKey(ExtensionServerKeyPath, writable: true);
            extensionServerKey.SetValue(null, _extensionPath, RegistryValueKind.String);
            extensionServerKey.SetValue("ThreadingModel", "Apartment", RegistryValueKind.String);

            using var handlerKey = Registry.CurrentUser.CreateSubKey(PairHandlerKeyPath, writable: true);
            handlerKey.SetValue(OwnerValueName, OwnerValue, RegistryValueKind.String);
            handlerKey.SetValue(null, ExtensionClassId, RegistryValueKind.String);
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

        var changed = TryRemoveOwnedKey(PairVerbKeyPath);
        changed |= TryRemoveOwnedKey(PairHandlerKeyPath);
        changed |= TryRemoveOwnedKey(ExtensionClassKeyPath);
        changed |= TryRemoveOwnedKey(ClassKeyPath, LocalServerKeyPath, _comServerCommandLine);
        return changed;
    }

    private static bool TryRemoveOwnedKey(string keyPath, string? commandKeyPath = null, string? currentCommand = null)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: false);
            using var commandKey = commandKeyPath is null ? null : Registry.CurrentUser.OpenSubKey(commandKeyPath);
            var owned = string.Equals(key?.GetValue(OwnerValueName) as string, OwnerValue, StringComparison.Ordinal);
            var matchesCommand = currentCommand is not null &&
                string.Equals(commandKey?.GetValue(null) as string, currentCommand, StringComparison.Ordinal);
            if (!owned && !matchesCommand) return false;
            key?.Close();
            commandKey?.Close();
            Registry.CurrentUser.DeleteSubKeyTree(keyPath, throwOnMissingSubKey: false);
            return true;
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
