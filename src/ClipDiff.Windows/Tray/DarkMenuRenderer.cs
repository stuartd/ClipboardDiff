using Drawing = System.Drawing;
using Drawing2D = System.Drawing.Drawing2D;
using Forms = System.Windows.Forms;

namespace ClipDiff.Windows.Tray;

internal sealed class DarkMenuRenderer : Forms.ToolStripProfessionalRenderer
{
    private static readonly Drawing.Color Background = Drawing.Color.FromArgb(32, 32, 32);
    private static readonly Drawing.Color Foreground = Drawing.Color.FromArgb(245, 245, 245);
    private static readonly Drawing.Color DisabledForeground = Drawing.Color.FromArgb(155, 155, 155);

    public static DarkMenuRenderer Instance { get; } = new();

    private DarkMenuRenderer()
        : base(new DarkMenuColorTable())
    {
        RoundedEdges = false;
    }

    public static void ApplyTo(Forms.ToolStrip toolStrip)
    {
        toolStrip.Renderer = Instance;
        toolStrip.BackColor = Background;
        toolStrip.ForeColor = Foreground;

        foreach (Forms.ToolStripItem item in toolStrip.Items)
        {
            item.BackColor = Background;
            item.ForeColor = Foreground;

            if (item is Forms.ToolStripDropDownItem dropDownItem && dropDownItem.HasDropDownItems)
            {
                ApplyTo(dropDownItem.DropDown);
            }
        }
    }

    protected override void OnRenderArrow(Forms.ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = e.Item?.Enabled is not false ? Foreground : DisabledForeground;
        base.OnRenderArrow(e);
    }

    protected override void OnRenderItemCheck(Forms.ToolStripItemImageRenderEventArgs e)
    {
        var bounds = e.ImageRectangle;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var previousSmoothingMode = e.Graphics.SmoothingMode;
        e.Graphics.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias;

        var scale = e.Graphics.DpiX / 96f;
        using var pen = new Drawing.Pen(
            e.Item?.Enabled is not false ? Foreground : DisabledForeground,
            Math.Max(1.5f, 1.75f * scale))
        {
            StartCap = Drawing2D.LineCap.Round,
            EndCap = Drawing2D.LineCap.Round,
            LineJoin = Drawing2D.LineJoin.Round
        };

        var points = new[]
        {
            new Drawing.PointF(bounds.Left + (bounds.Width * 0.18f), bounds.Top + (bounds.Height * 0.52f)),
            new Drawing.PointF(bounds.Left + (bounds.Width * 0.42f), bounds.Top + (bounds.Height * 0.75f)),
            new Drawing.PointF(bounds.Left + (bounds.Width * 0.82f), bounds.Top + (bounds.Height * 0.27f))
        };
        e.Graphics.DrawLines(pen, points);
        e.Graphics.SmoothingMode = previousSmoothingMode;
    }

    protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item?.Enabled is not false ? Foreground : DisabledForeground;
        base.OnRenderItemText(e);
    }

    private sealed class DarkMenuColorTable : Forms.ProfessionalColorTable
    {
        private static readonly Drawing.Color Selection = Drawing.Color.FromArgb(62, 62, 62);
        private static readonly Drawing.Color Pressed = Drawing.Color.FromArgb(72, 72, 72);
        private static readonly Drawing.Color Border = Drawing.Color.FromArgb(78, 78, 78);
        private static readonly Drawing.Color Separator = Drawing.Color.FromArgb(70, 70, 70);

        public override Drawing.Color ToolStripDropDownBackground => Background;
        public override Drawing.Color ImageMarginGradientBegin => Background;
        public override Drawing.Color ImageMarginGradientMiddle => Background;
        public override Drawing.Color ImageMarginGradientEnd => Background;
        public override Drawing.Color MenuBorder => Border;
        public override Drawing.Color MenuItemBorder => Selection;
        public override Drawing.Color MenuItemSelected => Selection;
        public override Drawing.Color MenuItemSelectedGradientBegin => Selection;
        public override Drawing.Color MenuItemSelectedGradientEnd => Selection;
        public override Drawing.Color MenuItemPressedGradientBegin => Pressed;
        public override Drawing.Color MenuItemPressedGradientMiddle => Pressed;
        public override Drawing.Color MenuItemPressedGradientEnd => Pressed;
        public override Drawing.Color CheckBackground => Pressed;
        public override Drawing.Color CheckSelectedBackground => Selection;
        public override Drawing.Color CheckPressedBackground => Pressed;
        public override Drawing.Color SeparatorDark => Separator;
        public override Drawing.Color SeparatorLight => Background;
    }
}
