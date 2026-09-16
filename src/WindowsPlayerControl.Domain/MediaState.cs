namespace WindowsPlayerControl.Domain;

public enum MediaAvailability
{
    Available,
    Unavailable
}

public enum PlaybackState
{
    Playing,
    Paused,
    Stopped,
    Idle,
    Unknown
}

public sealed record MediaState(
    MediaAvailability Availability,
    PlaybackState Playback,
    double? Volume,
    bool? Muted,
    string? Application,
    string? Title,
    string? Artist,
    string? Album,
    DateTimeOffset ObservedAt)
{
    public static MediaState Unavailable(DateTimeOffset now) => new(
        MediaAvailability.Unavailable,
        PlaybackState.Unknown,
        null,
        null,
        null,
        null,
        null,
        null,
        now);
}

public enum MediaErrorCode
{
    MediaSessionUnavailable,
    StateUnknown,
    OperationRejected,
    InvalidVolume
}

public sealed record MediaError(MediaErrorCode Code, string Message);

