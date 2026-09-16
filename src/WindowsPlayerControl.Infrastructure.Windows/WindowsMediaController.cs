using Windows.Media.Control;
using Windows.ApplicationModel;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using NAudio.CoreAudioApi;
using WindowsPlayerControl.Application;
using WindowsPlayerControl.Domain;

namespace WindowsPlayerControl.Infrastructure.Windows;

public sealed class WindowsMediaController : IMediaController, IMediaArtwork
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private GlobalSystemMediaTransportControlsSessionManager? manager;

    public async Task<MediaState> GetStateAsync(CancellationToken cancellationToken = default)
    {
        GlobalSystemMediaTransportControlsSession? session;
        try
        {
            session = await GetCurrentSessionAsync(cancellationToken);
        }
        catch (COMException)
        {
            return MediaState.Unavailable(DateTimeOffset.UtcNow);
        }
        if (session is null)
        {
            return MediaState.Unavailable(DateTimeOffset.UtcNow);
        }

        try
        {
            var playback = session.GetPlaybackInfo().PlaybackStatus;
            var properties = await session.TryGetMediaPropertiesAsync().AsTask(cancellationToken);
            return new(
                MediaAvailability.Available,
                MapPlayback(playback),
                null,
                null,
                ResolveApplicationName(session.SourceAppUserModelId),
                NullIfEmpty(properties.Title),
                NullIfEmpty(properties.Artist),
                NullIfEmpty(properties.AlbumTitle),
                DateTimeOffset.UtcNow);
        }
        catch (COMException)
        {
            return MediaState.Unavailable(DateTimeOffset.UtcNow);
        }
    }

    public async Task<MediaError?> ExecuteAsync(MediaCommand command, CancellationToken cancellationToken = default)
    {
        GlobalSystemMediaTransportControlsSession? session;
        try
        {
            session = await GetCurrentSessionAsync(cancellationToken);
        }
        catch (COMException)
        {
            return new(MediaErrorCode.MediaSessionUnavailable, "Windows media session service is unavailable.");
        }
        if (session is null)
        {
            return new(MediaErrorCode.MediaSessionUnavailable, "No controllable Windows media session is available.");
        }

        if (command == MediaCommand.Toggle)
        {
            var state = MapPlayback(session.GetPlaybackInfo().PlaybackStatus);
            var toggledCommand = state switch
            {
                PlaybackState.Playing => MediaCommand.Pause,
                PlaybackState.Paused => MediaCommand.Play,
                _ => (MediaCommand?)null
            };
            if (toggledCommand is null)
            {
                return new(MediaErrorCode.StateUnknown, "Playback state is unknown; toggle cannot choose an action.");
            }

            command = toggledCommand.Value;
        }

        bool succeeded;
        try
        {
            succeeded = command switch
            {
                MediaCommand.Play => await session.TryPlayAsync().AsTask(cancellationToken),
                MediaCommand.Pause => await session.TryPauseAsync().AsTask(cancellationToken),
                MediaCommand.Next => await session.TrySkipNextAsync().AsTask(cancellationToken),
                MediaCommand.Previous => await session.TrySkipPreviousAsync().AsTask(cancellationToken),
                _ => false
            };
        }
        catch (COMException)
        {
            return new(MediaErrorCode.OperationRejected, "Windows rejected the media operation.");
        }

        return succeeded ? null : new(MediaErrorCode.OperationRejected, "Windows rejected the media operation.");
    }

    public async Task<MediaArtwork?> GetArtworkAsync(CancellationToken cancellationToken = default)
    {
        GlobalSystemMediaTransportControlsSession? session;
        try
        {
            session = await GetCurrentSessionAsync(cancellationToken);
        }
        catch (COMException)
        {
            return null;
        }
        if (session is null)
        {
            return null;
        }

        try
        {
            var properties = await session.TryGetMediaPropertiesAsync().AsTask(cancellationToken);
            if (properties.Thumbnail is null)
            {
                return null;
            }

            using var stream = await properties.Thumbnail.OpenReadAsync();
            using var input = stream.AsStreamForRead();
            using var output = new MemoryStream();
            await input.CopyToAsync(output, cancellationToken);
            var contentType = string.IsNullOrWhiteSpace(stream.ContentType) ? "image/jpeg" : stream.ContentType;
            return output.Length == 0 ? null : new MediaArtwork(output.ToArray(), contentType);
        }
        catch (COMException)
        {
            return null;
        }
    }

    private async Task<GlobalSystemMediaTransportControlsSession?> GetCurrentSessionAsync(CancellationToken cancellationToken)
    {
        if (manager is null)
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask(cancellationToken);
            }
            finally
            {
                gate.Release();
            }
        }

        return manager.GetCurrentSession();
    }

    private static PlaybackState MapPlayback(GlobalSystemMediaTransportControlsSessionPlaybackStatus status) => status switch
    {
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => PlaybackState.Playing,
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => PlaybackState.Paused,
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped => PlaybackState.Stopped,
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed => PlaybackState.Idle,
        _ => PlaybackState.Unknown
    };

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string? ResolveApplicationName(string? appUserModelId)
    {
        if (string.IsNullOrWhiteSpace(appUserModelId))
        {
            return null;
        }

        try
        {
            return AppInfo.GetFromAppUserModelId(appUserModelId).DisplayInfo.DisplayName;
        }
        catch (Exception)
        {
            return appUserModelId;
        }
    }
}

public sealed class CoreAudioController : ISystemAudio, IDisposable
{
    private readonly object sync = new();
    private MMDeviceEnumerator? enumerator;
    private MMDevice? device;

    public Task<(double? Volume, bool? Muted)> GetStateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            lock (sync)
            {
                var endpoint = GetEndpoint();
                return Task.FromResult<(double?, bool?)>((endpoint.AudioEndpointVolume.MasterVolumeLevelScalar, endpoint.AudioEndpointVolume.Mute));
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            return Task.FromResult<(double?, bool?)>((null, null));
        }
    }

    public Task<MediaError?> SetVolumeAsync(double volume, CancellationToken cancellationToken = default)
    {
        try
        {
            lock (sync) GetEndpoint().AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp((float)volume, 0, 1);
            return Task.FromResult<MediaError?>(null);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            return Task.FromResult<MediaError?>(UnavailableError());
        }
    }

    public Task<MediaError?> AdjustVolumeAsync(double delta, CancellationToken cancellationToken = default)
    {
        try
        {
            lock (sync)
            {
                var endpoint = GetEndpoint().AudioEndpointVolume;
                endpoint.MasterVolumeLevelScalar = Math.Clamp(endpoint.MasterVolumeLevelScalar + (float)delta, 0, 1);
            }

            return Task.FromResult<MediaError?>(null);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            return Task.FromResult<MediaError?>(UnavailableError());
        }
    }

    public Task<MediaError?> SetMutedAsync(bool muted, CancellationToken cancellationToken = default)
    {
        try
        {
            lock (sync) GetEndpoint().AudioEndpointVolume.Mute = muted;
            return Task.FromResult<MediaError?>(null);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            return Task.FromResult<MediaError?>(UnavailableError());
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            device?.Dispose();
            enumerator?.Dispose();
            device = null;
            enumerator = null;
        }
    }

    private MMDevice GetEndpoint() => device ??= (enumerator ??= new MMDeviceEnumerator()).GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

    private static MediaError UnavailableError() => new(MediaErrorCode.OperationRejected, "System audio is not available.");
}
