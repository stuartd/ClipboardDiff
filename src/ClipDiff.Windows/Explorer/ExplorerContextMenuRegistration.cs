using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32;

namespace ClipDiff.Windows.Explorer;

internal sealed class ExplorerContextMenuRegistration : IDisposable
{
    private const string VerbKeyPath = @"Software\Classes\*\shell\ClipDiff.CompareWithCurrent";
    private const string CommandKeyPath = VerbKeyPath + @"\command";
    private const string OwnerValueName = "ClipDiffOwner";
    private const string OwnerValue = "ClipDiff.ExplorerContextMenu.v1";
    private const uint ShcneAssocChanged = 0x08000000;
    private const uint ShcnfIdList = 0x0000;
    private readonly string _commandLine;
    private readonly string _iconPath;
    private bool _stateKnown;
    private bool _enabled;
    private bool _disposed;

    public ExplorerContextMenuRegistration()
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
        {
            _iconPath = string.Empty;
            _commandLine = string.Empty;
            return;
        }

        _iconPath = processPath;
        _commandLine = ExplorerContextCommandLine.BuildShellCommand(
            processPath,
            Environment.GetCommandLineArgs().FirstOrDefault());
    }

    public void SetEnabled(bool enabled)
    {
        if (_disposed || _stateKnown && _enabled == enabled)
        {
            return;
        }

        _stateKnown = true;
        if (enabled)
        {
            _enabled = TryRegister();
            return;
        }

        _enabled = false;
        if (TryRemoveOwnedRegistration())
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
        if (TryRemoveOwnedRegistration())
        {
            NotifyShellChanged();
        }
    }

    private bool TryRegister()
    {
        if (_commandLine.Length == 0)
        {
            return false;
        }

        try
        {
            using var verbKey = Registry.CurrentUser.CreateSubKey(VerbKeyPath, writable: true);
            using var commandKey = Registry.CurrentUser.CreateSubKey(CommandKeyPath, writable: true);
            if (verbKey is null || commandKey is null)
            {
                return false;
            }

            commandKey.SetValue(null, _commandLine, RegistryValueKind.String);
            verbKey.SetValue(null, "Compare with current ClipDiff capture", RegistryValueKind.String);
            verbKey.SetValue("Icon", $"{Quote(_iconPath)},0", RegistryValueKind.String);
            verbKey.SetValue("MultiSelectModel", "Single", RegistryValueKind.String);
            verbKey.SetValue(OwnerValueName, OwnerValue, RegistryValueKind.String);
            NotifyShellChanged();
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                          SecurityException or PlatformNotSupportedException)
        {
            if (TryRemoveOwnedRegistration())
            {
                NotifyShellChanged();
            }

            return false;
        }
    }

    private bool TryRemoveOwnedRegistration()
    {
        if (_commandLine.Length == 0)
        {
            return false;
        }

        try
        {
            using var verbKey = Registry.CurrentUser.OpenSubKey(VerbKeyPath, writable: false);
            using var commandKey = Registry.CurrentUser.OpenSubKey(CommandKeyPath, writable: false);
            var hasOwnerMarker = string.Equals(
                verbKey?.GetValue(OwnerValueName) as string,
                OwnerValue,
                StringComparison.Ordinal);
            var hasCurrentCommand = string.Equals(
                commandKey?.GetValue(null) as string,
                _commandLine,
                StringComparison.Ordinal);
            if (!hasOwnerMarker && !hasCurrentCommand)
            {
                return false;
            }

            verbKey?.Close();
            commandKey?.Close();
            Registry.CurrentUser.DeleteSubKeyTree(VerbKeyPath, throwOnMissingSubKey: false);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                          SecurityException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";

    private static void NotifyShellChanged() =>
        SHChangeNotify(ShcneAssocChanged, ShcnfIdList, nint.Zero, nint.Zero);

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint eventId, uint flags, nint item1, nint item2);
}
