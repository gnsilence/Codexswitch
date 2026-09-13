using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

namespace CodexSwitch.Services;

public sealed class ProviderModelDiscoveryService
{
    private readonly HttpClient _httpClient;
    private readonly ProviderAuthService _authService;

    public ProviderModelDiscoveryService(HttpClient httpClient, ProviderAuthService authService)
    {
        _httpClient = httpClient;
        _authService = authService;
    }

    public async Task<IReadOnlyList<ModelRouteConfig>> FetchRoutesAsync(
        ProviderConfig provider,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(provider.BaseUrl))
            throw new InvalidOperationException("Provider API URL is empty.");

        var token = await _authService.ResolveAccessTokenAsync(provider, forceRefresh: false, cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("Provider API key or OAuth token is empty.");

        var useAnthropicHeaders = provider.Protocol == ProviderProtocol.AnthropicMessages;
        HttpResponseMessage? response = null;
        try
        {
            response = await SendModelsRequestAsync(provider, token, useAnthropicHeaders, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            if (ShouldRetryAnthropicWithBearer(provider, response.StatusCode))
            {
                response.Dispose();
                response = await SendModelsRequestAsync(provider, token, useAnthropicHeaders: false, cancellationToken);
                content = await response.Content.ReadAsStringAsync(cancellationToken);
            }

            response.EnsureSuccessStatusCode();

            var payload = JsonSerializer.Deserialize(content, CodexSwitchJsonContext.Default.ModelsListResponse);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var models = payload?.Data
                .Select((model, index) => new
                {
                    Id = model.Id?.Trim() ?? "",
                    DisplayName = model.DisplayName?.Trim(),
                    Index = index
                })
                .Where(item => !string.IsNullOrWhiteSpace(item.Id) && seen.Add(item.Id))
                .OrderBy(item => ModelSortGroup(item.Id))
                .ThenBy(item => item.Index)
                .ToArray() ?? [];

            return models
                .Select(model => new ModelRouteConfig
                {
                    Id = model.Id,
                    DisplayName = string.IsNullOrWhiteSpace(model.DisplayName) ? model.Id : model.DisplayName,
                    Protocol = provider.Protocol,
                    Cost = new ProviderCostSettings()
                })
                .ToArray();
        }
        finally
        {
            response?.Dispose();
        }
    }

    private async Task<HttpResponseMessage> SendModelsRequestAsync(
        ProviderConfig provider,
        string token,
        bool useAnthropicHeaders,
        CancellationToken cancellationToken)
    {
        var includeAnthropicLimit = provider.Protocol == ProviderProtocol.AnthropicMessages;
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildModelsUri(provider.BaseUrl, includeAnthropicLimit));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (useAnthropicHeaders)
        {
            request.Headers.TryAddWithoutValidation("x-api-key", token);
            request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
        }
        else
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await _httpClient.SendAsync(request, cancellationToken);
    }

    private static bool ShouldRetryAnthropicWithBearer(ProviderConfig provider, HttpStatusCode statusCode)
    {
        return provider.Protocol == ProviderProtocol.AnthropicMessages &&
            (statusCode == HttpStatusCode.Unauthorized || statusCode == HttpStatusCode.Forbidden);
    }

    private static Uri BuildModelsUri(string baseUrl, bool includeAnthropicLimit)
    {
        var uri = baseUrl.Trim().TrimEnd('/') + "/models";
        if (includeAnthropicLimit)
            uri += "?limit=1000";

        return new Uri(uri, UriKind.Absolute);
    }

    private static int ModelSortGroup(string id)
    {
        if (id.StartsWith("gpt-", StringComparison.OrdinalIgnoreCase) ||
            id.StartsWith("codex-", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (id.StartsWith("claude-", StringComparison.OrdinalIgnoreCase))
            return 1;
        if (id.StartsWith("deepseek-", StringComparison.OrdinalIgnoreCase))
            return 2;
        if (id.StartsWith("grok-", StringComparison.OrdinalIgnoreCase))
            return 3;

        return 4;
    }
}
