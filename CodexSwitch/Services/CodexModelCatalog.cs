using System.Text.Json.Serialization;

namespace CodexSwitch.Services;

public sealed class CodexModelCatalog
{
    [JsonPropertyName("models")]
    public List<CodexModelCatalogEntry> Models { get; set; } = [];
}

public sealed class CodexModelCatalogEntry
{
    [JsonPropertyName("slug")]
    public string Slug { get; set; } = "";

    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("base_instructions")]
    public string BaseInstructions { get; set; } = "";

    [JsonPropertyName("model_messages")]
    public CodexModelMessages ModelMessages { get; set; } = new();

    [JsonPropertyName("default_reasoning_level")]
    public string DefaultReasoningLevel { get; set; } = "high";

    [JsonPropertyName("supported_reasoning_levels")]
    public List<CodexReasoningLevel> SupportedReasoningLevels { get; set; } = [];

    [JsonPropertyName("shell_type")]
    public string ShellType { get; set; } = "shell_command";

    [JsonPropertyName("visibility")]
    public string Visibility { get; set; } = "list";

    [JsonPropertyName("supported_in_api")]
    public bool SupportedInApi { get; set; } = true;

    [JsonPropertyName("priority")]
    public int Priority { get; set; }

    [JsonPropertyName("additional_speed_tiers")]
    public List<string> AdditionalSpeedTiers { get; set; } = [];

    [JsonPropertyName("service_tiers")]
    public List<CodexServiceTier> ServiceTiers { get; set; } = [];

    [JsonPropertyName("availability_nux")]
    public string? AvailabilityNux { get; set; }

    [JsonPropertyName("upgrade")]
    public string? Upgrade { get; set; }

    [JsonPropertyName("supports_reasoning_summaries")]
    public bool SupportsReasoningSummaries { get; set; } = true;

    [JsonPropertyName("default_reasoning_summary")]
    public string DefaultReasoningSummary { get; set; } = "none";

    [JsonPropertyName("support_verbosity")]
    public bool SupportVerbosity { get; set; } = true;

    [JsonPropertyName("default_verbosity")]
    public string DefaultVerbosity { get; set; } = "low";

    [JsonPropertyName("apply_patch_tool_type")]
    public string ApplyPatchToolType { get; set; } = "freeform";

    [JsonPropertyName("web_search_tool_type")]
    public string WebSearchToolType { get; set; } = "text_and_image";

    [JsonPropertyName("truncation_policy")]
    public CodexTruncationPolicy TruncationPolicy { get; set; } = new();

    [JsonPropertyName("supports_parallel_tool_calls")]
    public bool SupportsParallelToolCalls { get; set; } = true;

    [JsonPropertyName("supports_image_detail_original")]
    public bool SupportsImageDetailOriginal { get; set; } = true;

    [JsonPropertyName("context_window")]
    public int ContextWindow { get; set; } = 128_000;

    [JsonPropertyName("max_context_window")]
    public int MaxContextWindow { get; set; } = 128_000;

    [JsonPropertyName("effective_context_window_percent")]
    public int EffectiveContextWindowPercent { get; set; } = 95;

    [JsonPropertyName("experimental_supported_tools")]
    public List<string> ExperimentalSupportedTools { get; set; } = [];

    [JsonPropertyName("input_modalities")]
    public List<string> InputModalities { get; set; } = ["text", "image"];

    [JsonPropertyName("supports_search_tool")]
    public bool SupportsSearchTool { get; set; } = true;
}

public sealed class CodexModelMessages
{
    [JsonPropertyName("instructions_template")]
    public string InstructionsTemplate { get; set; } = "You are Codex, a coding agent.";

    [JsonPropertyName("instructions_variables")]
    public Dictionary<string, string> InstructionsVariables { get; set; } = new()
    {
        ["personality_default"] = "",
        ["personality_friendly"] = "",
        ["personality_pragmatic"] = ""
    };
}

public sealed class CodexReasoningLevel
{
    [JsonPropertyName("effort")]
    public string Effort { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";
}

public sealed class CodexServiceTier
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";
}

public sealed class CodexTruncationPolicy
{
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "tokens";

    [JsonPropertyName("limit")]
    public int Limit { get; set; } = 10_000;
}
