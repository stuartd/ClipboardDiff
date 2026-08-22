using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using ClipDiff.Windows.ExternalDiff;

namespace ClipDiff.Windows.Tray;

internal sealed class TrayIconController : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Drawing.Icon? _applicationIcon;
    private readonly Forms.ContextMenuStrip _menu;
    private readonly Forms.ToolStripMenuItem _statusItem;
    private readonly Forms.ToolStripMenuItem _shortcutItem;
    private readonly Forms.ToolStripMenuItem _currentItem;
    private readonly Forms.ToolStripMenuItem _previousItem;
    private readonly Forms.ToolStripMenuItem _showDiffItem;
    private readonly Forms.ToolStripMenuItem _diffViewerItem;
    private readonly Forms.ToolStripMenuItem _monitorItem;
    private readonly Forms.ToolStripMenuItem _clearItem;
    private bool _disposed;

    private IReadOnlyList<ExternalDiffToolChoice> _diffTools = [];
    private string? _selectedDiffExecutablePath;

    public TrayIconController(
        IReadOnlyList<ExternalDiffToolChoice> diffTools,
        string? selectedDiffExecutablePath)
    {
        _statusItem = DisabledItem("Waiting for copied text");
        _shortcutItem = DisabledItem("Shortcut: Ctrl+Alt+D");
        _currentItem = DisabledItem("Current: None");
        _previousItem = DisabledItem("Previous: None");
        _showDiffItem = new Forms.ToolStripMenuItem("Show Diff");
        _diffViewerItem = new Forms.ToolStripMenuItem("Diff viewer");
        _monitorItem = new Forms.ToolStripMenuItem("Monitor Clipboard") { CheckOnClick = false };
        _clearItem = new Forms.ToolStripMenuItem("Clear Captured Text");
        var quitItem = new Forms.ToolStripMenuItem("Quit ClipDiff");

        _showDiffItem.Click += (_, _) => ShowDiffRequested?.Invoke(this, EventArgs.Empty);
        _monitorItem.Click += (_, _) => ToggleMonitoringRequested?.Invoke(this, EventArgs.Empty);
        _clearItem.Click += (_, _) => ClearRequested?.Invoke(this, EventArgs.Empty);
        quitItem.Click += (_, _) => QuitRequested?.Invoke(this, EventArgs.Empty);
        SetDiffTools(diffTools, selectedDiffExecutablePath);

        _menu = new Forms.ContextMenuStrip();
        _menu.Items.AddRange(
        [
            _statusItem,
            _shortcutItem,
            _currentItem,
            _previousItem,
            new Forms.ToolStripSeparator(),
            _showDiffItem,
            _diffViewerItem,
            _monitorItem,
            _clearItem,
            new Forms.ToolStripSeparator(),
            quitItem
        ]);

        _applicationIcon = TryLoadApplicationIcon();
        _notifyIcon = new Forms.NotifyIcon
        {
            Text = "ClipDiff",
            Icon = _applicationIcon ?? Drawing.SystemIcons.Application,
            ContextMenuStrip = _menu,
            Visible = true
        };
        _notifyIcon.DoubleClick += OnDoubleClick;
    }

    public event EventHandler? ShowDiffRequested;

    public event EventHandler? ToggleMonitoringRequested;

    public event EventHandler<ExternalDiffToolSelectedEventArgs>? DiffToolSelected;

    public event EventHandler? ChooseDiffToolRequested;

    public event EventHandler? ClearRequested;

    public event EventHandler? QuitRequested;

    public void SetDiffTools(
        IReadOnlyList<ExternalDiffToolChoice> diffTools,
        string? selectedDiffExecutablePath)
    {
        _diffTools = diffTools ?? throw new ArgumentNullException(nameof(diffTools));
        _selectedDiffExecutablePath = selectedDiffExecutablePath;
        RebuildDiffViewerMenu();
    }

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
        _applicationIcon?.Dispose();
        _menu.Dispose();
    }

    private static Forms.ToolStripMenuItem DisabledItem(string text) => new(text) { Enabled = false };

    private static Drawing.Icon? TryLoadApplicationIcon()
    {
        try
        {
            return string.IsNullOrWhiteSpace(Environment.ProcessPath)
                ? null
                : Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void RebuildDiffViewerMenu()
    {
        _diffViewerItem.DropDownItems.Clear();

        var builtIn = new Forms.ToolStripMenuItem("Built-in viewer")
        {
            Checked = string.IsNullOrWhiteSpace(_selectedDiffExecutablePath)
        };
        builtIn.Click += (_, _) => DiffToolSelected?.Invoke(this, new ExternalDiffToolSelectedEventArgs(null));
        _diffViewerItem.DropDownItems.Add(builtIn);

        if (_diffTools.Count > 0)
        {
            _diffViewerItem.DropDownItems.Add(new Forms.ToolStripSeparator());
            foreach (var choice in _diffTools)
            {
                var item = new Forms.ToolStripMenuItem(choice.DisplayName)
                {
                    Checked = string.Equals(
                        choice.ExecutablePath,
                        _selectedDiffExecutablePath,
                        StringComparison.OrdinalIgnoreCase),
                    ToolTipText = choice.ExecutablePath
                };
                item.Click += (_, _) => DiffToolSelected?.Invoke(
                    this,
                    new ExternalDiffToolSelectedEventArgs(choice));
                _diffViewerItem.DropDownItems.Add(item);
            }
        }

        _diffViewerItem.DropDownItems.Add(new Forms.ToolStripSeparator());
        var chooseProgram = new Forms.ToolStripMenuItem("Choose program...");
        chooseProgram.Click += (_, _) => ChooseDiffToolRequested?.Invoke(this, EventArgs.Empty);
        _diffViewerItem.DropDownItems.Add(chooseProgram);

        var selected = _diffTools.FirstOrDefault(choice => string.Equals(
            choice.ExecutablePath,
            _selectedDiffExecutablePath,
            StringComparison.OrdinalIgnoreCase));
        _diffViewerItem.Text = selected is null
            ? "Diff viewer: Built-in"
            : $"Diff viewer: {selected.DisplayName}";
    }

    private void OnDoubleClick(object? sender, EventArgs args)
    {
        if (_showDiffItem.Enabled)
        {
            ShowDiffRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}

internal sealed class ExternalDiffToolSelectedEventArgs(ExternalDiffToolChoice? choice) : EventArgs
{
    public ExternalDiffToolChoice? Choice { get; } = choice;
}
