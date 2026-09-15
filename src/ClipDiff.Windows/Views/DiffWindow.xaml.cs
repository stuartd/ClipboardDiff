using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using ClipDiff.Windows.Native;

namespace ClipDiff.Windows.Views;

public partial class DiffWindow : Window
{
    public DiffWindow()
    {
        InitializeComponent();
    }

    public bool AllowClose { get; set; }

    private void OnSourceInitialized(object? sender, EventArgs args)
    {
        var windowHandle = new WindowInteropHelper(this).Handle;
        var enabled = 1;
        var result = NativeMethods.DwmSetWindowAttribute(
            windowHandle,
            NativeMethods.DwmwaUseImmersiveDarkMode,
            ref enabled,
            sizeof(int));

        if (result != 0)
        {
            NativeMethods.DwmSetWindowAttribute(
                windowHandle,
                NativeMethods.DwmwaUseImmersiveDarkModeBefore20H1,
                ref enabled,
                sizeof(int));
        }
    }

    private void OnClosing(object? sender, CancelEventArgs args)
    {
        if (AllowClose)
        {
            return;
        }

        args.Cancel = true;
        Hide();
    }
}
