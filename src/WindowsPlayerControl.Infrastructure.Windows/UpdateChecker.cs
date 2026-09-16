using System.Net.Http.Headers;
using System.Text.Json;

namespace WindowsPlayerControl.Infrastructure.Windows;

public sealed record LatestRelease(string Version, string Url);

public sealed class UpdateChecker : IDisposable
{
    private const string LatestReleaseApi =
        "https://api.github.com/repos/klonwar/windows-player-rest-control/releases/latest";
    private readonly HttpClient client = new()
    {
        Timeout = TimeSpan.FromSeconds(5)
    };

    public UpdateChecker()
    {
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("WindowsPlayerControl", "1.0"));
    }

    public async Task<LatestRelease?> GetLatestReleaseAsync(CancellationToken cancellationToken = default)
    {
        using var response = await client.GetAsync(LatestReleaseApi, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var root = document.RootElement;
        if (!root.TryGetProperty("tag_name", out var tagProperty)
            || !root.TryGetProperty("html_url", out var urlProperty))
        {
            throw new InvalidOperationException("GitHub returned an invalid release response.");
        }

        var tag = tagProperty.GetString();
        var url = urlProperty.GetString();
        if (string.IsNullOrWhiteSpace(tag) || string.IsNullOrWhiteSpace(url))
        {
            throw new InvalidOperationException("GitHub returned an invalid release response.");
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var releaseUri)
            || releaseUri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(releaseUri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("GitHub returned an invalid release URL.");
        }

        var version = tag.TrimStart('v');
        return Version.TryParse(version, out _) ? new LatestRelease(version, url) : null;
    }

    public void Dispose() => client.Dispose();
}
