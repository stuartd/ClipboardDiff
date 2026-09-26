using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using ClipDiff.Windows.ExternalDiff;

namespace ClipDiff.Windows.Tray;

internal sealed class TrayIconController : IDisposable
{
    private readonly Forms.NotifyIcon notifyIcon;
    private readonly Drawing.Icon? applicationIcon;
    private readonly Forms.ContextMenuStrip menu;
    private readonly Forms.ToolStripMenuItem statusItem;
    private readonly Forms.ToolStripMenuItem currentItem;
    private readonly Forms.ToolStripMenuItem previousItem;
    private readonly Forms.ToolStripMenuItem showDiffItem;
    private readonly Forms.ToolStripMenuItem diffViewerItem;
    private readonly Forms.ToolStripMenuItem shortcutItem;
    private readonly Forms.ToolStripMenuItem monitorItem;
    private readonly Forms.ToolStripMenuItem clearItem;
    private bool disposed;

    private IReadOnlyList<ExternalDiffToolChoice> diffTools = [];
    private string? selectedDiffExecutablePath;

    public TrayIconController(
        IReadOnlyList<ExternalDiffToolChoice> diffTools,
        string? selectedDiffExecutablePath)
    {
        statusItem = DisabledItem("Waiting for copied text");
        currentItem = DisabledItem("Current: None");
        previousItem = DisabledItem("Previous: None");
        showDiffItem = new Forms.ToolStripMenuItem("Show Diff (Ctrl+Alt+D)");
        diffViewerItem = new Forms.ToolStripMenuItem("Diff viewer");
        shortcutItem = new Forms.ToolStripMenuItem("Keyboard shortcut...");
        monitorItem = new Forms.ToolStripMenuItem("Monitor Clipboard") { CheckOnClick = false };
        clearItem = new Forms.ToolStripMenuItem("Clear Captured Text");
        var aboutItem = new Forms.ToolStripMenuItem("About ClipDiff");
        var quitItem = new Forms.ToolStripMenuItem("Quit ClipDiff");

        showDiffItem.Click += (_, _) => ShowDiffRequested?.Invoke(this, EventArgs.Empty);
        shortcutItem.Click += (_, _) => ShortcutRequested?.Invoke(this, EventArgs.Empty);
        monitorItem.Click += (_, _) => ToggleMonitoringRequested?.Invoke(this, EventArgs.Empty);
        clearItem.Click += (_, _) => ClearRequested?.Invoke(this, EventArgs.Empty);
        aboutItem.Click += (_, _) => AboutRequested?.Invoke(this, EventArgs.Empty);
        quitItem.Click += (_, _) => QuitRequested?.Invoke(this, EventArgs.Empty);
        SetDiffTools(diffTools, selectedDiffExecutablePath);

        menu = new Forms.ContextMenuStrip();
        menu.Items.AddRange(
        [
            statusItem,
            currentItem,
            previousItem,
            new Forms.ToolStripSeparator(),
            showDiffItem,
            diffViewerItem,
            shortcutItem,
            monitorItem,
            clearItem,
            new Forms.ToolStripSeparator(),
            aboutItem,
            quitItem
        ]);
        DarkMenuRenderer.ApplyTo(menu);

        applicationIcon = TryLoadApplicationIcon();
        notifyIcon = new Forms.NotifyIcon
        {
            Text = "ClipDiff",
            Icon = applicationIcon ?? Drawing.SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = true
        };
        notifyIcon.DoubleClick += OnDoubleClick;
    }

    public event EventHandler? ShowDiffRequested;

    public event EventHandler? ToggleMonitoringRequested;

    public event EventHandler? ShortcutRequested;

    public event EventHandler<ExternalDiffToolSelectedEventArgs>? DiffToolSelected;

    public event EventHandler? ChooseDiffToolRequested;

    public event EventHandler? ClearRequested;

    public event EventHandler? AboutRequested;

    public event EventHandler? QuitRequested;

    public void SetDiffTools(
        IReadOnlyList<ExternalDiffToolChoice> externalDiffTools,
        string? diffToolExecutablePath)
    {
        this.diffTools = externalDiffTools ?? throw new ArgumentNullException(nameof(externalDiffTools));
        this.selectedDiffExecutablePath = diffToolExecutablePath;
        RebuildDiffViewerMenu();
    }

    public void Update(
        string status,
        bool hotKeyAvailable,
        string hotKeyDisplayText,
        bool monitoring,
        ClipboardEntry? current,
        ClipboardEntry? previous)
    {
        var fileLabels = ClipboardEntryDisplay.ResolveFileLabels(previous, current);
        statusItem.Text = status;
        showDiffItem.Text = hotKeyAvailable
            ? $"Show Diff ({hotKeyDisplayText})"
            : "Show Diff (shortcut unavailable)";
        currentItem.Text = "Current: " + EntryPreview(current, fileLabels.Current);
        previousItem.Text = "Previous: " + EntryPreview(previous, fileLabels.Previous);
        showDiffItem.Enabled = current is not null && previous is not null;
        monitorItem.Checked = monitoring;
        clearItem.Enabled = current is not null;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        notifyIcon.Visible = false;
        notifyIcon.DoubleClick -= OnDoubleClick;
        notifyIcon.Dispose();
        applicationIcon?.Dispose();
        menu.Dispose();
    }

    private static Forms.ToolStripMenuItem DisabledItem(string text) => new(text) { Enabled = false };

    private static string EntryPreview(ClipboardEntry? entry, string? fileLabel)
    {
        if (entry is null)
        {
            return "None";
        }

        return TextLines.EntryPreview(entry, fileLabel);
    }

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
        diffViewerItem.DropDownItems.Clear();

        var builtIn = new Forms.ToolStripMenuItem("Built-in viewer")
        {
            Checked = string.IsNullOrWhiteSpace(selectedDiffExecutablePath)
        };
        builtIn.Click += (_, _) => DiffToolSelected?.Invoke(this, new ExternalDiffToolSelectedEventArgs(null));
        diffViewerItem.DropDownItems.Add(builtIn);

        if (diffTools.Count > 0)
        {
            diffViewerItem.DropDownItems.Add(new Forms.ToolStripSeparator());
            foreach (var choice in diffTools)
            {
                var item = new Forms.ToolStripMenuItem(choice.DisplayName)
                {
                    Checked = string.Equals(
                        choice.ExecutablePath,
                        selectedDiffExecutablePath,
                        StringComparison.OrdinalIgnoreCase),
                    ToolTipText = choice.ExecutablePath
                };
                item.Click += (_, _) => DiffToolSelected?.Invoke(
                    this,
                    new ExternalDiffToolSelectedEventArgs(choice));
                diffViewerItem.DropDownItems.Add(item);
            }
        }

        diffViewerItem.DropDownItems.Add(new Forms.ToolStripSeparator());
        var chooseProgram = new Forms.ToolStripMenuItem("Choose program...");
        chooseProgram.Click += (_, _) => ChooseDiffToolRequested?.Invoke(this, EventArgs.Empty);
        diffViewerItem.DropDownItems.Add(chooseProgram);
        DarkMenuRenderer.ApplyTo(diffViewerItem.DropDown);

        var selected = diffTools.FirstOrDefault(choice => string.Equals(
            choice.ExecutablePath,
            selectedDiffExecutablePath,
            StringComparison.OrdinalIgnoreCase));
        diffViewerItem.Text = selected is null
            ? "Diff viewer: Built-in"
            : $"Diff viewer: {selected.DisplayName}";
    }

    private void OnDoubleClick(object? sender, EventArgs args)
    {
        if (showDiffItem.Enabled)
        {
            ShowDiffRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}

internal sealed class ExternalDiffToolSelectedEventArgs(ExternalDiffToolChoice? choice) : EventArgs
{
    public ExternalDiffToolChoice? Choice { get; } = choice;
}
