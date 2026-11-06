using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace ClipDiff.Windows.Updates;

internal sealed record UpdateCheckSettings(
	bool? AutomaticChecksEnabled = null,
	DateTimeOffset? LastAttemptUtc = null,
	string? NotifiedVersion = null);

internal enum UpdateCheckStatus { Skipped, Busy, Current, Available, Failed }

internal sealed record UpdateCheckResult(UpdateCheckStatus Status, GitHubRelease? Release = null);

internal sealed class UpdateCheckService(
	ReleaseVersion? installedVersion,
	Func<CancellationToken, Task<GitHubRelease>> fetch,
	Func<UpdateCheckSettings> readSettings,
	Func<UpdateCheckSettings, bool> saveSettings,
	TimeProvider? clock = null)
{
	private readonly TimeProvider timeProvider = clock ?? TimeProvider.System;
	private CancellationTokenSource? pendingAutomaticCheck;
	private bool checking;

	public bool TrySetAutomaticChecks(bool enabled)
	{
		if (!saveSettings(readSettings() with { AutomaticChecksEnabled = enabled }))
		{
			return false;
		}

		if (!enabled)
		{
			pendingAutomaticCheck?.Cancel();
		}

		return true;
	}

	public async Task<UpdateCheckResult> CheckAsync(bool manual, CancellationToken cancellationToken)
	{
		var settings = readSettings();
		var now = timeProvider.GetUtcNow();

		if (!manual && (settings.AutomaticChecksEnabled != true ||
			settings.LastAttemptUtc is { } last && now - last < TimeSpan.FromDays(1)))
		{
			return new(UpdateCheckStatus.Skipped);
		}

		if (checking)
		{
			return new(UpdateCheckStatus.Busy);
		}

		if (installedVersion is null)
		{
			return new(UpdateCheckStatus.Failed);
		}

		// Persist attempts, including offline failures, so relaunching cannot cause repeated requests.
		if (!saveSettings(settings with { LastAttemptUtc = now }) && !manual)
		{
			return new(UpdateCheckStatus.Failed);
		}

		checking = true;
		using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

		if (!manual)
		{
			pendingAutomaticCheck = cancellation;
		}

		try
		{
			var release = await fetch(cancellation.Token);
			settings = readSettings();

			if (cancellation.IsCancellationRequested || (!manual && settings.AutomaticChecksEnabled != true))
			{
				return new(UpdateCheckStatus.Skipped);
			}

			if (release.Version.CompareTo(installedVersion) <= 0)
			{
				return new(UpdateCheckStatus.Current);
			}

			if (!manual && ReleaseVersion.TryParse(settings.NotifiedVersion, out var notified) &&
				release.Version.CompareTo(notified) <= 0)
			{
				return new(UpdateCheckStatus.Skipped);
			}

			if (!saveSettings(settings with { NotifiedVersion = release.Version.Text }) && !manual)
			{
				return new(UpdateCheckStatus.Failed);
			}

			return new(UpdateCheckStatus.Available, release);
		}
		catch (OperationCanceledException)
		{
			return new(cancellation.IsCancellationRequested ? UpdateCheckStatus.Skipped : UpdateCheckStatus.Failed);
		}
		catch (Exception exception) when (exception is HttpRequestException or IOException or JsonException)
		{
			return new(UpdateCheckStatus.Failed);
		}
		finally
		{
			pendingAutomaticCheck = null;
			checking = false;
		}
	}
}
