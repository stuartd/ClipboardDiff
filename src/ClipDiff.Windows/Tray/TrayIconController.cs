using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace ClipDiff.Windows.Tray;

internal sealed class TrayIconController : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ContextMenuStrip _menu;
    private readonly Forms.ToolStripMenuItem _statusItem;
    private readonly Forms.ToolStripMenuItem _shortcutItem;
    private readonly Forms.ToolStripMenuItem _currentItem;
    private readonly Forms.ToolStripMenuItem _previousItem;
    private readonly Forms.ToolStripMenuItem _showDiffItem;
    private readonly Forms.ToolStripMenuItem _monitorItem;
    private readonly Forms.ToolStripMenuItem _clearItem;
    private bool _disposed;

    public TrayIconController()
    {
        _statusItem = DisabledItem("Waiting for copied text");
        _shortcutItem = DisabledItem("Shortcut: Ctrl+Alt+D");
        _currentItem = DisabledItem("Current: None");
        _previousItem = DisabledItem("Previous: None");
        _showDiffItem = new Forms.ToolStripMenuItem("Show Diff");
        _monitorItem = new Forms.ToolStripMenuItem("Monitor Clipboard") { CheckOnClick = false };
        _clearItem = new Forms.ToolStripMenuItem("Clear Captured Text");
        var quitItem = new Forms.ToolStripMenuItem("Quit ClipDiff");

        _showDiffItem.Click += (_, _) => ShowDiffRequested?.Invoke(this, EventArgs.Empty);
        _monitorItem.Click += (_, _) => ToggleMonitoringRequested?.Invoke(this, EventArgs.Empty);
        _clearItem.Click += (_, _) => ClearRequested?.Invoke(this, EventArgs.Empty);
        quitItem.Click += (_, _) => QuitRequested?.Invoke(this, EventArgs.Empty);

        _menu = new Forms.ContextMenuStrip();
        _menu.Items.AddRange(
        [
            _statusItem,
            _shortcutItem,
            _currentItem,
            _previousItem,
            new Forms.ToolStripSeparator(),
            _showDiffItem,
            _monitorItem,
            _clearItem,
            new Forms.ToolStripSeparator(),
            quitItem
        ]);

        _notifyIcon = new Forms.NotifyIcon
        {
            Text = "ClipDiff",
            Icon = Drawing.SystemIcons.Application,
            ContextMenuStrip = _menu,
            Visible = true
        };
        _notifyIcon.DoubleClick += OnDoubleClick;
    }

    public event EventHandler? ShowDiffRequested;

    public event EventHandler? ToggleMonitoringRequested;

    public event EventHandler? ClearRequested;

    public event EventHandler? QuitRequested;

    public void Update(
        string status,
        bool hotKeyAvailable,
        bool monitoring,
        ClipboardEntry? current,
        ClipboardEntry? previous)
    {
        _statusItem.Text = status;
        _shortcutItem.Text = hotKeyAvailable ? "Shortcut: Ctrl+Alt+D" : "Shortcut unavailable";
        _currentItem.Text = "Current: " + (current is null ? "None" : TextLines.Preview(current.Text));
        _previousItem.Text = "Previous: " + (previous is null ? "None" : TextLines.Preview(previous.Text));
        _showDiffItem.Enabled = current is not null && previous is not null;
        _monitorItem.Checked = monitoring;
        _clearItem.Enabled = current is not null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.DoubleClick -= OnDoubleClick;
        _notifyIcon.Dispose();
        _menu.Dispose();
    }

    private static Forms.ToolStripMenuItem DisabledItem(string text) => new(text) { Enabled = false };

    private void OnDoubleClick(object? sender, EventArgs args)
    {
        if (_showDiffItem.Enabled)
        {
            ShowDiffRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
