using System.IO;
using System.Net.Http;
using System.Text.Json;
using ClipDiff.Windows.Diagnostics;

namespace ClipDiff.Windows.Updates;

internal sealed record UpdateCheckSettings(
	bool? AutomaticChecksEnabled = null,
	DateTimeOffset? LastAttemptUtc = null,
	string? NotifiedVersion = null);

internal enum UpdateCheckStatus { Skipped, Busy, Current, Available, Failed }

internal enum UpdateCheckReason
{
	AutomaticChecksNotEnabled,
	WithinCheckInterval,
	CheckInProgress,
	InstalledVersionUnknown,
	AttemptTimeSaveFailed,
	Cancelled,
	InstalledVersionAtLeastPublished,
	AlreadyNotified,
	NotificationSaveFailed,
	NewerReleasePublished,
	RequestTimedOut,
	HttpError,
	RequestFailed,
	InvalidResponse
}

internal sealed record UpdateCheckResult(UpdateCheckStatus Status, GitHubRelease? Release = null);

internal sealed class UpdateCheckService(
	ReleaseVersion? installedVersion,
	Func<CancellationToken, Task<GitHubRelease>> fetch,
	Func<UpdateCheckSettings> readSettings,
	Func<UpdateCheckSettings, bool> saveSettings,
	TimeProvider? clock = null,
	DiagnosticLog? diagnosticLog = null)
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

		UpdateCheckResult Decide(UpdateCheckStatus status, UpdateCheckReason reason, GitHubRelease? release = null)
		{
			diagnosticLog?.WriteUpdateDecision(manual, installedVersion, status, reason);
			return new(status, release);
		}

		if (!manual && settings.AutomaticChecksEnabled != true)
		{
			return Decide(UpdateCheckStatus.Skipped, UpdateCheckReason.AutomaticChecksNotEnabled);
		}

		if (!manual && settings.LastAttemptUtc is { } last && now - last < TimeSpan.FromDays(1))
		{
			return Decide(UpdateCheckStatus.Skipped, UpdateCheckReason.WithinCheckInterval);
		}

		if (checking)
		{
			return Decide(UpdateCheckStatus.Busy, UpdateCheckReason.CheckInProgress);
		}

		if (installedVersion is null)
		{
			return Decide(UpdateCheckStatus.Failed, UpdateCheckReason.InstalledVersionUnknown);
		}

		// Persist attempts, including offline failures, so relaunching cannot cause repeated requests.
		if (!saveSettings(settings with { LastAttemptUtc = now }) && !manual)
		{
			return Decide(UpdateCheckStatus.Failed, UpdateCheckReason.AttemptTimeSaveFailed);
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
				return Decide(UpdateCheckStatus.Skipped, UpdateCheckReason.Cancelled);
			}

			if (release.Version.CompareTo(installedVersion) <= 0)
			{
				return Decide(UpdateCheckStatus.Current, UpdateCheckReason.InstalledVersionAtLeastPublished);
			}

			if (!manual && ReleaseVersion.TryParse(settings.NotifiedVersion, out var notified) &&
				release.Version.CompareTo(notified) <= 0)
			{
				return Decide(UpdateCheckStatus.Skipped, UpdateCheckReason.AlreadyNotified);
			}

			if (!saveSettings(settings with { NotifiedVersion = release.Version.Text }) && !manual)
			{
				return Decide(UpdateCheckStatus.Failed, UpdateCheckReason.NotificationSaveFailed);
			}

			return Decide(UpdateCheckStatus.Available, UpdateCheckReason.NewerReleasePublished, release);
		}
		catch (OperationCanceledException)
		{
			return cancellation.IsCancellationRequested
				? Decide(UpdateCheckStatus.Skipped, UpdateCheckReason.Cancelled)
				: Decide(UpdateCheckStatus.Failed, UpdateCheckReason.RequestTimedOut);
		}
		catch (HttpRequestException exception)
		{
			return Decide(UpdateCheckStatus.Failed,
				exception.StatusCode.HasValue ? UpdateCheckReason.HttpError : UpdateCheckReason.RequestFailed);
		}
		catch (Exception exception) when (exception is InvalidDataException or JsonException)
		{
			return Decide(UpdateCheckStatus.Failed, UpdateCheckReason.InvalidResponse);
		}
		catch (IOException)
		{
			return Decide(UpdateCheckStatus.Failed, UpdateCheckReason.RequestFailed);
		}
		finally
		{
			pendingAutomaticCheck = null;
			checking = false;
		}
	}
}
