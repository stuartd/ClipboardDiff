using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Navigation;

namespace ClipDiff.Windows.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        VersionText.Text = $"Version {GetDisplayVersion()}";
    }

    public bool AllowClose { get; set; }

    private static string GetDisplayVersion()
    {
        var assembly = typeof(AboutWindow).Assembly;
        
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        return AboutVersionFormatter.Format(informationalVersion, assembly.GetName().Version);
    }

    private void OnLinkRequestNavigate(object sender, RequestNavigateEventArgs args)
    {
        try
        {
            Process.Start(new ProcessStartInfo(args.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            System.Media.SystemSounds.Beep.Play();
        }

        args.Handled = true;
    }

    private void OnCloseClick(object sender, RoutedEventArgs args) => Hide();

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
