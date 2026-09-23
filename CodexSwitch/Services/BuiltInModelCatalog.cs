using System.Collections.ObjectModel;
using CodexSwitch.Models;

namespace CodexSwitch.Services;

public static class BuiltInModelCatalog
{
    public const string PricingSchemaVersion = "1.7";
    public const long OpenAiLongContextThresholdTokens = 272_000;
    public const long XiaomiLongContextThresholdTokens = 256_000;

    public static IReadOnlyList<ProviderTemplateModel> AiossModels => OpenAiOfficialModels;

    public static IReadOnlyList<ProviderTemplateModel> RoutinAiModels { get; } =
    [
        OpenAiResponsesRoute("gpt-6-astra", "GPT-6 Astra", serviceTier: "priority", fastMode: true),
        OpenAiResponsesRoute("gpt-6-sol", "GPT-6 Sol", serviceTier: "priority", fastMode: true),
        OpenAiResponsesRoute("gpt-6-luna", "GPT-6 Luna", serviceTier: "priority", fastMode: true),
        OpenAiResponsesRoute("gpt-5.6-sol", "GPT-5.6 Sol", serviceTier: "priority", fastMode: true),
        OpenAiResponsesRoute("gpt-5.6-terra", "GPT-5.6 Terra", serviceTier: "priority", fastMode: true),
        OpenAiResponsesRoute("gpt-5.6-luna", "GPT-5.6 Luna", serviceTier: "priority", fastMode: true),
        OpenAiResponsesRoute("gpt-5.5", "GPT-5.5", serviceTier: "priority", fastMode: true),
        OpenAiResponsesRoute("gpt-5.4", "GPT-5.4", serviceTier: "priority", fastMode: true),
        OpenAiResponsesRoute("deepseek-v4-flash", "DeepSeek V4 Flash", serviceTier: "priority", fastMode: true),
        OpenAiResponsesRoute("deepseek-v4-pro", "DeepSeek V4 Pro", serviceTier: "priority", fastMode: true),
        OpenAiResponsesRoute("mimo-v2-flash", "MiMo V2 Flash", serviceTier: "priority", fastMode: true),
        OpenAiResponsesRoute("mimo-v2-pro", "MiMo V2 Pro", serviceTier: "priority", fastMode: true),
        OpenAiResponsesRoute("mimo-v2.5-pro", "MiMo V2.5 Pro", serviceTier: "priority", fastMode: true)
    ];

    public static IReadOnlyList<ProviderTemplateModel> OpenAiOfficialModels { get; } =
    [
        OpenAiResponsesRoute("gpt-6-astra", "GPT-6 Astra"),
        OpenAiResponsesRoute("gpt-6-sol", "GPT-6 Sol"),
        OpenAiResponsesRoute("gpt-6-luna", "GPT-6 Luna"),
        OpenAiResponsesRoute("gpt-5.6-sol", "GPT-5.6 Sol"),
        OpenAiResponsesRoute("gpt-5.6-terra", "GPT-5.6 Terra"),
        OpenAiResponsesRoute("gpt-5.6-luna", "GPT-5.6 Luna"),
        OpenAiResponsesRoute("gpt-5.5", "GPT-5.5"),
        OpenAiResponsesRoute("gpt-5.4", "GPT-5.4")
    ];

    public static IReadOnlyList<ProviderTemplateModel> AnthropicModels { get; } =
    [
        AnthropicRoute("claude-fable-5-1", "Claude Fable 5.1"),
        AnthropicRoute("claude-opus-5", "Claude Opus 5"),
        AnthropicRoute("claude-sonnet-5", "Claude Sonnet 5"),
        AnthropicRoute("claude-haiku-4-5", "Claude Haiku 4.5", upstreamModel: "claude-haiku-4-5-20251001"),
        AnthropicRoute("sonnet", "Claude Code Sonnet", upstreamModel: "claude-sonnet-4-5-20250929"),
        AnthropicRoute("opus", "Claude Code Opus", upstreamModel: "claude-opus-4-5-20251101"),
        AnthropicRoute("haiku", "Claude Code Haiku", upstreamModel: "claude-haiku-4-5-20251001"),
        AnthropicRoute("claude-opus-4-7", "Claude Opus 4.7"),
        AnthropicRoute("claude-opus-4-6", "Claude Opus 4.6"),
        AnthropicRoute("claude-opus-4-5", "Claude Opus 4.5", upstreamModel: "claude-opus-4-5-20251101"),
        AnthropicRoute("claude-opus-4-1", "Claude Opus 4.1", upstreamModel: "claude-opus-4-1-20250805"),
        AnthropicRoute("claude-opus-4", "Claude Opus 4", upstreamModel: "claude-opus-4-20250514"),
        AnthropicRoute("claude-sonnet-4-6", "Claude Sonnet 4.6"),
        AnthropicRoute("claude-sonnet-4-5", "Claude Sonnet 4.5", upstreamModel: "claude-sonnet-4-5-20250929"),
        AnthropicRoute("claude-sonnet-4", "Claude Sonnet 4", upstreamModel: "claude-sonnet-4-20250514"),
        AnthropicRoute("claude-3-7-sonnet", "Claude Sonnet 3.7", upstreamModel: "claude-3-7-sonnet-20250219"),
        AnthropicRoute("claude-3-5-sonnet", "Claude Sonnet 3.5", upstreamModel: "claude-3-5-sonnet-20241022"),
        AnthropicRoute("claude-3-sonnet", "Claude Sonnet 3", upstreamModel: "claude-3-sonnet-20240229"),
        AnthropicRoute("claude-haiku-4-5", "Claude Haiku 4.5", upstreamModel: "claude-haiku-4-5-20251001"),
        AnthropicRoute("claude-3-5-haiku", "Claude Haiku 3.5", upstreamModel: "claude-3-5-haiku-20241022"),
        AnthropicRoute("claude-3-opus", "Claude Opus 3", upstreamModel: "claude-3-opus-20240229"),
        AnthropicRoute("claude-3-haiku", "Claude Haiku 3", upstreamModel: "claude-3-haiku-20240307")
    ];

    public static IReadOnlyList<ProviderTemplateModel> DeepSeekModels { get; } =
    [
        OpenAiResponsesRoute("deepseek-flash", "DeepSeek Flash"),
        OpenAiResponsesRoute("deepseek-v4-flash", "DeepSeek V4 Flash"),
        OpenAiResponsesRoute("deepseek-v4-pro", "DeepSeek V4 Pro"),
        OpenAiResponsesRoute("deepseek-chat", "DeepSeek Chat"),
        OpenAiResponsesRoute("deepseek-reasoner", "DeepSeek Reasoner")
    ];

    public static IReadOnlyList<ProviderTemplateModel> XiaomiModels { get; } =
    [
        OpenAiResponsesRoute("mimo-v2.5-pro", "MiMo V2.5 Pro"),
        OpenAiResponsesRoute("mimo-v2-pro", "MiMo V2 Pro"),
        OpenAiResponsesRoute("mimo-v2.5", "MiMo V2.5"),
        OpenAiResponsesRoute("mimo-v2-omni", "MiMo V2 Omni"),
        OpenAiResponsesRoute("mimo-v2-flash", "MiMo V2 Flash")
    ];

    public static IReadOnlyList<ProviderTemplateModel> GrokModels { get; } =
    [
        OpenAiResponsesRoute("grok-4.6", "Grok 4.6")
    ];

    public static FastModePricing CreateFastModePricing()
    {
        return new FastModePricing
        {
            DefaultMultiplier = 2m,
            ModelOverrides = CreateFastModeOverrides()
        };
    }

    public static Dictionary<string, decimal> CreateFastModeOverrides()
    {
        return new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["gpt-5.5*"] = 2.5m
        };
    }

    public static Collection<ModelPricingRule> CreatePricingRules()
    {
        return
        [
            CreateRule(
                "gpt-6-astra",
                "GPT-6 Astra",
                "openai",
                Tiered(OpenAiLongContextThresholdTokens, 10m, 20m),
                Tiered(OpenAiLongContextThresholdTokens, 1m, 2m),
                Tiered(OpenAiLongContextThresholdTokens, 12.50m, 25m),
                Tiered(OpenAiLongContextThresholdTokens, 50m, 75m),
                aliases: ["gpt-6-astra*"],
                contextPricingThresholdTokens: OpenAiLongContextThresholdTokens),
            CreateRule(
                "gpt-6-sol",
                "GPT-6 Sol",
                "openai",
                Tiered(OpenAiLongContextThresholdTokens, 2m, 4m),
                Tiered(OpenAiLongContextThresholdTokens, 0.20m, 0.40m),
                Tiered(OpenAiLongContextThresholdTokens, 2.50m, 5m),
                Tiered(OpenAiLongContextThresholdTokens, 10m, 15m),
                aliases: ["gpt-6-sol*"],
                contextPricingThresholdTokens: OpenAiLongContextThresholdTokens),
            CreateRule(
                "gpt-6-luna",
                "GPT-6 Luna",
                "openai",
                Tiered(OpenAiLongContextThresholdTokens, 0.10m, 0.20m),
                Tiered(OpenAiLongContextThresholdTokens, 0.01m, 0.02m),
                Tiered(OpenAiLongContextThresholdTokens, 0.125m, 0.25m),
                Tiered(OpenAiLongContextThresholdTokens, 0.50m, 0.75m),
                aliases: ["gpt-6-luna*"],
                contextPricingThresholdTokens: OpenAiLongContextThresholdTokens),
            CreateRule(
                "gpt-5.6-sol",
                "GPT-5.6 Sol",
                "openai",
                Tiered(OpenAiLongContextThresholdTokens, 5m, 10m),
                Tiered(OpenAiLongContextThresholdTokens, 0.50m, 1m),
                Tiered(OpenAiLongContextThresholdTokens, 6.25m, 12.50m),
                Tiered(OpenAiLongContextThresholdTokens, 30m, 45m),
                aliases: ["gpt-5.6-sol*"],
                contextPricingThresholdTokens: OpenAiLongContextThresholdTokens),
            CreateRule(
                "gpt-5.5",
                "GPT-5.5",
                "openai",
                Tiered(OpenAiLongContextThresholdTokens, 5m, 10m),
                Tiered(OpenAiLongContextThresholdTokens, 0.50m, 1m),
                new TokenPriceTable(),
                Tiered(OpenAiLongContextThresholdTokens, 30m, 45m),
                aliases: ["gpt-5.5*"],
                contextPricingThresholdTokens: OpenAiLongContextThresholdTokens),
            CreateRule(
                "gpt-5.6-terra",
                "GPT-5.6 Terra",
                "openai",
                Tiered(OpenAiLongContextThresholdTokens, 2m, 4m),
                Tiered(OpenAiLongContextThresholdTokens, 0.20m, 0.40m),
                Tiered(OpenAiLongContextThresholdTokens, 2.50m, 5m),
                Tiered(OpenAiLongContextThresholdTokens, 12m, 18m),
                aliases: ["gpt-5.6-terra*"],
                contextPricingThresholdTokens: OpenAiLongContextThresholdTokens),
            CreateRule(
                "gpt-5.6-luna",
                "GPT-5.6 Luna",
                "openai",
                Tiered(OpenAiLongContextThresholdTokens, 0.20m, 0.40m),
                Tiered(OpenAiLongContextThresholdTokens, 0.02m, 0.04m),
                Tiered(OpenAiLongContextThresholdTokens, 0.25m, 0.50m),
                Tiered(OpenAiLongContextThresholdTokens, 1.20m, 1.80m),
                aliases: ["gpt-5.6-luna*"],
                contextPricingThresholdTokens: OpenAiLongContextThresholdTokens),
            CreateRule(
                "codex-auto-review",
                "Codex Auto Review",
                "openai",
                Tiered(OpenAiLongContextThresholdTokens, 0.20m, 0.40m),
                Tiered(OpenAiLongContextThresholdTokens, 0.02m, 0.04m),
                Tiered(OpenAiLongContextThresholdTokens, 0.25m, 0.50m),
                Tiered(OpenAiLongContextThresholdTokens, 1.20m, 1.80m),
                aliases: ["codex-auto-review*"],
                contextPricingThresholdTokens: OpenAiLongContextThresholdTokens),

            CreateRule("claude-fable-5-1", "Claude Fable 5.1", "claude", Flat(10m), Flat(0.25m), Flat(12.50m), Flat(50m), cacheCreationInput1Hour: Flat(20m)),
            CreateRule("claude-opus-5", "Claude Opus 5", "claude", Flat(5m), Flat(0.50m), Flat(6.25m), Flat(25m), cacheCreationInput1Hour: Flat(10m)),
            CreateRule("claude-sonnet-5", "Claude Sonnet 5", "claude", Flat(2m), Flat(0.20m), Flat(2.50m), Flat(10m), cacheCreationInput1Hour: Flat(4m)),
            CreateRule("claude-haiku-4-5-20251001", "Claude Haiku 4.5", "claude", Flat(1m), Flat(0.10m), Flat(1.25m), Flat(5m), aliases: ["claude-haiku-4-5"], cacheCreationInput1Hour: Flat(2m)),

            // DeepSeek uses time-based peak/off-peak pricing; this static rule uses peak rates to avoid underestimating cost.
            CreateRule(
                "deepseek-flash",
                "DeepSeek Flash",
                "deepseek",
                Flat(0.30m),
                Flat(0.006m),
                new TokenPriceTable(),
                Flat(1.20m),
                aliases: ["deepseek-v4-flash", "deepseek-v4-flash-vision-exp", "deepseek-chat", "deepseek-reasoner"]),
            CreateRule(
                "deepseek-v4-pro",
                "DeepSeek V4 Pro",
                "deepseek",
                Flat(1.32m),
                Flat(0.044m),
                new TokenPriceTable(),
                Flat(3.96m)),

            CreateRule(
                "mimo-v2.5-pro",
                "MiMo V2.5 Pro",
                "xiaomi",
                Tiered(XiaomiLongContextThresholdTokens, 1.00m, 2.00m),
                Tiered(XiaomiLongContextThresholdTokens, 0.20m, 0.40m),
                new TokenPriceTable(),
                Tiered(XiaomiLongContextThresholdTokens, 3.00m, 6.00m),
                aliases: ["mimo-v2-pro"]),
            CreateRule(
                "mimo-v2.5",
                "MiMo V2.5",
                "xiaomi",
                Tiered(XiaomiLongContextThresholdTokens, 0.40m, 0.80m),
                Tiered(XiaomiLongContextThresholdTokens, 0.08m, 0.16m),
                new TokenPriceTable(),
                Tiered(XiaomiLongContextThresholdTokens, 2.00m, 4.00m)),
            CreateRule(
                "mimo-v2-omni",
                "MiMo V2 Omni",
                "xiaomi",
                Flat(0.40m),
                Flat(0.08m),
                new TokenPriceTable(),
                Flat(2.00m)),
            CreateRule(
                "mimo-v2-flash",
                "MiMo V2 Flash",
                "xiaomi",
                Flat(0.10m),
                Flat(0.01m),
                new TokenPriceTable(),
                Flat(0.30m))
        ];
    }

    private static ProviderTemplateModel OpenAiResponsesRoute(
        string id,
        string displayName,
        string? upstreamModel = null,
        string? serviceTier = null,
        bool fastMode = false)
    {
        return new ProviderTemplateModel
        {
            Id = id,
            DisplayName = displayName,
            Protocol = ProviderProtocol.OpenAiResponses,
            UpstreamModel = upstreamModel,
            ServiceTier = serviceTier,
            FastMode = fastMode
        };
    }

    private static ProviderTemplateModel OpenAiChatRoute(
        string id,
        string displayName,
        string? upstreamModel = null,
        string? serviceTier = null,
        bool fastMode = false)
    {
        return new ProviderTemplateModel
        {
            Id = id,
            DisplayName = displayName,
            Protocol = ProviderProtocol.OpenAiChat,
            UpstreamModel = upstreamModel,
            ServiceTier = serviceTier,
            FastMode = fastMode
        };
    }

    private static ProviderTemplateModel AnthropicRoute(
        string id,
        string displayName,
        string? upstreamModel = null)
    {
        return new ProviderTemplateModel
        {
            Id = id,
            DisplayName = displayName,
            Protocol = ProviderProtocol.AnthropicMessages,
            UpstreamModel = upstreamModel
        };
    }

    private static ModelPricingRule CreateRule(
        string id,
        string displayName,
        string iconSlug,
        TokenPriceTable input,
        TokenPriceTable cachedInput,
        TokenPriceTable cacheCreationInput,
        TokenPriceTable output,
        IEnumerable<string>? aliases = null,
        long? contextPricingThresholdTokens = null,
        TokenPriceTable? cacheCreationInput1Hour = null)
    {
        var rule = new ModelPricingRule
        {
            Id = id,
            DisplayName = displayName,
            IconSlug = iconSlug,
            ContextPricingThresholdTokens = contextPricingThresholdTokens,
            Input = input,
            CachedInput = cachedInput,
            CacheCreationInput = cacheCreationInput,
            CacheCreationInput1Hour = cacheCreationInput1Hour ?? new TokenPriceTable(),
            Output = output
        };

        if (aliases is not null)
        {
            foreach (var alias in aliases)
                rule.Aliases.Add(alias);
        }

        return rule;
    }

    private static TokenPriceTable Flat(decimal price)
    {
        var table = new TokenPriceTable();
        table.Tiers.Add(new PricingTier { UpToTokens = null, PricePerUnit = price });
        return table;
    }

    private static TokenPriceTable Tiered(long threshold, decimal firstPrice, decimal overflowPrice)
    {
        var table = new TokenPriceTable();
        table.Tiers.Add(new PricingTier
        {
            UpToTokens = threshold,
            PricePerUnit = firstPrice
        });
        table.Tiers.Add(new PricingTier
        {
            UpToTokens = null,
            PricePerUnit = overflowPrice
        });
        return table;
    }
}
