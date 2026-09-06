using System.Net.Http;
using System.Text;

namespace CodexSwitch.Services;

public sealed class CodexQuotaProbeService
{
    private const int DefaultTimeoutSeconds = 30;

    private readonly HttpClient _httpClient;
    private readonly ProviderAuthService _authService;

    public CodexQuotaProbeService(HttpClient httpClient, ProviderAuthService authService)
    {
        _httpClient = httpClient;
        _authService = authService;
    }

    public async Task<CodexQuotaProbeResult> ProbeAsync(
        ProviderConfig provider,
        string? accountId,
        CancellationToken cancellationToken)
    {
        var account = ResolveAccount(provider, accountId);
        if (provider.AuthMode != ProviderAuthMode.OAuth || account is null)
            return CodexQuotaProbeResult.Failed("Codex OAuth account is not available.");

        var token = await _authService.ResolveAccessTokenAsync(
            provider,
            account.Id,
            forceRefresh: false,
            cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
            return CodexQuotaProbeResult.Failed("Codex OAuth access token is empty.");

        if (!TryBuildResponsesUri(provider.BaseUrl, out var uri, out var error))
            return CodexQuotaProbeResult.Failed(error);

        var model = ResolveProbeModel(provider);
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(DefaultTimeoutSeconds));

            using var request = new HttpRequestMessage(HttpMethod.Post, uri);
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
            request.Headers.Accept.ParseAdd("application/json");
            request.Headers.Accept.ParseAdd("text/event-stream");
            request.Content = new StringContent(CreateProbeBody(model), Encoding.UTF8, "application/json");

            foreach (var header in ResolveRequestOverrideHeaders(provider, account, model))
            {
                if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value))
                    request.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutCts.Token);
            var quotaUpdated = _authService.UpdateAccountQuotaFromHeaders(provider, account.Id, response.Headers);
            if (quotaUpdated)
                return CodexQuotaProbeResult.Updated(
                    response.IsSuccessStatusCode
                        ? "Codex quota refreshed."
                        : $"Codex quota refreshed from HTTP {(int)response.StatusCode} response headers.");

            if (!response.IsSuccessStatusCode)
                return CodexQuotaProbeResult.Failed($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}".Trim());

            return CodexQuotaProbeResult.Failed("Codex quota headers were not present in the response.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return CodexQuotaProbeResult.Failed("Codex quota query timed out.");
        }
        catch (HttpRequestException ex)
        {
            return CodexQuotaProbeResult.Failed(ex.Message);
        }
        catch (IOException ex)
        {
            return CodexQuotaProbeResult.Failed(ex.Message);
        }
    }

    private static OAuthAccountConfig? ResolveAccount(ProviderConfig provider, string? accountId)
    {
        if (provider.AuthMode != ProviderAuthMode.OAuth)
            return null;

        var enabledAccounts = provider.OAuthAccounts.Where(account => account.IsEnabled).ToArray();
        if (enabledAccounts.Length == 0)
            return null;

        if (!string.IsNullOrWhiteSpace(accountId))
        {
            var selected = enabledAccounts.FirstOrDefault(account =>
                string.Equals(account.Id, accountId, StringComparison.OrdinalIgnoreCase));
            if (selected is not null)
                return selected;
        }

        return enabledAccounts.FirstOrDefault(account =>
            string.Equals(account.Id, provider.ActiveAccountId, StringComparison.OrdinalIgnoreCase)) ??
            enabledAccounts[0];
    }

    private static bool TryBuildResponsesUri(string baseUrl, out Uri uri, out string error)
    {
        var url = baseUrl.TrimEnd('/');
        if (!url.EndsWith("/responses", StringComparison.OrdinalIgnoreCase))
            url += "/responses";

        if (Uri.TryCreate(url, UriKind.Absolute, out uri!) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            error = "";
            return true;
        }

        error = "Codex provider base URL is invalid.";
        uri = null!;
        return false;
    }

    private static string ResolveProbeModel(ProviderConfig provider)
    {
        if (!string.IsNullOrWhiteSpace(provider.DefaultModel))
            return provider.DefaultModel.Trim();

        return provider.Models.FirstOrDefault(model => !string.IsNullOrWhiteSpace(model.Id))?.Id ??
            "gpt-5.1-codex";
    }

    private static string CreateProbeBody(string model)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("model", model);
            writer.WritePropertyName("input");
            writer.WriteStartArray();
            writer.WriteStartObject();
            writer.WriteString("type", "message");
            writer.WriteString("role", "user");
            writer.WriteString("content", "Hi");
            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WriteBoolean("stream", true);
            writer.WriteBoolean("store", false);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static IReadOnlyDictionary<string, string> ResolveRequestOverrideHeaders(
        ProviderConfig provider,
        OAuthAccountConfig account,
        string model)
    {
        var overrides = provider.RequestOverrides;
        if (overrides is null || overrides.Headers.Count == 0)
            return new Dictionary<string, string>();

        if (string.Equals(provider.BuiltinId, ProviderTemplateCatalog.CodexOAuthBuiltinId, StringComparison.OrdinalIgnoreCase) &&
            !ProviderTemplateCatalog.IsChatGptCodexBackend(provider.BaseUrl))
        {
            return new Dictionary<string, string>();
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sessionId = "cs_quota_" + Guid.NewGuid().ToString("N")[..20];
        foreach (var header in overrides.Headers)
        {
            var value = header.Value
                .Replace("{{sessionId}}", sessionId, StringComparison.Ordinal)
                .Replace("{{model}}", model, StringComparison.Ordinal)
                .Replace("{{chatgptAccountId}}", account.ChatgptAccountId ?? "", StringComparison.Ordinal);
            if (!string.IsNullOrWhiteSpace(header.Key) && !string.IsNullOrWhiteSpace(value))
                headers[header.Key] = value;
        }

        return headers;
    }
}

public sealed record CodexQuotaProbeResult(bool Success, bool QuotaUpdated, string Message)
{
    public static CodexQuotaProbeResult Updated(string message)
    {
        return new CodexQuotaProbeResult(true, true, message);
    }

    public static CodexQuotaProbeResult Failed(string message)
    {
        return new CodexQuotaProbeResult(false, false, message);
    }
}
