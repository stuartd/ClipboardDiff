using System.Net;
using System.Text;
using ClipDiff.Windows.Diagnostics;
using ClipDiff.Windows.Updates;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClipDiff.Update.Tests;

[TestClass]
public sealed class GitHubReleaseClientTests
{
	[TestMethod]
	public async Task RequestsOnlyTheReleaseEndpointAndBuildsATrustedDownloadDestination()
	{
		using var client = new HttpClient(new Handler(request =>
		{
			Assert.AreEqual(GitHubReleaseClient.LatestReleaseApi, request.RequestUri!.AbsoluteUri);
			Assert.AreEqual(HttpMethod.Get, request.Method);
			Assert.IsNull(request.Content);
			Assert.AreEqual("ClipDiff", request.Headers.UserAgent.ToString());
			return Response("""{"tag_name":"v1.2.3","draft":false,"prerelease":false,"html_url":"https://evil.example/"}""");
		}));
		var release = await new GitHubReleaseClient(client).FetchLatestAsync(default);
		Assert.AreEqual("1.2.3", release.Version.Text);
		Assert.AreEqual("https://github.com/stuartd/ClipboardDiff/releases/tag/v1.2.3", release.DownloadPage.AbsoluteUri);
	}

	[TestMethod]
	[DataRow("not json")]
	[DataRow("[]")]
	[DataRow("{\"tag_name\":\"v1.2.3\",\"draft\":true,\"prerelease\":false}")]
	[DataRow("{\"tag_name\":\"v1.2.3\",\"draft\":false,\"prerelease\":true}")]
	[DataRow("{\"tag_name\":12,\"draft\":false,\"prerelease\":false}")]
	[DataRow("{\"tag_name\":\"garbage\",\"draft\":false,\"prerelease\":false}")]
	public async Task RejectsInvalidOrUnpublishedReleases(string payload)
	{
		using var client = new HttpClient(new Handler(_ => Response(payload)));
		var service = new GitHubReleaseClient(client);
		await Assert.ThrowsAsync<Exception>(async () => { await service.FetchLatestAsync(default); });
	}

	[TestMethod]
	public async Task RejectsOversizedResponses()
	{
		using var client = new HttpClient(new Handler(_ => Response(new string(' ', 65537))));
		await Assert.ThrowsAsync<InvalidDataException>(async () => { await new GitHubReleaseClient(client).FetchLatestAsync(default); });
	}

	[TestMethod]
	[DataRow(HttpStatusCode.OK, "v1.2.3", "1.2.3", "Available", "NewerReleasePublished")]
	[DataRow(HttpStatusCode.OK, "v1.0.0.2", "1.0.0.2", "Available", "NewerReleasePublished")]
	[DataRow(HttpStatusCode.OK, "v1.0.0", "1.0.0", "Current", "InstalledVersionAtLeastPublished")]
	[DataRow(HttpStatusCode.OK, "v1.0.0.0", "1.0.0.0", "Current", "InstalledVersionAtLeastPublished")]
	[DataRow(HttpStatusCode.OK, "invalid-tag", "none", "Failed", "InvalidResponse")]
	[DataRow(HttpStatusCode.Forbidden, "v1.2.3", "none", "Failed", "HttpError")]
	public async Task LogsRequestResponseAndDecision(HttpStatusCode status, string tag, string publishedVersion,
		string expected, string reason)
	{
		var directory = Directory.CreateTempSubdirectory();
		var path = Path.Combine(directory.FullName, "ClipDiff.log");
		var log = new DiagnosticLog(path);
		var settings = new UpdateCheckSettings(false);
		var requests = 0;
		using var http = new HttpClient(new Handler(_ =>
		{
			requests++;
			return new HttpResponseMessage(status)
			{
				Content = new StringContent($$"""{"tag_name":"{{tag}}","draft":false,"prerelease":false,"body":"omitted release notes"}""")
			};
		}));
		var client = new GitHubReleaseClient(http, log);
		var service = new UpdateCheckService(UpdateCheckTests.Parse("1.0.0"), client.FetchLatestAsync,
			() => settings, updated => { settings = updated; return true; }, diagnosticLog: log);

		try
		{
			Assert.AreEqual(UpdateCheckStatus.Skipped, (await service.CheckAsync(false, default)).Status);
			Assert.AreEqual(expected, (await service.CheckAsync(true, default)).Status.ToString());
			Assert.AreEqual(1, requests);

			await log.CompleteAsync();
			var lines = await File.ReadAllLinesAsync(path);
			Assert.HasCount(4, lines);
			StringAssert.EndsWith(lines[0], "UpdateDecision mode=automatic installed=1.0.0 result=Skipped reason=AutomaticChecksNotEnabled");
			StringAssert.EndsWith(lines[1], $"UpdateRequest GET {GitHubReleaseClient.LatestReleaseApi}");
			StringAssert.EndsWith(lines[2], $"UpdateResponse http_status={(int)status} release={publishedVersion}");
			if (expected == "Failed")
			{
				StringAssert.Contains(lines[3], "exception=");
			}

			StringAssert.Contains(lines[3], $"UpdateDecision mode=manual installed=1.0.0 result={expected} reason={reason}");
		}
		finally
		{
			await log.CompleteAsync();
			directory.Delete(recursive: true);
		}
	}

	private static HttpResponseMessage Response(string payload) => new(HttpStatusCode.OK)
	{
		Content = new StringContent(payload, Encoding.UTF8, "application/json")
	};

	private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
			Task.FromResult(respond(request));
	}
}
