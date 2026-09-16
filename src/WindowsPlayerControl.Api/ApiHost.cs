using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WindowsPlayerControl.Application;
using WindowsPlayerControl.Domain;

namespace WindowsPlayerControl.Api;

public sealed record ApiHostOptions(string BindAddress, int Port, string Secret);

public sealed class ApiHost : IAsyncDisposable
{
    private readonly WebApplication application;

    private ApiHost(WebApplication application) => this.application = application;

    public static ApiHost Create(ApiHostOptions options, MediaService mediaService)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(ApiHost).Assembly.GetName().Name
        });
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));
        // Do not enable ASP.NET request logging: the secret is part of the route URL.
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls($"http://{options.BindAddress}:{options.Port}");

        var app = builder.Build();
        app.MapMediaRoutes(options.Secret, mediaService);
        return new ApiHost(app);
    }

    public Task StartAsync(CancellationToken cancellationToken = default) => application.StartAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken = default) => application.StopAsync(cancellationToken);

    public ValueTask DisposeAsync() => application.DisposeAsync();
}

internal static class MediaRouteExtensions
{
    public static void MapMediaRoutes(this IEndpointRouteBuilder endpoints, string secret, MediaService media)
    {
        var group = endpoints.MapGroup("/api/v1/{secret}").AddEndpointFilter(new SecretFilter(secret));

        group.MapGet("/state", async (CancellationToken cancellationToken) =>
            Results.Ok(await media.GetStateAsync(cancellationToken)));
        group.MapGet("/artwork", async (CancellationToken cancellationToken) =>
        {
            var artwork = await media.GetArtworkAsync(cancellationToken);
            return artwork is null
                ? Results.NotFound()
                : Results.File(artwork.Data, artwork.ContentType);
        });

        MapCommand(group, "/media/play", MediaCommand.Play, media);
        MapCommand(group, "/media/pause", MediaCommand.Pause, media);
        MapCommand(group, "/media/toggle", MediaCommand.Toggle, media);
        MapCommand(group, "/media/next", MediaCommand.Next, media);
        MapCommand(group, "/media/previous", MediaCommand.Previous, media);

        group.MapPut("/volume", async (SetVolumeRequest request, CancellationToken cancellationToken) =>
            ToResult(await media.SetVolumeAsync(request.Value, cancellationToken)));
        group.MapPost("/volume/up", async (CancellationToken cancellationToken) =>
            ToResult(await media.AdjustVolumeAsync(0.05, cancellationToken)));
        group.MapPost("/volume/down", async (CancellationToken cancellationToken) =>
            ToResult(await media.AdjustVolumeAsync(-0.05, cancellationToken)));
        group.MapPost("/volume/mute", async (CancellationToken cancellationToken) =>
            ToResult(await media.SetMutedAsync(true, cancellationToken)));
        group.MapPost("/volume/unmute", async (CancellationToken cancellationToken) =>
            ToResult(await media.SetMutedAsync(false, cancellationToken)));
    }

    private static void MapCommand(IEndpointRouteBuilder group, string pattern, MediaCommand command, MediaService media) =>
        group.MapPost(pattern, async (CancellationToken cancellationToken) =>
            ToResult(await media.ExecuteAsync(command, cancellationToken)));

    private static IResult ToResult(MediaError? error) => error is null
        ? Results.NoContent()
        : error.Code switch
        {
            MediaErrorCode.InvalidVolume => Results.BadRequest(new { error = ErrorName(error.Code), message = error.Message }),
            MediaErrorCode.StateUnknown or MediaErrorCode.MediaSessionUnavailable => Results.Conflict(new { error = ErrorName(error.Code), message = error.Message }),
            _ => Results.StatusCode(StatusCodes.Status502BadGateway)
        };

    private static string ErrorName(MediaErrorCode code) => code switch
    {
        MediaErrorCode.MediaSessionUnavailable => "media_session_unavailable",
        MediaErrorCode.StateUnknown => "state_unknown",
        MediaErrorCode.OperationRejected => "operation_rejected",
        MediaErrorCode.InvalidVolume => "invalid_volume",
        _ => "operation_failed"
    };
}

public sealed record SetVolumeRequest(double Value);

internal sealed class SecretFilter(string expectedSecret) : IEndpointFilter
{
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var supplied = context.HttpContext.Request.RouteValues["secret"]?.ToString() ?? string.Empty;
        var expected = Encoding.UTF8.GetBytes(expectedSecret);
        var actual = Encoding.UTF8.GetBytes(supplied);
        return CryptographicOperations.FixedTimeEquals(expected, actual)
            ? next(context)
            : ValueTask.FromResult<object?>(Results.Unauthorized());
    }
}
