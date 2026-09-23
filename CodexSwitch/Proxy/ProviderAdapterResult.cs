using Microsoft.AspNetCore.Http;

namespace CodexSwitch.Proxy;

public enum ProviderAdapterResultKind
{
    Success,
    NonRetryableFailure,
    RetryableFailureBeforeResponseStarted,
    ResponseAlreadyStartedFailure
}

public sealed record ProviderAdapterResult(
    ProviderAdapterResultKind Kind,
    int StatusCode,
    string? Error,
    TimeSpan? RetryAfter)
{
    public bool IsRetryableBeforeResponseStarted =>
        Kind == ProviderAdapterResultKind.RetryableFailureBeforeResponseStarted;

    public bool CountsAsCircuitFailure =>
        Kind is ProviderAdapterResultKind.RetryableFailureBeforeResponseStarted or
            ProviderAdapterResultKind.ResponseAlreadyStartedFailure;

    public static ProviderAdapterResult Success() =>
        new(ProviderAdapterResultKind.Success, StatusCodes.Status200OK, null, null);

    public static ProviderAdapterResult NonRetryableFailure(int statusCode, string? error = null) =>
        new(ProviderAdapterResultKind.NonRetryableFailure, statusCode, error, null);

    public static ProviderAdapterResult RetryableFailureBeforeResponseStarted(
        int statusCode,
        string? error,
        TimeSpan? retryAfter = null) =>
        new(ProviderAdapterResultKind.RetryableFailureBeforeResponseStarted, statusCode, error, retryAfter);

    public static ProviderAdapterResult ResponseAlreadyStartedFailure(int statusCode, string? error) =>
        new(ProviderAdapterResultKind.ResponseAlreadyStartedFailure, statusCode, error, null);
}
