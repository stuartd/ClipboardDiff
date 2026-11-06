using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using Orientation = System.Windows.Controls.Orientation;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace ClipDiff.Windows.Views;

internal sealed class UpdateWindow : Window
{
	private readonly TextBlock messageText;
	private readonly Button downloadButton;
	private Uri? downloadPage;

	public UpdateWindow(bool consent = false)
	{
		Title = consent ? "Check for ClipDiff updates?" : "ClipDiff updates";
		Width = 460;
		SizeToContent = SizeToContent.Height;
		ResizeMode = ResizeMode.NoResize;
		WindowStartupLocation = WindowStartupLocation.CenterScreen;
		Background = new SolidColorBrush(Color.FromRgb(32, 32, 32));
		Foreground = Brushes.WhiteSmoke;
		var panel = new StackPanel { Margin = new Thickness(24) };
		messageText = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 20) };
		panel.Children.Add(messageText);
		var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
		downloadButton = new Button { Content = consent ? "Enable" : "Download", Padding = new Thickness(16, 6, 16, 6), Visibility = consent ? Visibility.Visible : Visibility.Collapsed };
		var dismiss = new Button { Content = consent ? "Not now" : "Dismiss", Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(8, 0, 0, 0), IsCancel = true, IsDefault = true };
		dismiss.Click += (_, _) => Close();
		downloadButton.Click += (_, _) =>
		{
			if (consent)
			{
				DialogResult = true;
			}
			else
			{
				OpenDownloadPage();
			}
		};
		buttons.Children.Add(downloadButton);
		buttons.Children.Add(dismiss);
		panel.Children.Add(buttons);
		Content = panel;
	}

	public void SetMessage(string message, Uri? page = null)
	{
		messageText.Text = message;
		downloadPage = page;
		downloadButton.Visibility = page is null ? Visibility.Collapsed : Visibility.Visible;
	}

	public static bool AskConsent()
	{
		var window = new UpdateWindow(consent: true);
		window.messageText.Text = "Allow ClipDiff to check api.github.com for new releases automatically, at most once a day? " +
			"GitHub receives a normal web request, including your IP address. No clipboard content or document data is sent.\n\n" +
			"Updates are never downloaded or installed automatically. You can change this in the tray menu, or check manually at any time.";
		return window.ShowDialog() == true;
	}

	private void OpenDownloadPage()
	{
		if (downloadPage is null)
		{
			return;
		}

		try
		{
			using var process = Process.Start(new ProcessStartInfo(downloadPage.AbsoluteUri) { UseShellExecute = true });
			Close();
		}
		catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or NotSupportedException)
		{
			messageText.Text = "ClipDiff could not open your browser. Visit github.com/stuartd/ClipboardDiff/releases/latest to download the update.";
		}
	}
}
