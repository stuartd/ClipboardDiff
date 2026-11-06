using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace ClipDiff.Windows.Updates;

internal sealed record GitHubRelease(ReleaseVersion Version, Uri DownloadPage);

internal sealed class GitHubReleaseClient(HttpClient client)
{
	public const string LatestReleaseApi = "https://api.github.com/repos/stuartd/ClipboardDiff/releases/latest";
	private const string ReleasePagePrefix = "https://github.com/stuartd/ClipboardDiff/releases/tag/";
	private const int MaximumResponseBytes = 64 * 1024;

	public async Task<GitHubRelease> FetchLatestAsync(CancellationToken cancellationToken)
	{
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		deadline.CancelAfter(TimeSpan.FromSeconds(10));
		using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApi);
		request.Headers.UserAgent.ParseAdd("ClipDiff");
		request.Headers.Accept.ParseAdd("application/vnd.github+json");
		using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token)
			.ConfigureAwait(false);
		response.EnsureSuccessStatusCode();
		using var stream = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
		using var payload = new MemoryStream();
		var buffer = new byte[4096];
		int count;

		while ((count = await stream.ReadAsync(buffer, deadline.Token).ConfigureAwait(false)) != 0)
		{
			if (payload.Length + count > MaximumResponseBytes)
			{
				throw new InvalidDataException("The release response is too large.");
			}

			payload.Write(buffer, 0, count);
		}

		using var document = JsonDocument.Parse(payload.ToArray());
		var root = document.RootElement;

		if (root.ValueKind != JsonValueKind.Object ||
			!root.TryGetProperty("draft", out var draft) || draft.ValueKind != JsonValueKind.False ||
			!root.TryGetProperty("prerelease", out var prerelease) || prerelease.ValueKind != JsonValueKind.False ||
			!root.TryGetProperty("tag_name", out var tag) || tag.ValueKind != JsonValueKind.String ||
			!ReleaseVersion.TryParse(tag.GetString(), out var version))
		{
			throw new InvalidDataException("GitHub did not return a published release version.");
		}

		// Construct the destination from our repository and tag, never from response-supplied URLs.
		return new GitHubRelease(version, new Uri(ReleasePagePrefix + Uri.EscapeDataString(tag.GetString()!)));
	}
}
