using Microsoft.AspNetCore.Http;
using CodexSwitch.Models;
using CodexSwitch.Services;
using System.Security.Cryptography;
using System.Text;

namespace CodexSwitch.Proxy;

public sealed class ProviderRequestContext
{
    private readonly JsonDocument? _requestDocument;

    public ProviderRequestContext(
        HttpContext httpContext,
        AppConfig appConfig,
        ClientAppKind clientApp,
        ProviderConfig provider,
        ModelRouteConfig? model,
        ProviderCostSettings costSettings,
        string? accessToken,
        ProviderAuthService providerAuthService,
        JsonDocument requestDocument,
        ResponsesConversationStateStore responseStateStore,
        UsageMeter usageMeter,
        PriceCalculator priceCalculator,
        UsageLogWriter usageLogWriter)
    {
        HttpContext = httpContext;
        AppConfig = appConfig;
        ClientApp = clientApp;
        Provider = provider;
        Model = model;
        CostSettings = costSettings;
        AccessToken = accessToken;
        ProviderAuthService = providerAuthService;
        _requestDocument = requestDocument;
        ResponseStateStore = responseStateStore;
        UsageMeter = usageMeter;
        PriceCalculator = priceCalculator;
        UsageLogWriter = usageLogWriter;
    }

    public ProviderRequestContext(
        HttpContext httpContext,
        AppConfig appConfig,
        ClientAppKind clientApp,
        ProviderConfig provider,
        ModelRouteConfig? model,
        ProviderCostSettings costSettings,
        string? accessToken,
        ProviderAuthService providerAuthService,
        ResponsesRequestSnapshot requestSnapshot,
        ResponsesConversationStateStore responseStateStore,
        UsageMeter usageMeter,
        PriceCalculator priceCalculator,
        UsageLogWriter usageLogWriter)
    {
        HttpContext = httpContext;
        AppConfig = appConfig;
        ClientApp = clientApp;
        Provider = provider;
        Model = model;
        CostSettings = costSettings;
        AccessToken = accessToken;
        ProviderAuthService = providerAuthService;
        RequestSnapshot = requestSnapshot;
        ResponseStateStore = responseStateStore;
        UsageMeter = usageMeter;
        PriceCalculator = priceCalculator;
        UsageLogWriter = usageLogWriter;
    }

    public HttpContext HttpContext { get; }

    public AppConfig AppConfig { get; }

    public ClientAppKind ClientApp { get; }

    public ProviderConfig Provider { get; }

    public ModelRouteConfig? Model { get; }

    public ProviderCostSettings CostSettings { get; }

    public string? AccessToken { get; private set; }

    public ProviderAuthService ProviderAuthService { get; }

    public ResponsesRequestSnapshot? RequestSnapshot { get; }

    public JsonDocument RequestDocument =>
        _requestDocument ??
        RequestSnapshot?.RequestDocument ??
        throw new InvalidOperationException("A request document is not available.");

    public JsonElement RequestRoot => RequestDocument.RootElement;

    public ResponsesConversationStateStore ResponseStateStore { get; }

    public UsageMeter UsageMeter { get; }

    public PriceCalculator PriceCalculator { get; }

    public UsageLogWriter UsageLogWriter { get; }

    public string? ResolveAuthorizationToken()
    {
        return Provider.AuthMode == ProviderAuthMode.OAuth
            ? AccessToken
            : Provider.ApiKey;
    }

    public async Task<bool> TryForceRefreshAuthAsync(CancellationToken cancellationToken)
    {
        if (Provider.AuthMode != ProviderAuthMode.OAuth)
            return false;

        var refreshed = await ProviderAuthService.RefreshActiveAccountAsync(Provider, force: true, cancellationToken);
        if (string.IsNullOrWhiteSpace(refreshed))
            return false;

        AccessToken = refreshed;
        return true;
    }

    public IReadOnlyDictionary<string, string> ResolveRequestOverrideHeaders(bool preferPreviousResponseId = true)
    {
        var overrides = Provider.RequestOverrides;
        if (overrides is null || overrides.Headers.Count == 0)
            return new Dictionary<string, string>();

        if (string.Equals(Provider.BuiltinId, ProviderTemplateCatalog.CodexOAuthBuiltinId, StringComparison.OrdinalIgnoreCase) &&
            !ProviderTemplateCatalog.IsChatGptCodexBackend(Provider.BaseUrl))
        {
            return new Dictionary<string, string>();
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sessionId = ResolveSessionId(preferPreviousResponseId);
        var chatgptAccountId = Provider.AuthMode == ProviderAuthMode.OAuth
            ? ProviderAuthService.GetActiveAccount(Provider)?.ChatgptAccountId
            : null;

        foreach (var header in overrides.Headers)
        {
            var value = ResolveTemplate(header.Value, sessionId, chatgptAccountId);
            if (!string.IsNullOrWhiteSpace(value))
                headers[header.Key] = value;
        }

        return headers;
    }

    private string ResolveSessionId(bool preferPreviousResponseId)
    {
        if (preferPreviousResponseId)
        {
            var previousResponseId = RequestSnapshot?.PreviousResponseId ?? TryGetString(RequestRoot, "previous_response_id");
            if (!string.IsNullOrWhiteSpace(previousResponseId))
                return previousResponseId;
        }

        var bytes = RequestSnapshot is not null
            ? ComputeSessionHash(Provider.Id, RequestSnapshot.Body.Span)
            : ComputeSessionHash(Provider.Id, RequestRoot);
        return "cs_" + Convert.ToHexString(bytes)[..24].ToLowerInvariant();
    }

    private static byte[] ComputeSessionHash(string providerId, ReadOnlySpan<byte> requestBody)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(providerId));
        hash.AppendData([0]);
        hash.AppendData(requestBody);
        return hash.GetHashAndReset();
    }

    private static byte[] ComputeSessionHash(string providerId, JsonElement root)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(providerId));
        hash.AppendData([0]);

        using (var stream = new HashingWriteStream(hash))
        using (var writer = new Utf8JsonWriter(stream))
        {
            root.WriteTo(writer);
        }

        return hash.GetHashAndReset();
    }

    private string ResolveTemplate(string value, string sessionId, string? chatgptAccountId)
    {
        return value
            .Replace("{{sessionId}}", sessionId, StringComparison.Ordinal)
            .Replace("{{model}}", Model?.Id ?? Provider.DefaultModel, StringComparison.Ordinal)
            .Replace("{{chatgptAccountId}}", chatgptAccountId ?? "", StringComparison.Ordinal);
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private sealed class HashingWriteStream(IncrementalHash hash) : Stream
    {
        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            hash.AppendData(buffer.AsSpan(offset, count));
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            hash.AppendData(buffer);
        }
    }
}
