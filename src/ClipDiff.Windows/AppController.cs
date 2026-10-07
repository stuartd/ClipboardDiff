using System.IO;
using System.Media;
using System.Windows;
using ClipDiff.Windows.Clipboard;
using ClipDiff.Windows.ExternalDiff;
using ClipDiff.Windows.Explorer;
using ClipDiff.Windows.Hotkeys;
using ClipDiff.Windows.Native;
using ClipDiff.Windows.Settings;
using ClipDiff.Windows.Startup;
using ClipDiff.Windows.Tray;
using ClipDiff.Windows.ViewModels;
using ClipDiff.Windows.Views;

namespace ClipDiff.Windows;

internal sealed class AppController : IDisposable
{
	private readonly DiffEngine diffEngine = new();
	private readonly LatestComparison comparison = new();
	private readonly ComparisonPresentation comparisonPresentation = new();
	private readonly ExplorerComparisonRequests explorerComparisonRequests;
	private (ClipboardEntry Previous, ClipboardEntry Current, DiffSideLabels Labels)? comparisonInput;
	private readonly ClipDiffSettingsStore settingsStore;
	private readonly ExternalDiffLauncher externalDiffLauncher;
	private readonly NativeMessageWindow messageWindow;
	private readonly ClipboardMonitor clipboardMonitor;
	private readonly ClipboardWriter clipboardWriter;
	private readonly CopiedFileTextReader copiedFileTextReader = new();
	private readonly ClipboardHistory history;
	private readonly GlobalHotKey hotKey;
	private readonly TrayIconController trayIcon;
	private readonly DiffWindowViewModel viewModel;
	private readonly ExplorerCommandServer explorerCommandServer;
	private readonly ExplorerDropTargetServer explorerDropTargetServer;
	private readonly ExplorerContextMenuRegistration explorerContextMenuRegistration;
	private ClipDiffSettings settings;
	private IReadOnlyList<ExternalDiffToolChoice> externalDiffTools;
	private DiffWindow? diffWindow;
	private AboutWindow? aboutWindow;
	private ShortcutWindow? shortcutWindow;
	private readonly StartupRegistration startupRegistration = new();
	private bool disposed;

	public AppController()
	{
		messageWindow = new NativeMessageWindow();
		clipboardMonitor = new ClipboardMonitor(messageWindow);
		clipboardWriter = new ClipboardWriter(messageWindow.Handle);
		history = new ClipboardHistory(clipboardMonitor.BaselineSequence);
		settingsStore = new ClipDiffSettingsStore();
		settings = settingsStore.Load();
		hotKey = new GlobalHotKey(new NativeHotKeyBackend(messageWindow), HotKeyGesture.Normalize(settings.HotKey));
		externalDiffTools = ExternalDiffToolDiscovery.FindInstalled(settings.SelectedExecutablePath);
		externalDiffLauncher = new ExternalDiffLauncher();
		trayIcon = new TrayIconController(
			externalDiffTools,
			GetSelectedExternalDiffTool()?.ExecutablePath);
		viewModel = new DiffWindowViewModel(CopyDiff, Recompare);
		explorerComparisonRequests = new ExplorerComparisonRequests(
			history,
			ReadSelectedFilesAsync,
			CancelPendingComparison,
			() =>
			{
				UpdatePresentation();
				ShowDiff();
			},
			SystemSounds.Beep.Play);
		explorerCommandServer = new ExplorerCommandServer(CompareWithSelectedFileAsync);
		explorerDropTargetServer = new ExplorerDropTargetServer(
			OnExplorerFilesSelected,
			() => !disposed && history.IsMonitoring);
		explorerContextMenuRegistration = new ExplorerContextMenuRegistration();

		clipboardMonitor.ObservationReceived += OnClipboardObservation;
		hotKey.Pressed += OnHotKeyPressed;
		trayIcon.ShowDiffRequested += OnShowDiffRequested;
		trayIcon.ShortcutRequested += OnShortcutRequested;
		trayIcon.DiffToolSelected += OnDiffToolSelected;
		trayIcon.ChooseDiffToolRequested += OnChooseDiffToolRequested;
		trayIcon.AboutRequested += OnAboutRequested;
		trayIcon.QuitRequested += OnQuitRequested;
		trayIcon.ToggleStartAtLoginRequested += OnToggleStartAtLoginRequested;
		trayIcon.MenuOpening += OnTrayMenuOpening;
		UpdatePresentation();
	}

	public void InitializeStartAtLogin()
	{
		if (disposed)
		{
			return;
		}

		// Follow a portable upgrade to a new directory only when already opted in.
		if (startupRegistration.TryRead(out var registered) && registered)
		{
			if (!startupRegistration.TrySetEnabled(true))
			{
				ShowStartupError("ClipDiff could not update its login registration to this location.");
			}

			RememberStartupPrompt();
		}
		else if (!settings.StartupPromptShown)
		{
			var answer = System.Windows.MessageBox.Show(
				"Start ClipDiff automatically when you sign in to Windows?\n\n" +
				"You can change this later using Start at login in the tray menu.",
				"Start ClipDiff at login",
				MessageBoxButton.YesNo,
				MessageBoxImage.Question,
				MessageBoxResult.No);

			if (answer == MessageBoxResult.Yes && !startupRegistration.TrySetEnabled(true))
			{
				ShowStartupError("ClipDiff could not enable start at login. You can try again from the tray menu.");
			}

			RememberStartupPrompt();
		}

		RefreshStartAtLogin();
	}

	private void RememberStartupPrompt()
	{
		if (settings.StartupPromptShown)
		{
			return;
		}

		settings = settings with { StartupPromptShown = true };

		if (!settingsStore.TrySave(settings))
		{
			ShowStartupError("ClipDiff could not save your answer. The startup question may appear next time.");
		}
	}

	private void OnTrayMenuOpening(object? sender, EventArgs args)
	{
		RefreshStartAtLogin();
		UpdatePresentation();
	}

	private void RefreshStartAtLogin()
	{
		var available = startupRegistration.TryRead(out var registered);
		trayIcon.SetStartAtLogin(registered, available);
	}

	private void OnToggleStartAtLoginRequested(object? sender, EventArgs args)
	{
		if (!startupRegistration.TryRead(out var registered) ||
			!startupRegistration.TrySetEnabled(!registered))
		{
			ShowStartupError("ClipDiff could not change start at login.");
		}
		else
		{
			RememberStartupPrompt();
		}

		RefreshStartAtLogin();
	}

	private static void ShowStartupError(string message) =>
		System.Windows.MessageBox.Show(message, "ClipDiff", MessageBoxButton.OK, MessageBoxImage.Warning);

	public void CompareWithCurrent(string selectedFilePath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(selectedFilePath);

		if (!disposed)
		{
			_ = CompareWithSelectedFileAsync(selectedFilePath);
		}
	}

	public void Dispose()
	{
		if (disposed)
		{
			return;
		}

		disposed = true;
		ClearComparison();
		clipboardMonitor.ObservationReceived -= OnClipboardObservation;
		hotKey.Pressed -= OnHotKeyPressed;
		trayIcon.ShowDiffRequested -= OnShowDiffRequested;
		trayIcon.ShortcutRequested -= OnShortcutRequested;
		trayIcon.DiffToolSelected -= OnDiffToolSelected;
		trayIcon.ChooseDiffToolRequested -= OnChooseDiffToolRequested;
		trayIcon.AboutRequested -= OnAboutRequested;
		trayIcon.QuitRequested -= OnQuitRequested;
		trayIcon.ToggleStartAtLoginRequested -= OnToggleStartAtLoginRequested;
		trayIcon.MenuOpening -= OnTrayMenuOpening;

		explorerContextMenuRegistration.Dispose();
		explorerDropTargetServer.Dispose();
		explorerCommandServer.Dispose();
		trayIcon.Dispose();
		externalDiffLauncher.Dispose();
		hotKey.Dispose();
		clipboardMonitor.Dispose();

		if (shortcutWindow is not null)
		{
			shortcutWindow.Close();
			shortcutWindow = null;
		}

		if (diffWindow is not null)
		{
			diffWindow.AllowClose = true;
			diffWindow.Close();
			diffWindow = null;
		}

		if (aboutWindow is not null)
		{
			aboutWindow.AllowClose = true;
			aboutWindow.Close();
			aboutWindow = null;
		}

		viewModel.ClearDocument();
		history.Clear();
		messageWindow.Dispose();
	}

	private void OnClipboardObservation(object? sender, ClipboardObservation observation)
	{
		var change = history.Apply(observation);

		if (change == ClipboardHistoryChange.Accepted)
		{
			explorerComparisonRequests.Cancel();
			CancelPendingComparison();
		}

		if (change == ClipboardHistoryChange.RemovedByRecentClear)
		{
			ClearComparison();
			viewModel.ClearDocument();
		}

		UpdatePresentation();
	}

	private void OnHotKeyPressed(object? sender, EventArgs args)
	{
		if (shortcutWindow?.TryCaptureRegisteredShortcut(hotKey.Gesture) == true)
		{
			return;
		}

		ShowDiff();
	}

	private void OnShowDiffRequested(object? sender, EventArgs args) => ShowDiff();

	private void OnAboutRequested(object? sender, EventArgs args)
	{
		aboutWindow ??= new AboutWindow();

		if (aboutWindow.WindowState == WindowState.Minimized)
		{
			aboutWindow.WindowState = WindowState.Normal;
		}

		aboutWindow.Show();
		aboutWindow.Activate();
	}

	private void OnShortcutRequested(object? sender, EventArgs args)
	{
		if (shortcutWindow is not null)
		{
			shortcutWindow.Activate();
			return;
		}

		var window = new ShortcutWindow(hotKey.Gesture, TryChangeHotKey);
		shortcutWindow = window;
		try
		{
			window.ShowDialog();
		}
		finally
		{
			if (ReferenceEquals(shortcutWindow, window))
			{
				shortcutWindow = null;
			}
		}
	}

	private HotKeyChangeResult TryChangeHotKey(HotKeyGesture gesture)
	{
		if (disposed)
		{
			return HotKeyChangeResult.Unavailable;
		}

		var updatedSettings = settings with { HotKey = gesture };
		var result = HotKeyChangeTransaction.TrySave(
			hotKey, gesture, () => settingsStore.TrySave(updatedSettings));
		if (result == HotKeyChangeResult.Success)
		{
			settings = updatedSettings;
		}

		UpdatePresentation();
		return result;
	}

	private void OnDiffToolSelected(object? sender, ExternalDiffToolSelectedEventArgs args)
	{
		settings = settings with
		{
			SelectedExecutablePath = args.Choice?.ExecutablePath
		};

		if (!settingsStore.TrySave(settings))
		{
			ShowStartupError("ClipDiff could not save the diff viewer selection. It may reset next time.");
		}

		trayIcon.SetDiffTools(externalDiffTools, args.Choice?.ExecutablePath);
	}

	private void OnChooseDiffToolRequested(object? sender, EventArgs args)
	{
		var dialog = new Microsoft.Win32.OpenFileDialog
		{
			Title = "Choose a diff program",
			Filter = "Windows applications (*.exe)|*.exe|All files (*.*)|*.*",
			CheckFileExists = true,
			Multiselect = false
		};

		if (dialog.ShowDialog() != true)
		{
			return;
		}

		var choice = new ExternalDiffToolChoice(
			ExternalDiffToolCatalog.MatchExecutable(dialog.FileName),
			Path.GetFullPath(dialog.FileName));
		externalDiffTools =
		[
			.. externalDiffTools
				.Where(existing => !string.Equals(existing.ExecutablePath, choice.ExecutablePath,
					StringComparison.OrdinalIgnoreCase)),

			choice
		];
		OnDiffToolSelected(this, new ExternalDiffToolSelectedEventArgs(choice));
	}

	private void OnQuitRequested(object? sender, EventArgs args)
	{
		Dispose();
		System.Windows.Application.Current.Shutdown();
	}

	private void ShowDiff()
	{
		if (disposed || history.Previous is not { } previous || history.Current is not { } current)
		{
			SystemSounds.Beep.Play();
			return;
		}

		explorerComparisonRequests.Cancel();
		comparison.Cancel();
		var selectedTool = GetSelectedExternalDiffTool();
		comparisonPresentation.Show(
			selectedTool is null ? null : ConfirmExternalDiffRisk,
			() => externalDiffLauncher.TryLaunch(selectedTool!, previous, current),
			() => ShowBuiltInDiff(previous, current));
	}

	private void ShowBuiltInDiff(ClipboardEntry previous, ClipboardEntry current)
	{
		comparisonInput = (previous with { SourceFilePath = null },
			current with { SourceFilePath = null }, DiffFormatting.Labels(previous, current));
		Recompare();
	}

	private void Recompare()
	{
		if (comparisonInput is not { } input || disposed)
		{
			return;
		}

		_ = CompareBuiltInAsync(input.Previous, input.Current, input.Labels);
	}

	private async Task CompareBuiltInAsync(ClipboardEntry previous, ClipboardEntry current, DiffSideLabels labels)
	{
		var ignoreSpacing = viewModel.IgnoreSpacing;

		try
		{
			await comparison.RunAsync(
				token => Task.Run(() => diffEngine.Compare(previous, current,
					ignoreSpacing: ignoreSpacing, cancellationToken: token) with { Labels = labels }, token),
				document =>
				{
					viewModel.Load(document);
					diffWindow ??= new DiffWindow { DataContext = viewModel };

					if (diffWindow.WindowState == WindowState.Minimized)
					{
						diffWindow.WindowState = WindowState.Normal;
					}

					diffWindow.Show();
					diffWindow.Activate();
				});
		}
		catch (Exception exception) when (exception is InvalidOperationException or OverflowException)
		{
			// Never include input text in diagnostics or user-facing errors.
			if (!disposed)
			{
				SystemSounds.Beep.Play();
			}
		}
	}

	private void CancelPendingComparison()
	{
		comparisonPresentation.Cancel();
		comparison.Cancel();
	}

	private void ClearComparison()
	{
		explorerComparisonRequests.Cancel();
		CancelPendingComparison();
		comparisonInput = null;
	}

	private async ValueTask<IReadOnlyList<CopiedFileText>> ReadSelectedFilesAsync(
		IReadOnlyList<string> paths, CancellationToken cancellationToken)
	{
		// Opening or inspecting a network file can block before ReadFileAsync's
		// first await. Keep that work off the dispatcher and abandon stale waits.
		return await Task.Run(() => copiedFileTextReader.ReadValuesAsync(paths, cancellationToken).AsTask(),
			cancellationToken).WaitAsync(cancellationToken).ConfigureAwait(false);
	}

	private Task CompareWithSelectedFileAsync(string selectedFilePath) =>
		System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
			disposed ? Task.CompletedTask : explorerComparisonRequests.RunAsync([selectedFilePath], true)).Task.Unwrap();

	private void OnExplorerFilesSelected(IReadOnlyList<string> selectedFilePaths)
	{
		if (!ExplorerFileSelection.TryGetPair(selectedFilePaths, out var previousPath, out var currentPath))
		{
			SystemSounds.Beep.Play();
			return;
		}

		if (!disposed)
		{
			_ = explorerComparisonRequests.RunAsync([previousPath, currentPath], false);
		}
	}

	private ExternalDiffToolChoice? GetSelectedExternalDiffTool()
	{
		if (string.IsNullOrWhiteSpace(settings.SelectedExecutablePath))
		{
			return null;
		}

		return externalDiffTools.FirstOrDefault(choice => string.Equals(
			choice.ExecutablePath,
			settings.SelectedExecutablePath,
			StringComparison.OrdinalIgnoreCase));
	}

	private bool ConfirmExternalDiffRisk()
	{
		if (settings.PlaintextWarningAcknowledged)
		{
			return true;
		}

		var result = NativeMethods.ShowMessageBox(
			nint.Zero,
			"External diff programs require ClipDiff to write the previous and current comparison text to " +
			"read-only plaintext files under your local ClipDiff temporary folder. Comparison text may contain " +
			"passwords, tokens, or other secrets.\n\n" +
			"ClipDiff attempts to delete the files after the diff program closes, when ClipDiff exits, and on " +
			"its next start. Files may remain after a crash or power loss, and the chosen program may retain " +
			"its own copies. Continue with the external diff program?",
			"ClipDiff external diff privacy notice",
			NativeMethods.MbOkCancel |
			NativeMethods.MbIconWarning |
			NativeMethods.MbDefButton2 |
			NativeMethods.MbTaskModal |
			NativeMethods.MbSetForeground |
			NativeMethods.MbTopmost);

		if (result != NativeMethods.DialogResultOk)
		{
			return false;
		}

		settings = settings with { PlaintextWarningAcknowledged = true };

		if (!settingsStore.TrySave(settings))
		{
			ShowStartupError("ClipDiff could not save the privacy acknowledgement. The notice may appear next time.");
		}

		return true;
	}

	private void CopyDiff()
	{
		if (viewModel.Document is not { } document)
		{
			SystemSounds.Beep.Play();
			return;
		}

		var text = DiffFormatting.Unified(document);

		if (!clipboardWriter.TryWriteProtectedText(text, out var sequenceNumber))
		{
			SystemSounds.Beep.Play();
			return;
		}

		clipboardMonitor.SuppressOwnWrite(sequenceNumber);
		history.Apply(ClipboardObservation.OwnWrite(sequenceNumber, DateTimeOffset.Now));
		UpdatePresentation();
	}

	private void UpdatePresentation()
	{
		var status = clipboardMonitor.IsRegistered || !history.IsMonitoring
			? history.Status
			: "Clipboard listener unavailable";
		trayIcon.Update(
			status,
			hotKey.IsRegistered,
			hotKey.Gesture.DisplayText,
			history.Current,
			history.Previous);
		explorerContextMenuRegistration.SetState(
			history.IsMonitoring,
			history.Current is not null,
			explorerDropTargetServer.IsRegistered,
			history.Current?.SourceFileName);
	}
}
