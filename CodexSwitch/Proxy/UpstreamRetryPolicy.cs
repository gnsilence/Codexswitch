using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using CodexSwitch.Models;

namespace CodexSwitch.Proxy;

public static class UpstreamRetryPolicy
{
    public const int MaximumRetries = 5;
    public const int MaximumBaseDelaySeconds = 10;
    public static readonly TimeSpan MaximumDelay = TimeSpan.FromSeconds(30);

    public static int ResolveMaxRetries(NetworkSettings settings)
    {
        return settings.RetryEnabled
            ? Math.Clamp(settings.MaxRetries, 0, MaximumRetries)
            : 0;
    }

    public static TimeSpan CalculateDelay(
        NetworkSettings settings,
        int retryNumber,
        TimeSpan? retryAfter = null)
    {
        if (retryAfter is { } requestedDelay && requestedDelay > TimeSpan.Zero)
            return requestedDelay > MaximumDelay ? MaximumDelay : requestedDelay;

        var baseDelaySeconds = Math.Clamp(
            settings.RetryBaseDelaySeconds,
            1,
            MaximumBaseDelaySeconds);
        var exponent = Math.Clamp(retryNumber - 1, 0, MaximumRetries);
        var seconds = Math.Min(
            MaximumDelay.TotalSeconds,
            baseDelaySeconds * Math.Pow(2, exponent));
        return TimeSpan.FromSeconds(seconds);
    }

    public static bool ShouldRetryWebSocketException(Exception exception, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return false;

        var statusCode = TryGetStatusCode(exception);
        if (statusCode is { } code)
            return ProtocolAdapterCommon.IsTransientStatusCode(code);

        if (IsUnsupportedWebSocketHandshake(exception))
            return false;

        return ProtocolAdapterCommon.IsTransientException(exception, cancellationToken) ||
            exception is WebSocketException;
    }

    public static bool IsUnsupportedWebSocketHandshake(Exception exception)
    {
        if (TryGetStatusCode(exception) is { } statusCode)
        {
            return statusCode is HttpStatusCode.BadRequest or
                HttpStatusCode.Unauthorized or
                HttpStatusCode.Forbidden or
                HttpStatusCode.NotFound or
                HttpStatusCode.NotImplemented;
        }

        for (var current = exception; current is not null; current = current.InnerException!)
        {
            if (current is WebSocketException &&
                (current.Message.Contains("400", StringComparison.OrdinalIgnoreCase) ||
                 current.Message.Contains("BadRequest", StringComparison.OrdinalIgnoreCase) ||
                 current.Message.Contains("401", StringComparison.OrdinalIgnoreCase) ||
                 current.Message.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase) ||
                 current.Message.Contains("403", StringComparison.OrdinalIgnoreCase) ||
                 current.Message.Contains("Forbidden", StringComparison.OrdinalIgnoreCase) ||
                 current.Message.Contains("404", StringComparison.OrdinalIgnoreCase) ||
                 current.Message.Contains("NotFound", StringComparison.OrdinalIgnoreCase) ||
                 current.Message.Contains("501", StringComparison.OrdinalIgnoreCase) ||
                 current.Message.Contains("NotImplemented", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            if (current.InnerException is null)
                break;
        }

        return false;
    }

    public static HttpStatusCode? TryGetStatusCode(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException!)
        {
            if (current is HttpRequestException { StatusCode: { } statusCode })
                return statusCode;

            if (current.InnerException is null)
                break;
        }

        return null;
    }
}
