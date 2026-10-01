using System.Text.Json.Serialization;

namespace CodexSwitch.Proxy;

public sealed class ProxyHealthResponse
{
    public string Status { get; set; } = "stopped";

    public string Endpoint { get; set; } = "";

    public string ActiveProviderId { get; set; } = "";

    public string ActiveProviderProtocol { get; set; } = "";

    public long Requests { get; set; }

    public long Errors { get; set; }
}

public sealed class ModelsListResponse
{
    public string Object { get; set; } = "list";

    public ModelInfoResponse[] Data { get; set; } = [];
}

public sealed class ModelInfoResponse
{
    public string Id { get; set; } = "";

    public string Object { get; set; } = "model";

    public long Created { get; set; }

    [JsonPropertyName("display_name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DisplayName { get; set; }

    public string OwnedBy { get; set; } = "codexswitch";

    [JsonPropertyName("capabilities")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ModelCapabilities? Capabilities { get; set; }
}

public sealed class ModelCapabilities
{
    [JsonPropertyName("context_window")]
    public int ContextWindow { get; set; }

    [JsonPropertyName("reasoning_efforts")]
    public string[] ReasoningEfforts { get; set; } = [];

    [JsonPropertyName("vision")]
    public bool Vision { get; set; }

    [JsonPropertyName("tool_calls")]
    public bool ToolCalls { get; set; }

    [JsonPropertyName("pricing")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ModelPricingInfo? Pricing { get; set; }
}

public sealed class ModelPricingInfo
{
    [JsonPropertyName("currency")]
    public string Currency { get; set; } = "USD";

    [JsonPropertyName("input_per_million")]
    public decimal? InputPerMillion { get; set; }

    [JsonPropertyName("cached_input_per_million")]
    public decimal? CachedInputPerMillion { get; set; }

    [JsonPropertyName("output_per_million")]
    public decimal? OutputPerMillion { get; set; }
}

public sealed class UsageLogRecord
{
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    public string RequestId { get; set; } = "";

    public ClientAppKind ClientApp { get; set; } = ClientAppKind.Codex;

    public string ProviderId { get; set; } = "";

    public string Protocol { get; set; } = "";

    public string RequestModel { get; set; } = "";

    public string BilledModel { get; set; } = "";

    public bool Stream { get; set; }

    public bool FastMode { get; set; }

    public string? ServiceTier { get; set; }

    public UsageTokens Usage { get; set; }

    public decimal CostMultiplier { get; set; } = 1m;

    public decimal EstimatedCost { get; set; }

    public long DurationMs { get; set; }

    public long UpstreamDurationMs { get; set; }

    public int RetryCount { get; set; }

    public string FinalProvider { get; set; } = "";

    public string ConversionStage { get; set; } = "";

    public int StatusCode { get; set; }

    public string? Error { get; set; }
}

public sealed class CodexAuthFile
{
    [JsonPropertyName("auth_mode")]
    public string AuthMode { get; set; } = "apikey";

    [JsonPropertyName("OPENAI_API_KEY")]
    public string OpenAiApiKey { get; set; } = "";
}
