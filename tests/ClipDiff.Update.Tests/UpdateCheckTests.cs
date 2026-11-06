using ClipDiff.Windows.Updates;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClipDiff.Update.Tests;

[TestClass]
public sealed class UpdateCheckTests
{
	[TestMethod]
	[DataRow(null)]
	[DataRow(false)]
	public async Task NoRequestWithoutConsentIncludingAfterRestart(bool? enabled)
	{
		var fixture = new Fixture(new(AutomaticChecksEnabled: enabled));
		Assert.AreEqual(UpdateCheckStatus.Skipped, (await fixture.Service().CheckAsync(false, default)).Status);
		Assert.AreEqual(UpdateCheckStatus.Skipped, (await fixture.Service().CheckAsync(false, default)).Status);
		Assert.AreEqual(0, fixture.Requests);
	}

	[TestMethod]
	public async Task FailedChecksAreThrottledAcrossRestarts()
	{
		var fixture = new Fixture(new(true)) { Fail = true };
		Assert.AreEqual(UpdateCheckStatus.Failed, (await fixture.Service().CheckAsync(false, default)).Status);
		fixture.Clock.Now += TimeSpan.FromHours(23);
		Assert.AreEqual(UpdateCheckStatus.Skipped, (await fixture.Service().CheckAsync(false, default)).Status);
		fixture.Clock.Now += TimeSpan.FromHours(1);
		Assert.AreEqual(UpdateCheckStatus.Failed, (await fixture.Service().CheckAsync(false, default)).Status);
		Assert.AreEqual(2, fixture.Requests);
	}

	[TestMethod]
	public async Task NotifyOncePerNewerReleaseAcrossRestarts()
	{
		var fixture = new Fixture(new(true));
		Assert.AreEqual(UpdateCheckStatus.Available, (await fixture.Service().CheckAsync(false, default)).Status);
		fixture.Clock.Now += TimeSpan.FromDays(1);
		Assert.AreEqual(UpdateCheckStatus.Skipped, (await fixture.Service().CheckAsync(false, default)).Status);
		fixture.Clock.Now += TimeSpan.FromDays(1);
		fixture.PublishedVersion = "1.3.0";
		Assert.AreEqual(UpdateCheckStatus.Available, (await fixture.Service().CheckAsync(false, default)).Status);
	}

	[TestMethod]
	public async Task ManualChecksIgnoreConsentThrottleAndNotificationSuppression()
	{
		var fixture = new Fixture(new(false));
		Assert.AreEqual(UpdateCheckStatus.Available, (await fixture.Service().CheckAsync(true, default)).Status);
		Assert.AreEqual(UpdateCheckStatus.Available, (await fixture.Service().CheckAsync(true, default)).Status);
		Assert.AreEqual(2, fixture.Requests);
		Assert.IsFalse(fixture.Settings.AutomaticChecksEnabled);
	}

	[TestMethod]
	[DataRow("1.0.0")]
	[DataRow("0.9.0")]
	[DataRow("1.0.0+different-commit")]
	public async Task CurrentAndOlderReleasesDoNotNotify(string published)
	{
		var fixture = new Fixture(new(true)) { PublishedVersion = published };
		Assert.AreEqual(UpdateCheckStatus.Current, (await fixture.Service().CheckAsync(false, default)).Status);
		Assert.IsNull(fixture.Settings.NotifiedVersion);
	}

	[TestMethod]
	public async Task TurningOffCancelsAPendingAutomaticRequest()
	{
		var settings = new UpdateCheckSettings(true);
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var service = new UpdateCheckService(Parse("1.0.0"), async cancellation =>
		{
			entered.SetResult();
			await Task.Delay(Timeout.Infinite, cancellation);
			throw new InvalidOperationException("The request should have been cancelled.");
		}, () => settings, value => { settings = value; return true; });
		var check = service.CheckAsync(false, default);
		await entered.Task;
		Assert.IsTrue(service.TrySetAutomaticChecks(false));
		Assert.AreEqual(UpdateCheckStatus.Skipped, (await check).Status);
		Assert.IsFalse(settings.AutomaticChecksEnabled);
		Assert.IsNull(settings.NotifiedVersion);
	}

	[TestMethod]
	public async Task FailedPersistenceDoesNotEnableOrStartAutomaticChecks()
	{
		var fixture = new Fixture(new(false)) { SaveSucceeds = false };
		Assert.IsFalse(fixture.Service().TrySetAutomaticChecks(true));
		Assert.IsFalse(fixture.Settings.AutomaticChecksEnabled);
		fixture.Settings = new(true);
		Assert.AreEqual(UpdateCheckStatus.Failed, (await fixture.Service().CheckAsync(false, default)).Status);
		Assert.AreEqual(0, fixture.Requests);
		Assert.AreEqual(UpdateCheckStatus.Available, (await fixture.Service().CheckAsync(true, default)).Status);
	}

	[TestMethod]
	public async Task FutureAttemptDoesNotCauseRepeatedRequestsWhenClockMovesBack()
	{
		var fixture = new Fixture(new(true, DateTimeOffset.UtcNow.AddDays(2)));
		Assert.AreEqual(UpdateCheckStatus.Skipped, (await fixture.Service().CheckAsync(false, default)).Status);
		Assert.AreEqual(0, fixture.Requests);
	}

	internal static ReleaseVersion Parse(string text)
	{
		Assert.IsTrue(ReleaseVersion.TryParse(text, out var version));
		return version;
	}

	private sealed class TestClock : TimeProvider
	{
		public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
		public override DateTimeOffset GetUtcNow() => Now;
	}

	private sealed class Fixture(UpdateCheckSettings preferences)
	{
		public UpdateCheckSettings Settings = preferences;
		public readonly TestClock Clock = new();
		public int Requests;
		public bool Fail;
		public bool SaveSucceeds = true;
		public string PublishedVersion = "1.2.0";

		public UpdateCheckService Service() => new(Parse("1.0.0"), _ =>
		{
			Requests++;

			if (Fail)
			{
				throw new HttpRequestException("Offline");
			}

			return Task.FromResult(new GitHubRelease(Parse(PublishedVersion), new Uri("https://github.com/stuartd/ClipboardDiff/releases/latest")));
		}, () => Settings, value =>
		{
			if (!SaveSucceeds)
			{
				return false;
			}

			Settings = value;
			return true;
		}, Clock);
	}
}
