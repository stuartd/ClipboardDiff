using System.ComponentModel;
using System.Windows;

namespace ClipDiff.Windows.Views;

public partial class DiffWindow : Window
{
    public DiffWindow()
    {
        InitializeComponent();
    }

    public bool AllowClose { get; set; }

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
