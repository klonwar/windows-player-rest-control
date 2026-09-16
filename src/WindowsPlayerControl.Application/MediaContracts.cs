using WindowsPlayerControl.Domain;

namespace WindowsPlayerControl.Application;

public interface IMediaController
{
    Task<MediaState> GetStateAsync(CancellationToken cancellationToken = default);
    Task<MediaError?> ExecuteAsync(MediaCommand command, CancellationToken cancellationToken = default);
}

public interface ISystemAudio
{
    Task<(double? Volume, bool? Muted)> GetStateAsync(CancellationToken cancellationToken = default);
    Task<MediaError?> SetVolumeAsync(double volume, CancellationToken cancellationToken = default);
    Task<MediaError?> AdjustVolumeAsync(double delta, CancellationToken cancellationToken = default);
    Task<MediaError?> SetMutedAsync(bool muted, CancellationToken cancellationToken = default);
}

public enum MediaCommand
{
    Play,
    Pause,
    Toggle,
    Next,
    Previous
}

public sealed class MediaService(IMediaController mediaController, ISystemAudio systemAudio)
{
    public async Task<MediaState> GetStateAsync(CancellationToken cancellationToken = default)
    {
        var media = await mediaController.GetStateAsync(cancellationToken);
        var audio = await systemAudio.GetStateAsync(cancellationToken);
        return media with { Volume = audio.Volume, Muted = audio.Muted };
    }

    public Task<MediaError?> ExecuteAsync(MediaCommand command, CancellationToken cancellationToken = default) =>
        mediaController.ExecuteAsync(command, cancellationToken);

    public Task<MediaError?> SetVolumeAsync(double volume, CancellationToken cancellationToken = default) =>
        volume is < 0 or > 1
            ? Task.FromResult<MediaError?>(new(MediaErrorCode.InvalidVolume, "Volume must be between 0 and 1."))
            : systemAudio.SetVolumeAsync(volume, cancellationToken);

    public Task<MediaError?> AdjustVolumeAsync(double delta, CancellationToken cancellationToken = default) =>
        systemAudio.AdjustVolumeAsync(delta, cancellationToken);

    public Task<MediaError?> SetMutedAsync(bool muted, CancellationToken cancellationToken = default) =>
        systemAudio.SetMutedAsync(muted, cancellationToken);
}

