using System.IO;
using System.Media;
using System.Windows;
using ClipDiff.Windows.Clipboard;
using ClipDiff.Windows.ExternalDiff;
using ClipDiff.Windows.Hotkeys;
using ClipDiff.Windows.Native;
using ClipDiff.Windows.Tray;
using ClipDiff.Windows.ViewModels;
using ClipDiff.Windows.Views;

namespace ClipDiff.Windows;

internal sealed class AppController : IDisposable
{
    private readonly DiffEngine _diffEngine = new();
    private readonly ExternalDiffSettingsStore _externalDiffSettingsStore;
    private readonly ExternalDiffLauncher _externalDiffLauncher;
    private readonly NativeMessageWindow _messageWindow;
    private readonly ClipboardMonitor _clipboardMonitor;
    private readonly ClipboardWriter _clipboardWriter;
    private readonly ClipboardHistory _history;
    private readonly GlobalHotKey _hotKey;
    private readonly TrayIconController _trayIcon;
    private readonly DiffWindowViewModel _viewModel;
    private ExternalDiffSettings _externalDiffSettings;
    private IReadOnlyList<ExternalDiffToolChoice> _externalDiffTools;
    private DiffWindow? _diffWindow;
    private bool _disposed;

    public AppController()
    {
        _messageWindow = new NativeMessageWindow();
        _clipboardMonitor = new ClipboardMonitor(_messageWindow);
        _clipboardWriter = new ClipboardWriter(_messageWindow.Handle);
        _history = new ClipboardHistory(_clipboardMonitor.BaselineSequence);
        _hotKey = new GlobalHotKey(_messageWindow);
        _externalDiffSettingsStore = new ExternalDiffSettingsStore();
        _externalDiffSettings = _externalDiffSettingsStore.Load();
        _externalDiffTools = ExternalDiffToolDiscovery.FindInstalled(_externalDiffSettings.SelectedExecutablePath);
        _externalDiffLauncher = new ExternalDiffLauncher();
        _trayIcon = new TrayIconController(
            _externalDiffTools,
            GetSelectedExternalDiffTool()?.ExecutablePath);
        _viewModel = new DiffWindowViewModel(CopyDiff, ClearCapturedText);

        _clipboardMonitor.ObservationReceived += OnClipboardObservation;
        _hotKey.Pressed += OnShowDiffRequested;
        _trayIcon.ShowDiffRequested += OnShowDiffRequested;
        _trayIcon.ToggleMonitoringRequested += OnToggleMonitoringRequested;
        _trayIcon.DiffToolSelected += OnDiffToolSelected;
        _trayIcon.ChooseDiffToolRequested += OnChooseDiffToolRequested;
        _trayIcon.ClearRequested += OnClearRequested;
        _trayIcon.QuitRequested += OnQuitRequested;
        UpdatePresentation();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _clipboardMonitor.ObservationReceived -= OnClipboardObservation;
        _hotKey.Pressed -= OnShowDiffRequested;
        _trayIcon.ShowDiffRequested -= OnShowDiffRequested;
        _trayIcon.ToggleMonitoringRequested -= OnToggleMonitoringRequested;
        _trayIcon.DiffToolSelected -= OnDiffToolSelected;
        _trayIcon.ChooseDiffToolRequested -= OnChooseDiffToolRequested;
        _trayIcon.ClearRequested -= OnClearRequested;
        _trayIcon.QuitRequested -= OnQuitRequested;

        _trayIcon.Dispose();
        _externalDiffLauncher.Dispose();
        _hotKey.Dispose();
        _clipboardMonitor.Dispose();
        if (_diffWindow is not null)
        {
            _diffWindow.AllowClose = true;
            _diffWindow.Close();
            _diffWindow = null;
        }

        _viewModel.ClearDocument();
        _history.Clear();
        _messageWindow.Dispose();
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

    private void OnDiffToolSelected(object? sender, ExternalDiffToolSelectedEventArgs args)
    {
        _externalDiffSettings = _externalDiffSettings with
        {
            SelectedExecutablePath = args.Choice?.ExecutablePath
        };
        _externalDiffSettingsStore.TrySave(_externalDiffSettings);
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

    private ExternalDiffToolChoice? GetSelectedExternalDiffTool()
    {
        if (string.IsNullOrWhiteSpace(_externalDiffSettings.SelectedExecutablePath))
        {
            return null;
        }

        return _externalDiffTools.FirstOrDefault(choice => string.Equals(
            choice.ExecutablePath,
            _externalDiffSettings.SelectedExecutablePath,
            StringComparison.OrdinalIgnoreCase));
    }

    private bool ConfirmExternalDiffRisk()
    {
        if (_externalDiffSettings.PlaintextWarningAcknowledged)
        {
            return true;
        }

        var result = NativeMethods.ShowMessageBox(
            nint.Zero,
            "External diff programs require ClipDiff to write the previous and current clipboard text to " +
            "read-only plaintext files under your local ClipDiff temporary folder. Clipboard text may contain " +
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

        _externalDiffSettings = _externalDiffSettings with { PlaintextWarningAcknowledged = true };
        _externalDiffSettingsStore.TrySave(_externalDiffSettings);
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
            _history.IsMonitoring,
            _history.Current,
            _history.Previous);
        _viewModel.SetCanClear(_history.Current is not null);
    }
}
