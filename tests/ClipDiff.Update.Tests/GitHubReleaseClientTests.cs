using System.Net;
using System.Text;
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
