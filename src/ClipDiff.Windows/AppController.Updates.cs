using System.Net.Http;
using System.Reflection;
using System.Windows.Threading;
using ClipDiff.Windows.Updates;
using ClipDiff.Windows.Views;

namespace ClipDiff.Windows;

internal sealed partial class AppController
{
	private readonly HttpClient updateHttpClient = new(new HttpClientHandler { AllowAutoRedirect = false });
	private readonly CancellationTokenSource updateShutdown = new();
	private UpdateCheckService updateCheckService = null!;
	private DispatcherTimer? updateTimer;
	private UpdateWindow? updateWindow;
	private GitHubRelease? availableRelease;
	private bool updateCheckRunning;

	private void InitializeUpdateChecks()
	{
		var versionText = typeof(AppController).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
		ReleaseVersion.TryParse(versionText, out var version);
		var client = new GitHubReleaseClient(updateHttpClient);
		updateCheckService = new UpdateCheckService(version, client.FetchLatestAsync,
			() => settings.UpdateChecks ?? new(), TrySaveUpdateSettings);
		trayIcon.ToggleAutomaticUpdatesRequested += OnToggleAutomaticUpdatesRequested;
		trayIcon.CheckForUpdatesRequested += OnCheckForUpdatesRequested;
		trayIcon.AvailableUpdateRequested += OnAvailableUpdateRequested;
		RefreshUpdateChecks();
	}

	public void InitializeUpdateConsent()
	{
		if (disposed)
		{
			return;
		}

		if (settings.UpdateChecks?.AutomaticChecksEnabled is null)
		{
			var enabled = UpdateWindow.AskConsent();

			if (disposed)
			{
				return;
			}

			if (!updateCheckService.TrySetAutomaticChecks(enabled))
			{
				ShowStartupError("ClipDiff could not save your update preference. Automatic checks remain off; the question may appear next time.");
			}
		}

		RefreshUpdateChecks();
		updateTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(1) };
		updateTimer.Tick += OnUpdateTimer;
		updateTimer.Start();
		_ = CheckForUpdatesAsync(manual: false);
	}

	private bool TrySaveUpdateSettings(UpdateCheckSettings preferences)
	{
		var updated = settings with { UpdateChecks = preferences };

		if (!settingsStore.TrySave(updated))
		{
			return false;
		}

		settings = updated;
		return true;
	}

	private void OnToggleAutomaticUpdatesRequested(object? sender, EventArgs args)
	{
		var enabled = settings.UpdateChecks?.AutomaticChecksEnabled == true;

		if (!enabled && !UpdateWindow.AskConsent())
		{
			return;
		}

		if (disposed)
		{
			return;
		}

		if (!updateCheckService.TrySetAutomaticChecks(!enabled))
		{
			ShowStartupError("ClipDiff could not save your update preference. The previous setting remains in effect.");
		}

		RefreshUpdateChecks();
		_ = CheckForUpdatesAsync(manual: false);
	}

	private void OnUpdateTimer(object? sender, EventArgs args) => _ = CheckForUpdatesAsync(manual: false);
	private void OnCheckForUpdatesRequested(object? sender, EventArgs args) => _ = CheckForUpdatesAsync(manual: true);

	private void OnAvailableUpdateRequested(object? sender, EventArgs args)
	{
		if (availableRelease is { } release)
		{
			ShowUpdateWindow();
			updateWindow!.SetMessage($"ClipDiff {release.Version.Text} is available. Download opens its GitHub release page. Run the Setup installer to update ClipDiff.", release.DownloadPage);
		}
	}

	private async Task CheckForUpdatesAsync(bool manual)
	{
		if (disposed || updateCheckRunning)
		{
			return;
		}

		if (manual)
		{
			ShowUpdateWindow();
			updateWindow!.SetMessage("Checking GitHub for a new ClipDiff release...");
		}

		updateCheckRunning = true;
		RefreshUpdateChecks();
		var result = await updateCheckService.CheckAsync(manual, updateShutdown.Token);

		if (disposed)
		{
			return;
		}

		updateCheckRunning = false;
		RefreshUpdateChecks();

		if (result.Status == UpdateCheckStatus.Available && result.Release is { } release)
		{
			availableRelease = release;
			trayIcon.SetAvailableUpdate(release.Version.Text, notify: !manual);

			if (manual)
			{
				updateWindow?.SetMessage($"ClipDiff {release.Version.Text} is available. Download opens its GitHub release page. Run the Setup installer to update ClipDiff.", release.DownloadPage);
			}
		}
		else if (manual)
		{
			updateWindow?.SetMessage(result.Status == UpdateCheckStatus.Current
				? "ClipDiff is up to date."
				: "ClipDiff could not check for updates. Check your connection and try again later.");
		}
	}

	private void ShowUpdateWindow()
	{
		if (updateWindow is null)
		{
			updateWindow = new UpdateWindow();
			updateWindow.Closed += (_, _) => updateWindow = null;
		}

		updateWindow.Show();
		updateWindow.Activate();
	}

	private void RefreshUpdateChecks() => trayIcon.SetUpdateChecks(settings.UpdateChecks?.AutomaticChecksEnabled == true, updateCheckRunning);

	private void DisposeUpdateChecks()
	{
		updateTimer?.Stop();
		updateShutdown.Cancel();
		updateWindow?.Close();
		updateHttpClient.Dispose();
		trayIcon.ToggleAutomaticUpdatesRequested -= OnToggleAutomaticUpdatesRequested;
		trayIcon.CheckForUpdatesRequested -= OnCheckForUpdatesRequested;
		trayIcon.AvailableUpdateRequested -= OnAvailableUpdateRequested;
	}
}
