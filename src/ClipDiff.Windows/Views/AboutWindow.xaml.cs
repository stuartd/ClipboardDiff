using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Navigation;
using ClipDiff.Windows.Native;

namespace ClipDiff.Windows.Views;

public partial class AboutWindow : Window
{
	public AboutWindow()
	{
		InitializeComponent();
		BuildInfoText.Text = GetBuildInfo();
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

	private static string GetBuildInfo()
	{
		var assembly = typeof(AboutWindow).Assembly;

		var informationalVersion = assembly
			.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
			.InformationalVersion;

		var buildDate = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
			.FirstOrDefault(attribute => attribute.Key == "BuildDateUtc")?.Value;

		return AboutBuildInfoFormatter.Format(informationalVersion, buildDate);
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
