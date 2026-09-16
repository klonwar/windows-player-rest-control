using WindowsPlayerControl.Application;
using WindowsPlayerControl.Domain;

namespace WindowsPlayerControl.Application.Tests;

public sealed class MediaServiceTests
{
    [Fact]
    public async Task GetState_merges_media_and_audio_state()
    {
        var media = new FakeMediaController(new MediaState(
            MediaAvailability.Available,
            PlaybackState.Playing,
            null,
            null,
            "Chrome",
            "Title",
            "Artist",
            "Album",
            DateTimeOffset.UtcNow));
        var audio = new FakeAudioController((0.42, false));

        var state = await new MediaService(media, audio).GetStateAsync();

        Assert.Equal(MediaAvailability.Available, state.Availability);
        Assert.Equal(PlaybackState.Playing, state.Playback);
        Assert.Equal(0.42, state.Volume);
        Assert.False(state.Muted);
        Assert.Equal("Title", state.Title);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public async Task SetVolume_rejects_values_outside_home_assistant_range(double value)
    {
        var audio = new FakeAudioController((0.5, false));
        var error = await new MediaService(new FakeMediaController(MediaState.Unavailable(DateTimeOffset.UtcNow)), audio)
            .SetVolumeAsync(value);

        Assert.NotNull(error);
        Assert.Equal(MediaErrorCode.InvalidVolume, error!.Code);
        Assert.Equal(0, audio.SetVolumeCalls);
    }

    private sealed class FakeMediaController(MediaState state) : IMediaController
    {
        public Task<MediaState> GetStateAsync(CancellationToken cancellationToken = default) => Task.FromResult(state);

        public Task<MediaError?> ExecuteAsync(MediaCommand command, CancellationToken cancellationToken = default) =>
            Task.FromResult<MediaError?>(null);
    }

    private sealed class FakeAudioController((double? Volume, bool? Muted) state) : ISystemAudio
    {
        public int SetVolumeCalls { get; private set; }

        public Task<(double? Volume, bool? Muted)> GetStateAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(state);

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

