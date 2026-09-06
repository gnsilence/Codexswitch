using Microsoft.AspNetCore.Http;

namespace CodexSwitch.Proxy;

public interface IProviderProtocolAdapter
{
    ProviderProtocol Protocol { get; }

    Task<ProviderAdapterResult> HandleResponsesAsync(ProviderRequestContext context, CancellationToken cancellationToken);

    async Task<ProviderAdapterResult> HandleMessagesAsync(ProviderRequestContext context, CancellationToken cancellationToken)
    {
        await ProtocolAdapterCommon.WriteJsonErrorAsync(
            context.HttpContext,
            StatusCodes.Status501NotImplemented,
            $"Provider protocol {Protocol} does not support /v1/messages yet.",
            cancellationToken);
        return ProviderAdapterResult.NonRetryableFailure(StatusCodes.Status501NotImplemented);
    }
}
