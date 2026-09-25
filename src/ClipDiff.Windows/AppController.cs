using System.IO;
using System.Media;
using System.Windows;
using ClipDiff.Windows.Clipboard;
using ClipDiff.Windows.ExternalDiff;
using ClipDiff.Windows.Explorer;
using ClipDiff.Windows.Hotkeys;
using ClipDiff.Windows.Native;
using ClipDiff.Windows.Settings;
using ClipDiff.Windows.Tray;
using ClipDiff.Windows.ViewModels;
using ClipDiff.Windows.Views;

namespace ClipDiff.Windows;

internal sealed class AppController : IDisposable
{
    private readonly DiffEngine diffEngine = new();
    private readonly LatestComparison comparison = new();
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
    private readonly CancellationTokenSource shutdown = new();
    private ClipDiffSettings settings;
    private IReadOnlyList<ExternalDiffToolChoice> externalDiffTools;
    private DiffWindow? diffWindow;
    private AboutWindow? aboutWindow;
    private ShortcutWindow? shortcutWindow;
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
        viewModel = new DiffWindowViewModel(CopyDiff, ClearCapturedText, Recompare);
        explorerCommandServer = new ExplorerCommandServer(CompareWithSelectedFileAsync);
        explorerDropTargetServer = new ExplorerDropTargetServer(
            OnExplorerFilesSelected,
            () => !disposed && history.IsMonitoring);
        explorerContextMenuRegistration = new ExplorerContextMenuRegistration();

        clipboardMonitor.ObservationReceived += OnClipboardObservation;
        hotKey.Pressed += OnHotKeyPressed;
        trayIcon.ShowDiffRequested += OnShowDiffRequested;
        trayIcon.ShortcutRequested += OnShortcutRequested;
        trayIcon.ToggleMonitoringRequested += OnToggleMonitoringRequested;
        trayIcon.DiffToolSelected += OnDiffToolSelected;
        trayIcon.ChooseDiffToolRequested += OnChooseDiffToolRequested;
        trayIcon.ClearRequested += OnClearRequested;
        trayIcon.AboutRequested += OnAboutRequested;
        trayIcon.QuitRequested += OnQuitRequested;
        UpdatePresentation();
    }

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
        shutdown.Cancel();
        clipboardMonitor.ObservationReceived -= OnClipboardObservation;
        hotKey.Pressed -= OnHotKeyPressed;
        trayIcon.ShowDiffRequested -= OnShowDiffRequested;
        trayIcon.ShortcutRequested -= OnShortcutRequested;
        trayIcon.ToggleMonitoringRequested -= OnToggleMonitoringRequested;
        trayIcon.DiffToolSelected -= OnDiffToolSelected;
        trayIcon.ChooseDiffToolRequested -= OnChooseDiffToolRequested;
        trayIcon.ClearRequested -= OnClearRequested;
        trayIcon.AboutRequested -= OnAboutRequested;
        trayIcon.QuitRequested -= OnQuitRequested;

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
        shutdown.Dispose();
    }

    private void OnClipboardObservation(object? sender, ClipboardObservation observation)
    {
        var change = history.Apply(observation);
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

    private void OnToggleMonitoringRequested(object? sender, EventArgs args)
    {
        if (history.IsMonitoring)
        {
            clipboardMonitor.Pause();
            history.Pause();
        }
        else
        {
            history.Resume(clipboardMonitor.Resume());
        }

        UpdatePresentation();
    }

    private void OnClearRequested(object? sender, EventArgs args) => ClearCapturedText();

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
        settingsStore.TrySave(settings);
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
        if (history.Previous is not { } previous || history.Current is not { } current)
        {
            SystemSounds.Beep.Play();
            return;
        }

        comparison.Cancel();
        var selectedTool = GetSelectedExternalDiffTool();
        if (selectedTool is not null && ConfirmExternalDiffRisk() &&
            externalDiffLauncher.TryLaunch(selectedTool, previous, current))
        {
            return;
        }

        ShowBuiltInDiff(previous, current);
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

    private void ClearComparison()
    {
        comparison.Cancel();
        comparisonInput = null;
    }

    private async Task CompareWithSelectedFileAsync(string selectedFilePath)
    {
        try
        {
            var dispatcher = System.Windows.Application.Current.Dispatcher;
            var canCompare = await dispatcher.InvokeAsync(
                () => !disposed && history.IsMonitoring && history.Current is not null).Task.ConfigureAwait(false);
            if (!canCompare)
            {
                await dispatcher.InvokeAsync(SystemSounds.Beep.Play).Task.ConfigureAwait(false);
                return;
            }

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(selectedFilePath);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or
                                              PathTooLongException or System.Security.SecurityException)
            {
                await dispatcher.InvokeAsync(SystemSounds.Beep.Play).Task.ConfigureAwait(false);
                return;
            }

            var selectedValue = await copiedFileTextReader.ReadFileAsync(
                fullPath,
                shutdown.Token).ConfigureAwait(false);
            if (selectedValue is null)
            {
                await dispatcher.InvokeAsync(SystemSounds.Beep.Play).Task.ConfigureAwait(false);
                return;
            }

            await dispatcher.InvokeAsync(() =>
            {
                if (disposed)
                {
                    return;
                }

                if (!history.IsMonitoring || history.Current is null)
                {
                    SystemSounds.Beep.Play();
                    return;
                }

                history.AcceptDirectText(
                    selectedValue.Text,
                    DateTimeOffset.Now,
                    selectedValue.FileName,
                    selectedValue.FilePath);
                UpdatePresentation();
                ShowDiff();
            }).Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (disposed)
        {
        }
    }

    private void OnExplorerFilesSelected(IReadOnlyList<string> selectedFilePaths) =>
        _ = CompareSelectedFilesAsync(selectedFilePaths);

    private async Task CompareSelectedFilesAsync(IReadOnlyList<string> selectedFilePaths)
    {
        if (!ExplorerFileSelection.TryGetPair(
                selectedFilePaths,
                out var previousFilePath,
                out var currentFilePath))
        {
            SystemSounds.Beep.Play();
            return;
        }

        try
        {
            var dispatcher = System.Windows.Application.Current.Dispatcher;
            var canCompare = await dispatcher.InvokeAsync(
                () => !disposed && history.IsMonitoring).Task.ConfigureAwait(false);
            if (!canCompare)
            {
                await dispatcher.InvokeAsync(SystemSounds.Beep.Play).Task.ConfigureAwait(false);
                return;
            }

            var selectedValues = await copiedFileTextReader.ReadValuesAsync(
                [previousFilePath, currentFilePath],
                shutdown.Token).ConfigureAwait(false);
            if (selectedValues.Count != 2)
            {
                await dispatcher.InvokeAsync(SystemSounds.Beep.Play).Task.ConfigureAwait(false);
                return;
            }

            await dispatcher.InvokeAsync(() =>
            {
                if (disposed)
                {
                    return;
                }

                if (!history.IsMonitoring)
                {
                    SystemSounds.Beep.Play();
                    return;
                }

                var previous = selectedValues[0];
                var current = selectedValues[1];
                history.AcceptDirectPair(
                    previous.Text,
                    current.Text,
                    DateTimeOffset.Now,
                    previous.FileName,
                    current.FileName,
                    previous.FilePath,
                    current.FilePath);
                UpdatePresentation();
                ShowDiff();
            }).Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (disposed)
        {
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
        settingsStore.TrySave(settings);
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

    private void ClearCapturedText()
    {
        ClearComparison();
        history.Clear();
        viewModel.ClearDocument();
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
            history.IsMonitoring,
            history.Current,
            history.Previous);
        viewModel.SetCanClear(history.Current is not null);
        explorerContextMenuRegistration.SetState(
            history.IsMonitoring,
            history.Current is not null,
            explorerDropTargetServer.IsRegistered,
            history.Current?.SourceFileName);
    }
}
