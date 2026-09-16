using System.Net;
using System.Net.Sockets;
using System.Net.Http.Json;
using WindowsPlayerControl.Api;
using WindowsPlayerControl.Application;
using WindowsPlayerControl.Domain;

namespace WindowsPlayerControl.Application.Tests;

public sealed class ApiHostTests
{
    [Fact]
    public async Task State_rejects_wrong_secret_and_returns_json_state_for_valid_secret()
    {
        await using var host = await StartHostAsync();
        using var client = new HttpClient { BaseAddress = host.BaseAddress };

        var unauthorized = await client.GetAsync("api/v1/wrong/state");
        var response = await client.GetAsync("api/v1/test-secret/state");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"playback\":\"paused\"", body);
        Assert.DoesNotContain("test-secret", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Put_volume_rejects_invalid_value_without_calling_audio()
    {
        var audio = new FakeAudioController();
        await using var host = await StartHostAsync(audio: audio);
        using var client = new HttpClient { BaseAddress = host.BaseAddress };

        var response = await client.PutAsJsonAsync("api/v1/test-secret/volume", new { value = 2.0 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, audio.SetVolumeCalls);
    }

    [Fact]
    public async Task Media_command_maps_unavailable_session_to_conflict()
    {
        await using var host = await StartHostAsync();
        using var client = new HttpClient { BaseAddress = host.BaseAddress };

        var response = await client.PostAsync("api/v1/test-secret/media/play", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("media_session_unavailable", body);
        Assert.DoesNotContain("test-secret", body, StringComparison.Ordinal);
    }

    private static async Task<TestHost> StartHostAsync(FakeAudioController? audio = null)
    {
        var port = GetFreePort();
        var media = new MediaService(
            new FakeMediaController(),
            audio ?? new FakeAudioController());
        var api = ApiHost.Create(new ApiHostOptions("127.0.0.1", port, "test-secret"), media);
        await api.StartAsync();
        return new TestHost(api, new Uri($"http://127.0.0.1:{port}/"));
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed class TestHost(ApiHost api, Uri baseAddress) : IAsyncDisposable
    {
        public Uri BaseAddress { get; } = baseAddress;

        public async ValueTask DisposeAsync()
        {
            await api.StopAsync();
            await api.DisposeAsync();
        }
    }

    private sealed class FakeMediaController : IMediaController
    {
        public Task<MediaState> GetStateAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new MediaState(MediaAvailability.Available, PlaybackState.Paused, null, null, "Test", "Title", null, null, DateTimeOffset.UtcNow));

        public Task<MediaError?> ExecuteAsync(MediaCommand command, CancellationToken cancellationToken = default) =>
            Task.FromResult<MediaError?>(new(MediaErrorCode.MediaSessionUnavailable, "No session"));
    }

    private sealed class FakeAudioController : ISystemAudio
    {
        public int SetVolumeCalls { get; private set; }

        public Task<(double? Volume, bool? Muted)> GetStateAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<(double?, bool?)>((0.5, false));

        public Task<MediaError?> SetVolumeAsync(double volume, CancellationToken cancellationToken = default)
        {
            SetVolumeCalls++;
            return Task.FromResult<MediaError?>(null);
        }

        public Task<MediaError?> AdjustVolumeAsync(double delta, CancellationToken cancellationToken = default) =>
            Task.FromResult<MediaError?>(null);

        public Task<MediaError?> SetMutedAsync(bool muted, CancellationToken cancellationToken = default) =>
            Task.FromResult<MediaError?>(null);
    }
}
