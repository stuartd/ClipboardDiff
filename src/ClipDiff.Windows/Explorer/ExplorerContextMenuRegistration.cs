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
	private readonly string singleCommandLine;
	private readonly string comServerCommandLine;
	private readonly string iconPath;
	private readonly string extensionPath = Path.Combine(AppContext.BaseDirectory, "ClipDiff.ShellExtension.dll");
	private readonly EventWaitHandle? pairReady;
	private readonly ExplorerRegistrationCoordinator? registrationCoordinator;
	private bool pairRegistered;
	private bool disposed;

	public ExplorerContextMenuRegistration()
	{
		var processPath = Environment.ProcessPath;

		if (string.IsNullOrWhiteSpace(processPath))
		{
			iconPath = string.Empty;
			singleCommandLine = string.Empty;
			comServerCommandLine = string.Empty;
			return;
		}

		iconPath = processPath;
		singleCommandLine = string.Empty;
		comServerCommandLine = string.Empty;

		try
		{
			var entryAssemblyPath = ApplicationCommandLine.GetCurrentEntryAssemblyPath();
			singleCommandLine = ExplorerContextCommandLine.BuildShellCommand(processPath, entryAssemblyPath);
			comServerCommandLine = ExplorerContextCommandLine.BuildComServerCommand(processPath, entryAssemblyPath);
			registrationCoordinator = new ExplorerRegistrationCoordinator(
				new ExplorerRegistryClaimStore(),
				Path.Combine(
					Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
					"ClipDiff",
					"ExplorerOwners"));
		}
		catch (Exception exception) when (IsRegistryException(exception) || exception is ArgumentException)
		{
			// The clipboard/tray workflows remain available if native integration is unavailable.
			return;
		}

		try
		{
			pairReady = new EventWaitHandle(false, EventResetMode.ManualReset, ReadyEventName);
			pairReady.Reset();
		}
		catch (Exception exception) when (IsRegistryException(exception) ||
			exception is WaitHandleCannotBeOpenedException)
		{
			// A failed pair-ready event does not disable the individual-file command.
		}
	}

	public void SetState(
		bool monitoringEnabled,
		bool hasCurrentCapture,
		bool pairHandlerAvailable,
		string? currentSourceFileName = null)
	{
		if (disposed)
		{
			return;
		}

		if (registrationCoordinator is null)
		{
			return;
		}

		var pairEnabled = monitoringEnabled && pairHandlerAvailable && pairReady is not null &&
			comServerCommandLine.Length > 0 && File.Exists(extensionPath) && Environment.Is64BitProcess;

		if (!pairEnabled)
		{
			pairReady?.Reset();
		}

		try
		{
			var claim = new ExplorerRegistrationClaim(
				registrationCoordinator.OwnerId,
				monitoringEnabled && hasCurrentCapture && singleCommandLine.Length > 0,
				ExplorerContextCommandLine.BuildDisplayName(currentSourceFileName),
				singleCommandLine,
				iconPath,
				pairEnabled,
				comServerCommandLine,
				extensionPath);
			var changed = registrationCoordinator.Update(claim, ApplySelection);

			if (pairEnabled && pairRegistered)
			{
				pairReady?.Set();
			}
			else
			{
				pairReady?.Reset();
			}

			if (changed)
			{
				NotifyShellChanged();
			}
		}
		catch (Exception exception) when (IsRegistryException(exception))
		{
			pairReady?.Reset();
		}
	}

	public void Dispose()
	{
		if (disposed)
		{
			return;
		}

		disposed = true;
		pairReady?.Reset();
		pairReady?.Dispose();

		try
		{
			if (registrationCoordinator?.Remove(ApplySelection) == true)
			{
				NotifyShellChanged();
			}
		}
		catch (Exception exception) when (IsRegistryException(exception))
		{
			registrationCoordinator?.Dispose();
		}
	}

	private bool ApplySelection(ExplorerRegistrationSelection selection)
	{
		var changed = selection.Single is null
			? TryRemoveOwnedSingleRegistration()
			: TryRegisterSingle(
				selection.MultipleOwners
					? ExplorerContextCommandLine.BuildDisplayName(null)
					: selection.Single.DisplayName,
				selection.Single);

		// The old static two-file verb has no live owner in this registration scheme.
		changed |= TryRemoveOwnedKey(PairVerbKeyPath);
		pairRegistered = selection.Pair is not null && TryRegisterPair(selection.Pair);

		if (selection.Pair is null)
		{
			changed |= TryRemoveOwnedPairRegistration();
		}
		else
		{
			changed |= pairRegistered;
		}

		return changed;
	}

	private bool TryRegisterSingle(string displayName, ExplorerRegistrationClaim claim)
	{
		if (claim.SingleCommandLine.Length == 0)
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

			commandKey.SetValue(null, claim.SingleCommandLine, RegistryValueKind.String);
			verbKey.SetValue(null, displayName, RegistryValueKind.String);
			verbKey.SetValue("Icon", $"{Quote(claim.IconPath)},0", RegistryValueKind.String);
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

	private bool TryRegisterPair(ExplorerRegistrationClaim claim)
	{
		if (claim.ComServerCommandLine.Length == 0 || !File.Exists(claim.ExtensionPath) || !Environment.Is64BitProcess)
		{
			return false;
		}

		try
		{
			using var classKey = Registry.CurrentUser.CreateSubKey(ClassKeyPath, writable: true);
			classKey.SetValue(OwnerValueName, OwnerValue, RegistryValueKind.String);
			using var localServerKey = Registry.CurrentUser.CreateSubKey(LocalServerKeyPath, writable: true);
			classKey.SetValue(null, "ClipDiff Explorer comparison", RegistryValueKind.String);
			localServerKey.SetValue(null, claim.ComServerCommandLine, RegistryValueKind.String);

			using var extensionClassKey = Registry.CurrentUser.CreateSubKey(ExtensionClassKeyPath, writable: true);
			extensionClassKey.SetValue(OwnerValueName, OwnerValue, RegistryValueKind.String);
			extensionClassKey.SetValue(null, "ClipDiff selection menu", RegistryValueKind.String);
			using var extensionServerKey = Registry.CurrentUser.CreateSubKey(ExtensionServerKeyPath, writable: true);
			extensionServerKey.SetValue(null, claim.ExtensionPath, RegistryValueKind.String);
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
		if (singleCommandLine.Length == 0)
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
				singleCommandLine,
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
		if (comServerCommandLine.Length == 0)
		{
			return false;
		}

		var changed = TryRemoveOwnedKey(PairVerbKeyPath);
		changed |= TryRemoveOwnedKey(PairHandlerKeyPath);
		changed |= TryRemoveOwnedKey(ExtensionClassKeyPath);
		changed |= TryRemoveOwnedKey(ClassKeyPath, LocalServerKeyPath, comServerCommandLine);
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

			if (!owned && !matchesCommand)
			{
				return false;
			}

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
