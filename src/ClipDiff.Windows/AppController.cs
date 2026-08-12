using System.Media;
using System.Windows;
using ClipDiff.Windows.Clipboard;
using ClipDiff.Windows.Hotkeys;
using ClipDiff.Windows.Native;
using ClipDiff.Windows.Tray;
using ClipDiff.Windows.ViewModels;
using ClipDiff.Windows.Views;

namespace ClipDiff.Windows;

internal sealed class AppController : IDisposable
{
    private readonly DiffEngine _diffEngine = new();
    private readonly NativeMessageWindow _messageWindow;
    private readonly ClipboardMonitor _clipboardMonitor;
    private readonly ClipboardWriter _clipboardWriter;
    private readonly ClipboardHistory _history;
    private readonly GlobalHotKey _hotKey;
    private readonly TrayIconController _trayIcon;
    private readonly DiffWindowViewModel _viewModel;
    private DiffWindow? _diffWindow;
    private bool _disposed;

    public AppController()
    {
        _messageWindow = new NativeMessageWindow();
        _clipboardMonitor = new ClipboardMonitor(_messageWindow);
        _clipboardWriter = new ClipboardWriter(_messageWindow.Handle);
        _history = new ClipboardHistory(_clipboardMonitor.BaselineSequence);
        _hotKey = new GlobalHotKey(_messageWindow);
        _trayIcon = new TrayIconController();
        _viewModel = new DiffWindowViewModel(CopyDiff, ClearCapturedText);

        _clipboardMonitor.ObservationReceived += OnClipboardObservation;
        _hotKey.Pressed += OnShowDiffRequested;
        _trayIcon.ShowDiffRequested += OnShowDiffRequested;
        _trayIcon.ToggleMonitoringRequested += OnToggleMonitoringRequested;
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
        _trayIcon.ClearRequested -= OnClearRequested;
        _trayIcon.QuitRequested -= OnQuitRequested;

        _trayIcon.Dispose();
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

        _viewModel.Load(_diffEngine.Compare(previous, current));
        _diffWindow ??= new DiffWindow { DataContext = _viewModel };
        if (_diffWindow.WindowState == WindowState.Minimized)
        {
            _diffWindow.WindowState = WindowState.Normal;
        }

        _diffWindow.Show();
        _diffWindow.Activate();
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
