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
    private readonly DiffEngine _diffEngine = new();
    private readonly ClipDiffSettingsStore _settingsStore;
    private readonly ExternalDiffLauncher _externalDiffLauncher;
    private readonly NativeMessageWindow _messageWindow;
    private readonly ClipboardMonitor _clipboardMonitor;
    private readonly ClipboardWriter _clipboardWriter;
    private readonly CopiedFileTextReader _copiedFileTextReader = new();
    private readonly ClipboardHistory _history;
    private readonly GlobalHotKey _hotKey;
    private readonly TrayIconController _trayIcon;
    private readonly DiffWindowViewModel _viewModel;
    private readonly ExplorerCommandServer _explorerCommandServer;
    private readonly ExplorerDropTargetServer _explorerDropTargetServer;
    private readonly ExplorerContextMenuRegistration _explorerContextMenuRegistration;
    private readonly CancellationTokenSource _shutdown = new();
    private ClipDiffSettings _settings;
    private IReadOnlyList<ExternalDiffToolChoice> _externalDiffTools;
    private DiffWindow? _diffWindow;
    private AboutWindow? _aboutWindow;
    private ShortcutWindow? _shortcutWindow;
    private bool _disposed;

    public AppController()
    {
        _messageWindow = new NativeMessageWindow();
        _clipboardMonitor = new ClipboardMonitor(_messageWindow);
        _clipboardWriter = new ClipboardWriter(_messageWindow.Handle);
        _history = new ClipboardHistory(_clipboardMonitor.BaselineSequence);
        _settingsStore = new ClipDiffSettingsStore();
        _settings = _settingsStore.Load();
        _hotKey = new GlobalHotKey(_messageWindow, HotKeyGesture.Normalize(_settings.HotKey));
        _externalDiffTools = ExternalDiffToolDiscovery.FindInstalled(_settings.SelectedExecutablePath);
        _externalDiffLauncher = new ExternalDiffLauncher();
        _trayIcon = new TrayIconController(
            _externalDiffTools,
            GetSelectedExternalDiffTool()?.ExecutablePath);
        _viewModel = new DiffWindowViewModel(CopyDiff, ClearCapturedText);
        _explorerCommandServer = new ExplorerCommandServer(CompareWithSelectedFileAsync);
        _explorerDropTargetServer = new ExplorerDropTargetServer(OnExplorerFilesSelected);
        _explorerContextMenuRegistration = new ExplorerContextMenuRegistration();

        _clipboardMonitor.ObservationReceived += OnClipboardObservation;
        _hotKey.Pressed += OnShowDiffRequested;
        _trayIcon.ShowDiffRequested += OnShowDiffRequested;
        _trayIcon.ShortcutRequested += OnShortcutRequested;
        _trayIcon.ToggleMonitoringRequested += OnToggleMonitoringRequested;
        _trayIcon.DiffToolSelected += OnDiffToolSelected;
        _trayIcon.ChooseDiffToolRequested += OnChooseDiffToolRequested;
        _trayIcon.ClearRequested += OnClearRequested;
        _trayIcon.AboutRequested += OnAboutRequested;
        _trayIcon.QuitRequested += OnQuitRequested;
        UpdatePresentation();
    }

    public void CompareWithCurrent(string selectedFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selectedFilePath);

        if (!_disposed)
        {
            _ = CompareWithSelectedFileAsync(selectedFilePath);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _shutdown.Cancel();
        _clipboardMonitor.ObservationReceived -= OnClipboardObservation;
        _hotKey.Pressed -= OnShowDiffRequested;
        _trayIcon.ShowDiffRequested -= OnShowDiffRequested;
        _trayIcon.ShortcutRequested -= OnShortcutRequested;
        _trayIcon.ToggleMonitoringRequested -= OnToggleMonitoringRequested;
        _trayIcon.DiffToolSelected -= OnDiffToolSelected;
        _trayIcon.ChooseDiffToolRequested -= OnChooseDiffToolRequested;
        _trayIcon.ClearRequested -= OnClearRequested;
        _trayIcon.AboutRequested -= OnAboutRequested;
        _trayIcon.QuitRequested -= OnQuitRequested;

        _explorerContextMenuRegistration.Dispose();
        _explorerDropTargetServer.Dispose();
        _explorerCommandServer.Dispose();
        _trayIcon.Dispose();
        _externalDiffLauncher.Dispose();
        _hotKey.Dispose();
        _clipboardMonitor.Dispose();
        if (_shortcutWindow is not null)
        {
            _shortcutWindow.Close();
            _shortcutWindow = null;
        }

        if (_diffWindow is not null)
        {
            _diffWindow.AllowClose = true;
            _diffWindow.Close();
            _diffWindow = null;
        }

        if (_aboutWindow is not null)
        {
            _aboutWindow.AllowClose = true;
            _aboutWindow.Close();
            _aboutWindow = null;
        }

        _viewModel.ClearDocument();
        _history.Clear();
        _messageWindow.Dispose();
        _shutdown.Dispose();
    }

    private void OnClipboardObservation(object? sender, ClipboardObservation observation)
    {
        var change = _history.Apply(observation);
        if (change == ClipboardHistoryChange.RemovedByRecentClear)
        {
            _viewModel.ClearDocument();
        }

        UpdatePresentation();
    }

    private void OnShowDiffRequested(object? sender, EventArgs args) => ShowDiff();

    private void OnToggleMonitoringRequested(object? sender, EventArgs args)
    {
        if (_history.IsMonitoring)
        {
            _clipboardMonitor.Pause();
            _history.Pause();
        }
        else
        {
            _history.Resume(_clipboardMonitor.Resume());
        }

        UpdatePresentation();
    }

    private void OnClearRequested(object? sender, EventArgs args) => ClearCapturedText();

    private void OnAboutRequested(object? sender, EventArgs args)
    {
        _aboutWindow ??= new AboutWindow();
        if (_aboutWindow.WindowState == WindowState.Minimized)
        {
            _aboutWindow.WindowState = WindowState.Normal;
        }

        _aboutWindow.Show();
        _aboutWindow.Activate();
    }

    private void OnShortcutRequested(object? sender, EventArgs args)
    {
        if (_shortcutWindow is not null)
        {
            _shortcutWindow.Activate();
            return;
        }

        var window = new ShortcutWindow(_hotKey.Gesture, TryChangeHotKey);
        _shortcutWindow = window;
        try
        {
            window.ShowDialog();
        }
        finally
        {
            if (ReferenceEquals(_shortcutWindow, window))
            {
                _shortcutWindow = null;
            }
        }
    }

    private HotKeyChangeResult TryChangeHotKey(HotKeyGesture gesture)
    {
        if (_disposed)
        {
            return HotKeyChangeResult.Unavailable;
        }

        var previousGesture = _hotKey.Gesture;
        if (!_hotKey.TryChange(gesture))
        {
            return HotKeyChangeResult.Unavailable;
        }

        var updatedSettings = _settings with { HotKey = gesture };
        if (!_settingsStore.TrySave(updatedSettings))
        {
            _hotKey.TryChange(previousGesture);
            UpdatePresentation();
            return HotKeyChangeResult.SaveFailed;
        }

        _settings = updatedSettings;
        UpdatePresentation();
        return HotKeyChangeResult.Success;
    }

    private void OnDiffToolSelected(object? sender, ExternalDiffToolSelectedEventArgs args)
    {
        _settings = _settings with
        {
            SelectedExecutablePath = args.Choice?.ExecutablePath
        };
        _settingsStore.TrySave(_settings);
        _trayIcon.SetDiffTools(_externalDiffTools, args.Choice?.ExecutablePath);
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
        _externalDiffTools = _externalDiffTools
            .Where(existing => !string.Equals(existing.ExecutablePath, choice.ExecutablePath, StringComparison.OrdinalIgnoreCase))
            .Append(choice)
            .ToArray();
        OnDiffToolSelected(this, new ExternalDiffToolSelectedEventArgs(choice));
    }

    private void OnQuitRequested(object? sender, EventArgs args)
    {
        Dispose();
        System.Windows.Application.Current.Shutdown();
    }

    private void ShowDiff()
    {
        if (_history.Previous is not { } previous || _history.Current is not { } current)
        {
            SystemSounds.Beep.Play();
            return;
        }

        var selectedTool = GetSelectedExternalDiffTool();
        if (selectedTool is not null && ConfirmExternalDiffRisk() &&
            _externalDiffLauncher.TryLaunch(selectedTool, previous, current))
        {
            return;
        }

        ShowBuiltInDiff(previous, current);
    }

    private void ShowBuiltInDiff(ClipboardEntry previous, ClipboardEntry current)
    {
        _viewModel.Load(_diffEngine.Compare(previous, current));
        _diffWindow ??= new DiffWindow { DataContext = _viewModel };
        if (_diffWindow.WindowState == WindowState.Minimized)
        {
            _diffWindow.WindowState = WindowState.Normal;
        }

        _diffWindow.Show();
        _diffWindow.Activate();
    }

    private async Task CompareWithSelectedFileAsync(string selectedFilePath)
    {
        try
        {
            var dispatcher = System.Windows.Application.Current.Dispatcher;
            var canCompare = await dispatcher.InvokeAsync(
                () => !_disposed && _history.IsMonitoring && _history.Current is not null).Task.ConfigureAwait(false);
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

            var selectedValue = await _copiedFileTextReader.ReadFileAsync(
                fullPath,
                _shutdown.Token).ConfigureAwait(false);
            if (selectedValue is null)
            {
                await dispatcher.InvokeAsync(SystemSounds.Beep.Play).Task.ConfigureAwait(false);
                return;
            }

            await dispatcher.InvokeAsync(() =>
            {
                if (_disposed)
                {
                    return;
                }

                if (!_history.IsMonitoring || _history.Current is null)
                {
                    SystemSounds.Beep.Play();
                    return;
                }

                _history.AcceptDirectText(
                    selectedValue.Text,
                    DateTimeOffset.Now,
                    selectedValue.FileName,
                    selectedValue.FilePath);
                UpdatePresentation();
                ShowDiff();
            }).Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_disposed)
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
                () => !_disposed && _history.IsMonitoring).Task.ConfigureAwait(false);
            if (!canCompare)
            {
                await dispatcher.InvokeAsync(SystemSounds.Beep.Play).Task.ConfigureAwait(false);
                return;
            }

            var selectedValues = await _copiedFileTextReader.ReadValuesAsync(
                [previousFilePath, currentFilePath],
                _shutdown.Token).ConfigureAwait(false);
            if (selectedValues.Count != 2)
            {
                await dispatcher.InvokeAsync(SystemSounds.Beep.Play).Task.ConfigureAwait(false);
                return;
            }

            await dispatcher.InvokeAsync(() =>
            {
                if (_disposed)
                {
                    return;
                }

                if (!_history.IsMonitoring)
                {
                    SystemSounds.Beep.Play();
                    return;
                }

                var previous = selectedValues[0];
                var current = selectedValues[1];
                _history.AcceptDirectPair(
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
        catch (OperationCanceledException) when (_disposed)
        {
        }
    }

    private ExternalDiffToolChoice? GetSelectedExternalDiffTool()
    {
        if (string.IsNullOrWhiteSpace(_settings.SelectedExecutablePath))
        {
            return null;
        }

        return _externalDiffTools.FirstOrDefault(choice => string.Equals(
            choice.ExecutablePath,
            _settings.SelectedExecutablePath,
            StringComparison.OrdinalIgnoreCase));
    }

    private bool ConfirmExternalDiffRisk()
    {
        if (_settings.PlaintextWarningAcknowledged)
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

        _settings = _settings with { PlaintextWarningAcknowledged = true };
        _settingsStore.TrySave(_settings);
        return true;
    }

    private void CopyDiff()
    {
        if (_viewModel.Document is not { } document)
        {
            SystemSounds.Beep.Play();
            return;
        }

        var text = DiffFormatting.Unified(document);
        if (!_clipboardWriter.TryWriteProtectedText(text, out var sequenceNumber))
        {
            SystemSounds.Beep.Play();
            return;
        }

        _clipboardMonitor.SuppressOwnWrite(sequenceNumber);
        _history.Apply(ClipboardObservation.OwnWrite(sequenceNumber, DateTimeOffset.Now));
        UpdatePresentation();
    }

    private void ClearCapturedText()
    {
        _history.Clear();
        _viewModel.ClearDocument();
        UpdatePresentation();
    }

    private void UpdatePresentation()
    {
        var status = _clipboardMonitor.IsRegistered || !_history.IsMonitoring
            ? _history.Status
            : "Clipboard listener unavailable";
        _trayIcon.Update(
            status,
            _hotKey.IsRegistered,
            _hotKey.Gesture.DisplayText,
            _history.IsMonitoring,
            _history.Current,
            _history.Previous);
        _viewModel.SetCanClear(_history.Current is not null);
        _explorerContextMenuRegistration.SetState(
            _history.IsMonitoring,
            _history.Current is not null,
            _explorerDropTargetServer.IsRegistered,
            _history.Current?.SourceFileName);
    }
}
