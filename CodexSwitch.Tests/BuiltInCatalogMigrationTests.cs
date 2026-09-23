using System.Text.Json;
using System.Text.Json.Serialization;
using CodexSwitch.Models;
using CodexSwitch.Proxy;
using CodexSwitch.Services;

namespace CodexSwitch.Tests;

public sealed class BuiltInCatalogMigrationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public void LoadPricing_UpgradesBuiltInCatalogToOfficialDefaults()
    {
        var root = CreateTempDirectory();
        try
        {
            var paths = new AppPaths(root, Path.Combine(root, ".codex"));
            var store = new ConfigurationStore(paths);

            var legacy = new ModelPricingCatalog
            {
                SchemaVersion = "1.0",
                Currency = "USD",
                BillingUnitTokens = 1_000_000,
                FastMode =
                {
                    DefaultMultiplier = 2m,
                    ModelOverrides =
                    {
                        ["gpt-5.5"] = 2.5m
                    }
                },
                Models =
                {
                    new ModelPricingRule
                    {
                        Id = "gpt-5.5",
                        DisplayName = "GPT-5.5",
                        IconSlug = "openai",
                        Input = FlatTable(1.25m),
                        CachedInput = FlatTable(0.125m),
                        Output = FlatTable(10m)
                    },
                    new ModelPricingRule
                    {
                        Id = "gpt-5.3-codex",
                        Input = FlatTable(99m)
                    },
                    new ModelPricingRule
                    {
                        Id = "custom-non-gpt",
                        Input = FlatTable(3m)
                    },
                    new ModelPricingRule
                    {
                        Id = "claude-opus-4-7",
                        Input = FlatTable(99m)
                    }
                }
            };

            WriteJson(paths.PricingPath, legacy);

            var upgraded = store.LoadPricing();

            Assert.Equal(BuiltInModelCatalog.PricingSchemaVersion, upgraded.SchemaVersion);
            Assert.Equal(
                [
                    "gpt-6-astra",
                    "gpt-6-sol",
                    "gpt-6-luna",
                    "gpt-5.6-sol",
                    "gpt-5.5",
                    "gpt-5.6-terra",
                    "gpt-5.6-luna",
                    "codex-auto-review"
                ],
                upgraded.Models
                    .Where(rule => rule.Id.StartsWith("gpt-", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(rule.Id, "codex-auto-review", StringComparison.OrdinalIgnoreCase))
                    .Select(rule => rule.Id));
            Assert.Contains(upgraded.Models, rule => rule.Id == "custom-non-gpt");
            Assert.DoesNotContain(upgraded.Models, rule => rule.Id == "gpt-5.3-codex");
            Assert.DoesNotContain(upgraded.Models, rule => rule.Id == "gpt-5.4");
            Assert.Equal(
                [
                    "claude-fable-5-1",
                    "claude-opus-5",
                    "claude-sonnet-5",
                    "claude-haiku-4-5-20251001"
                ],
                upgraded.Models
                    .Where(rule => rule.Id.StartsWith("claude-", StringComparison.OrdinalIgnoreCase))
                    .Select(rule => rule.Id));
            Assert.DoesNotContain(upgraded.Models, rule => rule.Id == "claude-opus-4-7");
            Assert.DoesNotContain(upgraded.Models, rule => rule.Id == "claude-3-5-sonnet");
            Assert.Contains(upgraded.Models, rule => rule.Id == "deepseek-flash");
            Assert.Contains(upgraded.Models, rule => rule.Id == "deepseek-v4-pro");
            Assert.Contains(upgraded.Models, rule => rule.Id == "mimo-v2.5-pro");
            Assert.Contains(upgraded.Models, rule => rule.Id == "mimo-v2.5");

            var gpt55 = Assert.Single(upgraded.Models, rule => rule.Id == "gpt-5.5");
            Assert.Equal(5m, gpt55.Input.Tiers[0].PricePerUnit);
            Assert.Equal(BuiltInModelCatalog.OpenAiLongContextThresholdTokens, gpt55.Input.Tiers[0].UpToTokens);
            Assert.True(upgraded.FastMode.ModelOverrides.ContainsKey("gpt-5.5*"));
            Assert.False(upgraded.FastMode.ModelOverrides.ContainsKey("gpt-5"));

            var deepSeekFlash = Assert.Single(upgraded.Models, rule => rule.Id == "deepseek-flash");
            Assert.Contains("deepseek-v4-flash", deepSeekFlash.Aliases);
            Assert.Contains("deepseek-chat", deepSeekFlash.Aliases);
            Assert.Contains("deepseek-reasoner", deepSeekFlash.Aliases);

            var mimoPro = Assert.Single(upgraded.Models, rule => rule.Id == "mimo-v2.5-pro");
            Assert.Contains("mimo-v2-pro", mimoPro.Aliases);
            Assert.Equal(BuiltInModelCatalog.XiaomiLongContextThresholdTokens, mimoPro.Input.Tiers[0].UpToTokens);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LoadPricing_RefreshesNewGpt6RulesFromPreviousCatalog()
    {
        var root = CreateTempDirectory();
        try
        {
            var paths = new AppPaths(root, Path.Combine(root, ".codex"));
            var store = new ConfigurationStore(paths);
            WriteJson(paths.PricingPath, new ModelPricingCatalog
            {
                SchemaVersion = "1.6",
                Models =
                {
                    new ModelPricingRule { Id = "gpt-6-sol", Input = FlatTable(99m) },
                    new ModelPricingRule { Id = "custom-model", Input = FlatTable(3m) }
                }
            });

            var upgraded = store.LoadPricing();

            Assert.Equal(BuiltInModelCatalog.PricingSchemaVersion, upgraded.SchemaVersion);
            AssertGptPricing(upgraded.Models, "gpt-6-sol", 2m, 4m, 0.20m, 0.40m, 2.50m, 5m, 10m, 15m);
            AssertGptPricing(upgraded.Models, "gpt-6-luna", 0.10m, 0.20m, 0.01m, 0.02m, 0.125m, 0.25m, 0.50m, 0.75m);
            AssertFlatPrice(Assert.Single(upgraded.Models, rule => rule.Id == "custom-model").Input, 3m);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuiltInPricing_UsesProvidedGptRates()
    {
        var rules = BuiltInModelCatalog.CreatePricingRules();

        AssertGptPricing(rules, "gpt-6-astra", 10m, 20m, 1m, 2m, 12.50m, 25m, 50m, 75m);
        AssertGptPricing(rules, "gpt-6-sol", 2m, 4m, 0.20m, 0.40m, 2.50m, 5m, 10m, 15m);
        AssertGptPricing(rules, "gpt-6-luna", 0.10m, 0.20m, 0.01m, 0.02m, 0.125m, 0.25m, 0.50m, 0.75m);
        AssertGptPricing(rules, "gpt-5.6-sol", 5m, 10m, 0.50m, 1m, 6.25m, 12.50m, 30m, 45m);
        AssertGptPricing(rules, "gpt-5.5", 5m, 10m, 0.50m, 1m, null, null, 30m, 45m);
        AssertGptPricing(rules, "gpt-5.6-terra", 2m, 4m, 0.20m, 0.40m, 2.50m, 5m, 12m, 18m);
        AssertGptPricing(rules, "gpt-5.6-luna", 0.20m, 0.40m, 0.02m, 0.04m, 0.25m, 0.50m, 1.20m, 1.80m);
        AssertGptPricing(rules, "codex-auto-review", 0.20m, 0.40m, 0.02m, 0.04m, 0.25m, 0.50m, 1.20m, 1.80m);

        Assert.DoesNotContain(rules, rule => rule.Id == "gpt-5.4-mini");
        Assert.DoesNotContain(rules, rule => rule.Id == "gpt-5.6");
    }

    [Fact]
    public void AiossTemplates_UseFullOpenAiModelCatalog()
    {
        var aiossPlus = ProviderTemplateCatalog.CreateProvider(ProviderTemplateCatalog.AiossPlusBuiltinId, []);

        Assert.Contains(aiossPlus.Models, route => route.Id == "gpt-6-astra");
        Assert.Contains(aiossPlus.Models, route => route.Id == "gpt-6-sol");
        Assert.Contains(aiossPlus.Models, route => route.Id == "gpt-6-luna");
        Assert.Contains(aiossPlus.Models, route => route.Id == "gpt-5.6-sol");
        Assert.Contains(aiossPlus.Models, route => route.Id == "gpt-5.6-terra");
        Assert.Contains(aiossPlus.Models, route => route.Id == "gpt-5.6-luna");
        Assert.True(aiossPlus.Models.Count > 2);
    }

    [Fact]
    public void BuiltInPricing_UsesCurrentClaudeRates()
    {
        var rules = BuiltInModelCatalog.CreatePricingRules();

        AssertClaudePricing(rules, "claude-fable-5-1", 10m, 0.25m, 12.50m, 20m, 50m);
        AssertClaudePricing(rules, "claude-opus-5", 5m, 0.50m, 6.25m, 10m, 25m);
        AssertClaudePricing(rules, "claude-sonnet-5", 2m, 0.20m, 2.50m, 4m, 10m);
        AssertClaudePricing(rules, "claude-haiku-4-5-20251001", 1m, 0.10m, 1.25m, 2m, 5m);

        var claudeRules = rules.Where(rule =>
            rule.Id.StartsWith("claude-", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(4, claudeRules.Count());
        Assert.Contains("claude-haiku-4-5", Assert.Single(
            rules,
            rule => rule.Id == "claude-haiku-4-5-20251001").Aliases);
    }

    [Fact]
    public void BuiltInPricing_UsesCurrentDeepSeekPeakRates()
    {
        var rules = BuiltInModelCatalog.CreatePricingRules();

        var flash = Assert.Single(rules, rule => rule.Id == "deepseek-flash");
        Assert.Equal("deepseek", flash.IconSlug);
        AssertFlatPrice(flash.Input, 0.30m);
        AssertFlatPrice(flash.CachedInput, 0.006m);
        AssertFlatPrice(flash.Output, 1.20m);
        Assert.Contains("deepseek-v4-flash", flash.Aliases);
        Assert.Contains("deepseek-chat", flash.Aliases);
        Assert.Contains("deepseek-reasoner", flash.Aliases);

        var pro = Assert.Single(rules, rule => rule.Id == "deepseek-v4-pro");
        AssertFlatPrice(pro.Input, 1.32m);
        AssertFlatPrice(pro.CachedInput, 0.044m);
        AssertFlatPrice(pro.Output, 3.96m);
    }

    [Fact]
    public void AnthropicTemplate_IncludesCurrentClaudeRoutesForCodexSync()
    {
        var provider = ProviderTemplateCatalog.CreateProvider(ProviderTemplateCatalog.AnthropicBuiltinId, []);

        Assert.Equal("claude-sonnet-4-5", provider.DefaultModel);
        Assert.Contains(provider.Models, model => model.Id == "claude-fable-5-1");
        Assert.Contains(provider.Models, model => model.Id == "claude-opus-5");
        Assert.Contains(provider.Models, model => model.Id == "claude-sonnet-5");
        Assert.Contains(provider.Models, model => model.Id == "claude-haiku-4-5");
        Assert.All(provider.Models, model => Assert.Equal(ProviderProtocol.AnthropicMessages, model.Protocol));
    }

    [Fact]
    public void LoadConfig_SeedsOnlyRequestedAiossProviders()
    {
        var root = CreateTempDirectory();
        try
        {
            var paths = new AppPaths(root, Path.Combine(root, ".codex"));
            var store = new ConfigurationStore(paths);

            var config = store.LoadConfig();

            Assert.Collection(
                config.Providers,
                provider => AssertAiossProvider(provider, ProviderTemplateCatalog.AiossPlusBuiltinId),
                provider => AssertAiossProvider(provider, ProviderTemplateCatalog.AiossProBuiltinId));
            Assert.Equal(ProviderTemplateCatalog.AiossPlusBuiltinId, config.ActiveCodexProviderId);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LoadConfig_MigratesLegacyAiossMultiplierOnlyOnce()
    {
        var root = CreateTempDirectory();
        try
        {
            var paths = new AppPaths(root, Path.Combine(root, ".codex"));
            var store = new ConfigurationStore(paths);
            var provider = ProviderTemplateCatalog.CreateProvider(ProviderTemplateCatalog.AiossPlusBuiltinId, []);
            provider.Cost!.Multiplier = 1m;
            WriteJson(paths.ConfigPath, new AppConfig
            {
                ActiveProviderId = provider.Id,
                Providers = { provider }
            });

            var migrated = store.LoadConfig();
            var migratedProvider = Assert.Single(migrated.Providers, item => item.Id == provider.Id);
            Assert.Equal(0.13m, migratedProvider.Cost?.Multiplier);

            migratedProvider.Cost!.Multiplier = 1m;
            store.SaveConfig(migrated);

            var reloaded = store.LoadConfig();
            Assert.Equal(1m, Assert.Single(reloaded.Providers, item => item.Id == provider.Id).Cost?.Multiplier);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void VisibleTemplates_HideRoutinAiAndIncludeAioss()
    {
        Assert.Contains(ProviderTemplateCatalog.VisibleTemplates, template => template.Id == ProviderTemplateCatalog.AiossPlusBuiltinId);
        Assert.Contains(ProviderTemplateCatalog.VisibleTemplates, template => template.Id == ProviderTemplateCatalog.AiossProBuiltinId);
        Assert.DoesNotContain(ProviderTemplateCatalog.VisibleTemplates, template => template.Id == ProviderTemplateCatalog.RoutinAiBuiltinId);
        Assert.DoesNotContain(ProviderTemplateCatalog.VisibleTemplates, template => template.Id == ProviderTemplateCatalog.RoutinAiPlanBuiltinId);

        Assert.Contains(UsageQueryTemplateCatalog.VisibleTemplates, template => template.Id == UsageQueryTemplateCatalog.AiossTemplateId);
        Assert.DoesNotContain(UsageQueryTemplateCatalog.VisibleTemplates, template => template.Id == UsageQueryTemplateCatalog.RoutinAiApiKeyTemplateId);
        Assert.DoesNotContain(UsageQueryTemplateCatalog.VisibleTemplates, template => template.Id == UsageQueryTemplateCatalog.RoutinAiPlanTemplateId);
        Assert.NotNull(ProviderTemplateCatalog.Find(ProviderTemplateCatalog.RoutinAiBuiltinId));
        Assert.NotNull(UsageQueryTemplateCatalog.Find(UsageQueryTemplateCatalog.RoutinAiApiKeyTemplateId));
    }

    [Fact]
    public void LoadConfig_PreservesExistingBuiltInProviderModelLists()
    {
        var root = CreateTempDirectory();
        try
        {
            var paths = new AppPaths(root, Path.Combine(root, ".codex"));
            var store = new ConfigurationStore(paths);

            var legacy = new AppConfig
            {
                ActiveProviderId = "openai-official",
                Providers =
                {
                    new ProviderConfig
                    {
                        Id = "openai-official",
                        BuiltinId = ProviderTemplateCatalog.OpenAiOfficialBuiltinId,
                        DisplayName = "OpenAI Official",
                        BaseUrl = "https://api.openai.com/v1",
                        Protocol = ProviderProtocol.OpenAiResponses,
                        DefaultModel = "gpt-5.5",
                        Models =
                        {
                            new ModelRouteConfig { Id = "gpt-5.5", Protocol = ProviderProtocol.OpenAiResponses }
                        }
                    },
                    new ProviderConfig
                    {
                        Id = "anthropic",
                        BuiltinId = ProviderTemplateCatalog.AnthropicBuiltinId,
                        DisplayName = "Anthropic Messages",
                        BaseUrl = "https://api.anthropic.com/v1",
                        Protocol = ProviderProtocol.AnthropicMessages,
                        DefaultModel = "claude-sonnet-4-5",
                        Models =
                        {
                            new ModelRouteConfig { Id = "claude-sonnet-4-5", Protocol = ProviderProtocol.AnthropicMessages }
                        }
                    }
                }
            };

            WriteJson(paths.ConfigPath, legacy);

            var upgraded = store.LoadConfig();
            var openAi = Assert.Single(upgraded.Providers, provider => provider.Id == "openai-official");
            var anthropic = Assert.Single(upgraded.Providers, provider => provider.Id == "anthropic");
            var aiossPlus = Assert.Single(upgraded.Providers, provider =>
                string.Equals(provider.BuiltinId, ProviderTemplateCatalog.AiossPlusBuiltinId, StringComparison.OrdinalIgnoreCase));
            var aiossPro = Assert.Single(upgraded.Providers, provider =>
                string.Equals(provider.BuiltinId, ProviderTemplateCatalog.AiossProBuiltinId, StringComparison.OrdinalIgnoreCase));

            Assert.DoesNotContain(openAi.Models, model => model.Id == "gpt-5.4");
            Assert.Contains(openAi.Models, model => model.Id == "gpt-6-sol");
            Assert.Contains(openAi.Models, model => model.Id == "gpt-6-luna");
            Assert.DoesNotContain(openAi.Models, model => model.Id == "gpt-5.4-mini");
            Assert.DoesNotContain(openAi.Models, model => model.Id == "gpt-5.3-codex");
            AssertDefaultConversion(openAi);
            Assert.DoesNotContain(anthropic.Models, model => model.Id == "claude-opus-4-7");
            Assert.DoesNotContain(anthropic.Models, model => model.Id == "claude-3-5-sonnet");
            AssertDefaultConversion(anthropic);
            AssertAiossProvider(aiossPlus, ProviderTemplateCatalog.AiossPlusBuiltinId);
            AssertAiossProvider(aiossPro, ProviderTemplateCatalog.AiossProBuiltinId);
            Assert.DoesNotContain(upgraded.Providers, provider =>
                string.Equals(provider.BuiltinId, ProviderTemplateCatalog.DeepSeekBuiltinId, StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(upgraded.Providers, provider =>
                string.Equals(provider.BuiltinId, ProviderTemplateCatalog.XiaomiBuiltinId, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DeepSeekTemplate_UsesOpenAiResponsesEndpointAndRoutes()
    {
        var provider = ProviderTemplateCatalog.CreateProvider(ProviderTemplateCatalog.DeepSeekBuiltinId, []);

        Assert.Equal(ProviderTemplateCatalog.AiossBaseUrl, provider.BaseUrl);
        Assert.Equal(ProviderProtocol.OpenAiResponses, provider.Protocol);
        Assert.Equal("deepseek-flash", provider.DefaultModel);
        Assert.True(provider.SupportsClaudeCode);
        Assert.Contains(provider.Models, model => model.Id == "deepseek-flash");
        Assert.Contains(provider.Models, model => model.Id == "deepseek-v4-flash");
        Assert.All(provider.Models, model => Assert.Equal(ProviderProtocol.OpenAiResponses, model.Protocol));
    }

    [Fact]
    public void XiaomiTemplate_UsesOpenAiEndpointAndRoutes()
    {
        var provider = ProviderTemplateCatalog.CreateProvider(ProviderTemplateCatalog.XiaomiBuiltinId, []);

        Assert.Equal(ProviderTemplateCatalog.AiossBaseUrl, provider.BaseUrl);
        Assert.Equal(ProviderProtocol.OpenAiResponses, provider.Protocol);
        Assert.Contains(provider.Models, model => model.Id == "mimo-v2.5-pro");
        Assert.DoesNotContain(provider.Models, model => model.Id == "gpt-5.4");
        AssertDefaultConversion(provider);
    }

    [Fact]
    public void EnsureValidDefaults_PreservesXiaomiTemplateWhenUsingOpenAiEndpoint()
    {
        var provider = ProviderTemplateCatalog.CreateProvider(ProviderTemplateCatalog.XiaomiBuiltinId, []);
        var config = new AppConfig
        {
            ActiveProviderId = provider.Id,
            Providers = { provider }
        };

        ConfigurationStore.EnsureValidDefaults(config);

        Assert.Equal(ProviderTemplateCatalog.XiaomiBuiltinId, provider.BuiltinId);
        Assert.Equal(ProviderTemplateCatalog.AiossBaseUrl, provider.BaseUrl);
        Assert.Equal("mimo-v2.5-pro", provider.DefaultModel);
        Assert.Contains(provider.Models, model => model.Id == "mimo-v2.5-pro");
        Assert.DoesNotContain(provider.Models, model => model.Id == "gpt-5.4");
    }

    [Fact]
    public void LoadConfig_AddsOnlyNewGpt6RoutesToExistingOpenAiBuiltIns()
    {
        var root = CreateTempDirectory();
        try
        {
            var paths = new AppPaths(root, Path.Combine(root, ".codex"));
            var store = new ConfigurationStore(paths);
            var aioss = ProviderTemplateCatalog.CreateProvider(ProviderTemplateCatalog.AiossPlusBuiltinId, []);
            aioss.Models.Remove(aioss.Models.Single(model => model.Id == "gpt-6-luna"));
            aioss.Models.Single(model => model.Id == "gpt-6-sol").DisplayName = "My 6 Sol";
            aioss.Models.Single(model => model.Id == "gpt-5.6-sol").DisplayName = "My Sol";
            var routin = ProviderTemplateCatalog.CreateProvider(ProviderTemplateCatalog.RoutinAiBuiltinId, []);
            routin.Models.Clear();
            var custom = new ProviderConfig
            {
                Id = "custom",
                DefaultModel = "custom-model",
                Models = { new ModelRouteConfig { Id = "custom-model" } }
            };
            WriteJson(paths.ConfigPath, new AppConfig
            {
                SchemaVersion = 3,
                ActiveProviderId = aioss.Id,
                Providers = { aioss, routin, custom }
            });

            var upgraded = store.LoadConfig();
            var updatedAioss = upgraded.Providers.Single(provider => provider.Id == aioss.Id);
            var updatedRoutin = upgraded.Providers.Single(provider => provider.Id == routin.Id);

            Assert.Equal(4, upgraded.SchemaVersion);
            Assert.Equal("My 6 Sol", updatedAioss.Models.Single(model => model.Id == "gpt-6-sol").DisplayName);
            Assert.Equal("My Sol", updatedAioss.Models.Single(model => model.Id == "gpt-5.6-sol").DisplayName);
            Assert.Equal(1, updatedAioss.Models.Count(model => model.Id == "gpt-6-sol"));
            Assert.Equal(1, updatedAioss.Models.Count(model => model.Id == "gpt-6-luna"));
            Assert.True(updatedRoutin.Models.Single(model => model.Id == "gpt-6-sol").Cost?.FastMode);
            Assert.True(updatedRoutin.Models.Single(model => model.Id == "gpt-6-luna").Cost?.FastMode);
            Assert.Single(upgraded.Providers.Single(provider => provider.Id == custom.Id).Models);

            updatedAioss.Models.Remove(updatedAioss.Models.Single(model => model.Id == "gpt-6-luna"));
            store.SaveConfig(upgraded);
            var reloaded = store.LoadConfig();
            Assert.DoesNotContain(reloaded.Providers.Single(provider => provider.Id == aioss.Id).Models,
                model => model.Id == "gpt-6-luna");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void GrokTemplate_UsesAiossEndpointAndGrok46Route()
    {
        var provider = ProviderTemplateCatalog.CreateProvider(ProviderTemplateCatalog.GrokBuiltinId, []);

        Assert.Equal(ProviderTemplateCatalog.AiossBaseUrl, provider.BaseUrl);
        Assert.Equal("grok-4.6", provider.DefaultModel);
        Assert.Equal(ProviderProtocol.OpenAiResponses, provider.Protocol);
        Assert.True(provider.SupportsCodex);
        Assert.False(provider.SupportsClaudeCode);
        Assert.False(provider.SupportsWebSockets == true);

        var route = Assert.Single(provider.Models);
        Assert.Equal("grok-4.6", route.Id);
        Assert.Equal(ProviderProtocol.OpenAiResponses, route.Protocol);
        Assert.Equal(UsageQueryTemplateCatalog.AiossTemplateId, provider.UsageQuery?.TemplateId);
        Assert.True(provider.UsageQuery?.Enabled == true);
    }

    [Fact]
    public void LoadConfig_MigratesAiossOpenAiCompatibleBuiltInsToResponsesProtocol()
    {
        var root = CreateTempDirectory();
        try
        {
            var paths = new AppPaths(root, Path.Combine(root, ".codex"));
            var store = new ConfigurationStore(paths);
            var config = new AppConfig
            {
                SchemaVersion = 2,
                ActiveProviderId = "deepseek",
                Providers =
                {
                    new ProviderConfig
                    {
                        Id = "deepseek",
                        BuiltinId = ProviderTemplateCatalog.DeepSeekBuiltinId,
                        BaseUrl = ProviderTemplateCatalog.AiossBaseUrl,
                        Protocol = ProviderProtocol.OpenAiChat,
                        DefaultModel = "deepseek-v4-flash",
                        Models =
                        {
                            new ModelRouteConfig
                            {
                                Id = "deepseek-v4-flash",
                                Protocol = ProviderProtocol.OpenAiChat
                            }
                        }
                    }
                }
            };

            WriteJson(paths.ConfigPath, config);

            var reloaded = store.LoadConfig();
            var provider = Assert.Single(
                reloaded.Providers,
                item => string.Equals(item.BuiltinId, ProviderTemplateCatalog.DeepSeekBuiltinId, StringComparison.OrdinalIgnoreCase));
            Assert.Equal(4, reloaded.SchemaVersion);
            Assert.Equal(ProviderProtocol.OpenAiResponses, provider.Protocol);
            Assert.All(provider.Models, route => Assert.Equal(ProviderProtocol.OpenAiResponses, route.Protocol));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void NewBuiltInProviderTemplates_UseAiossEndpointAndUsageQuery()
    {
        foreach (var template in ProviderTemplateCatalog.VisibleTemplates.Where(template => !template.IsCustom))
        {
            var provider = ProviderTemplateCatalog.CreateProvider(template.Id, []);

            Assert.Equal(ProviderTemplateCatalog.AiossBaseUrl, provider.BaseUrl);
            Assert.Equal(UsageQueryTemplateCatalog.AiossTemplateId, provider.UsageQuery?.TemplateId);
            Assert.True(provider.UsageQuery?.Enabled == true);
        }
    }

    [Fact]
    public void SaveConfig_PreservesEditedBuiltInProviderFieldsDuringMigration()
    {
        var root = CreateTempDirectory();
        try
        {
            var paths = new AppPaths(root, Path.Combine(root, ".codex"));
            var store = new ConfigurationStore(paths);
            var provider = ProviderTemplateCatalog.CreateProvider(ProviderTemplateCatalog.DeepSeekBuiltinId, []);
            provider.BaseUrl = "https://custom.example/v1";
            provider.Protocol = ProviderProtocol.AnthropicMessages;
            provider.Note = "custom note";
            provider.Website = "https://custom.example";
            provider.SupportsWebSockets = true;
            provider.Cost!.Multiplier = 0.42m;

            var config = new AppConfig
            {
                SchemaVersion = 1,
                ActiveProviderId = provider.Id,
                Providers = { provider }
            };

            store.SaveConfig(config);
            var reloaded = store.LoadConfig();
            var saved = Assert.Single(reloaded.Providers, item => item.Id == provider.Id);

            Assert.Equal(4, reloaded.SchemaVersion);
            Assert.Equal("https://custom.example/v1", saved.BaseUrl);
            Assert.Equal(ProviderProtocol.AnthropicMessages, saved.Protocol);
            Assert.Equal("custom note", saved.Note);
            Assert.Equal("https://custom.example", saved.Website);
            Assert.True(saved.SupportsWebSockets == true);
            Assert.Equal(0.42m, saved.Cost?.Multiplier);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SaveConfig_DoesNotRestoreRemovedModelsForCurrentSchema()
    {
        var root = CreateTempDirectory();
        try
        {
            var paths = new AppPaths(root, Path.Combine(root, ".codex"));
            var store = new ConfigurationStore(paths);
            var provider = ProviderTemplateCatalog.CreateProvider(ProviderTemplateCatalog.GrokBuiltinId, []);
            provider.Models.Clear();

            var config = new AppConfig
            {
                SchemaVersion = 4,
                ActiveProviderId = provider.Id,
                Providers = { provider }
            };

            store.SaveConfig(config);
            var reloaded = store.LoadConfig();

            Assert.Empty(Assert.Single(reloaded.Providers, item => item.Id == provider.Id).Models);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void EnsureValidDefaults_PreservesLegacyXiaomiEndpoint()
    {
        var provider = new ProviderConfig
        {
            Id = "xiaomi-mimo",
            BuiltinId = ProviderTemplateCatalog.XiaomiBuiltinId,
            DisplayName = "Xiaomi MiMo",
            BaseUrl = ProviderTemplateCatalog.XiaomiLegacyBaseUrl,
            Protocol = ProviderProtocol.OpenAiChat,
            DefaultModel = "mimo-v2.5-pro"
        };
        var config = new AppConfig
        {
            ActiveProviderId = provider.Id,
            Providers = { provider }
        };

        ConfigurationStore.EnsureValidDefaults(config);

        Assert.Equal(ProviderTemplateCatalog.XiaomiLegacyBaseUrl, provider.BaseUrl);
        Assert.Equal(ProviderTemplateCatalog.XiaomiBuiltinId, provider.BuiltinId);
        Assert.Contains(provider.Models, model => model.Id == "mimo-v2.5-pro");
        Assert.DoesNotContain(provider.Models, model => model.Id == "gpt-5.4");
    }

    [Fact]
    public void CodexOAuthTemplate_EnablesWebSocketsByDefault()
    {
        var provider = ProviderTemplateCatalog.CreateProvider(ProviderTemplateCatalog.CodexOAuthBuiltinId, []);

        Assert.True(provider.SupportsWebSockets == true);
    }

    [Fact]
    public void LoadConfig_MigratesCodexOAuthWebSocketDefaultButPreservesExplicitFalse()
    {
        var root = CreateTempDirectory();
        try
        {
            var paths = new AppPaths(root, Path.Combine(root, ".codex"));
            var store = new ConfigurationStore(paths);
            var legacy = new AppConfig
            {
                ActiveProviderId = "codex-oauth",
                Providers =
                {
                    new ProviderConfig
                    {
                        Id = "codex-oauth",
                        BuiltinId = ProviderTemplateCatalog.CodexOAuthBuiltinId,
                        DisplayName = "Codex OAuth",
                        BaseUrl = ProviderTemplateCatalog.CodexOAuthTemplate.BaseUrl,
                        AuthMode = ProviderAuthMode.OAuth,
                        Protocol = ProviderProtocol.OpenAiResponses,
                        DefaultModel = ProviderTemplateCatalog.CodexOAuthTemplate.DefaultModel
                    },
                    new ProviderConfig
                    {
                        Id = "codex-oauth-disabled",
                        BuiltinId = ProviderTemplateCatalog.CodexOAuthBuiltinId,
                        DisplayName = "Codex OAuth Disabled",
                        BaseUrl = ProviderTemplateCatalog.CodexOAuthTemplate.BaseUrl,
                        AuthMode = ProviderAuthMode.OAuth,
                        Protocol = ProviderProtocol.OpenAiResponses,
                        DefaultModel = ProviderTemplateCatalog.CodexOAuthTemplate.DefaultModel,
                        SupportsWebSockets = false
                    }
                }
            };

            WriteJson(paths.ConfigPath, legacy);

            var upgraded = store.LoadConfig();

            Assert.True(upgraded.Providers.Single(provider => provider.Id == "codex-oauth").SupportsWebSockets == true);
            Assert.False(upgraded.Providers.Single(provider => provider.Id == "codex-oauth-disabled").SupportsWebSockets == true);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LoadConfig_PreservesLegacyDeepSeekEndpointAndProtocol()
    {
        var root = CreateTempDirectory();
        try
        {
            var paths = new AppPaths(root, Path.Combine(root, ".codex"));
            var store = new ConfigurationStore(paths);

            var legacy = new AppConfig
            {
                ActiveProviderId = "deepseek",
                Providers =
                {
                    new ProviderConfig
                    {
                        Id = "deepseek",
                        BuiltinId = ProviderTemplateCatalog.DeepSeekBuiltinId,
                        DisplayName = "DeepSeek",
                        BaseUrl = "https://api.deepseek.com/anthropic",
                        Protocol = ProviderProtocol.AnthropicMessages,
                        DefaultModel = "deepseek-chat",
                        Models =
                        {
                            new ModelRouteConfig { Id = "deepseek-chat", Protocol = ProviderProtocol.AnthropicMessages }
                        }
                    }
                }
            };

            WriteJson(paths.ConfigPath, legacy);

            var upgraded = store.LoadConfig();
            var deepSeek = Assert.Single(upgraded.Providers, provider => provider.Id == "deepseek");

            Assert.Equal(ProviderTemplateCatalog.DeepSeekBuiltinId, deepSeek.BuiltinId);
            Assert.Equal("https://api.deepseek.com/anthropic", deepSeek.BaseUrl);
            Assert.Equal(ProviderProtocol.AnthropicMessages, deepSeek.Protocol);
            Assert.Equal("deepseek-chat", deepSeek.DefaultModel);
            Assert.All(deepSeek.Models, model => Assert.Equal(ProviderProtocol.AnthropicMessages, model.Protocol));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RoutinAiTemplate_IncludesRequestedDeepSeekAndMimoRoutes()
    {
        var provider = ProviderTemplateCatalog.CreateProvider(ProviderTemplateCatalog.RoutinAiBuiltinId, []);

        var deepSeekFlash = Assert.Single(provider.Models, model => model.Id == "deepseek-v4-flash");
        var deepSeekPro = Assert.Single(provider.Models, model => model.Id == "deepseek-v4-pro");
        var mimoFlash = Assert.Single(provider.Models, model => model.Id == "mimo-v2-flash");
        var mimoV2Pro = Assert.Single(provider.Models, model => model.Id == "mimo-v2-pro");
        var mimoV25Pro = Assert.Single(provider.Models, model => model.Id == "mimo-v2.5-pro");

        Assert.Equal(ProviderProtocol.OpenAiResponses, deepSeekFlash.Protocol);
        Assert.Equal(ProviderProtocol.OpenAiResponses, deepSeekPro.Protocol);
        Assert.Equal(ProviderProtocol.OpenAiResponses, mimoFlash.Protocol);
        Assert.Equal(ProviderProtocol.OpenAiResponses, mimoV2Pro.Protocol);
        Assert.Equal(ProviderProtocol.OpenAiResponses, mimoV25Pro.Protocol);
        Assert.Equal("priority", deepSeekFlash.ServiceTier);
        Assert.Equal("priority", deepSeekPro.ServiceTier);
        Assert.Equal("priority", mimoFlash.ServiceTier);
        Assert.Equal("priority", mimoV2Pro.ServiceTier);
        Assert.Equal("priority", mimoV25Pro.ServiceTier);
        var deepSeekFlashCost = deepSeekFlash.Cost ?? throw new InvalidOperationException("DeepSeek V4 Flash cost settings should be seeded.");
        var deepSeekProCost = deepSeekPro.Cost ?? throw new InvalidOperationException("DeepSeek V4 Pro cost settings should be seeded.");
        var mimoFlashCost = mimoFlash.Cost ?? throw new InvalidOperationException("MiMo V2 Flash cost settings should be seeded.");
        var mimoV2ProCost = mimoV2Pro.Cost ?? throw new InvalidOperationException("MiMo V2 Pro cost settings should be seeded.");
        var mimoV25ProCost = mimoV25Pro.Cost ?? throw new InvalidOperationException("MiMo V2.5 Pro cost settings should be seeded.");
        Assert.True(deepSeekFlashCost.FastMode);
        Assert.True(deepSeekProCost.FastMode);
        Assert.True(mimoFlashCost.FastMode);
        Assert.True(mimoV2ProCost.FastMode);
        Assert.True(mimoV25ProCost.FastMode);
        Assert.DoesNotContain(provider.Models, model => model.Id == "deepseek-chat");
        Assert.DoesNotContain(provider.Models, model => model.Id == "deepseek-reasoner");
    }

    [Fact]
    public void EnsureValidDefaults_PreservesEditedRoutinAiDeepSeekRoutes()
    {
        var provider = ProviderTemplateCatalog.CreateProvider(ProviderTemplateCatalog.RoutinAiBuiltinId, []);
        foreach (var route in provider.Models.Where(route => route.Id.StartsWith("deepseek-", StringComparison.OrdinalIgnoreCase)))
            route.Protocol = ProviderProtocol.OpenAiResponses;

        var config = new AppConfig
        {
            ActiveProviderId = provider.Id,
            Providers = { provider }
        };

        ConfigurationStore.EnsureValidDefaults(config);

        Assert.Equal(
            ProviderProtocol.OpenAiResponses,
            provider.Models.Single(route => route.Id == "deepseek-v4-flash").Protocol);
        Assert.Equal(
            ProviderProtocol.OpenAiResponses,
            provider.Models.Single(route => route.Id == "deepseek-v4-pro").Protocol);
    }

    [Fact]
    public void SaveConfig_PreservesEditedBuiltInProviderModelRouteFields()
    {
        var root = CreateTempDirectory();
        try
        {
            var paths = new AppPaths(root, Path.Combine(root, ".codex"));
            var store = new ConfigurationStore(paths);
            var provider = ProviderTemplateCatalog.CreateProvider(ProviderTemplateCatalog.RoutinAiBuiltinId, []);
            var route = provider.Models.Single(model => model.Id == "gpt-5.5");
            route.DisplayName = "Custom GPT";
            route.Protocol = ProviderProtocol.AnthropicMessages;
            route.UpstreamModel = null;
            route.ServiceTier = null;
            route.Cost = new ProviderCostSettings { FastMode = false };

            var config = new AppConfig
            {
                ActiveProviderId = provider.Id,
                Providers = { provider }
            };

            store.SaveConfig(config);
            var reloaded = store.LoadConfig();
            var reloadedProvider = Assert.Single(reloaded.Providers, item => item.Id == provider.Id);
            var reloadedRoute = reloadedProvider.Models.Single(model => model.Id == "gpt-5.5");

            Assert.Equal("Custom GPT", reloadedRoute.DisplayName);
            Assert.Equal(ProviderProtocol.AnthropicMessages, reloadedRoute.Protocol);
            Assert.Null(reloadedRoute.UpstreamModel);
            Assert.Null(reloadedRoute.ServiceTier);
            var cost = Assert.IsType<ProviderCostSettings>(reloadedRoute.Cost);
            Assert.False(cost.FastMode);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ProviderRoutingResolver_UsesActiveProviderForRequestedModel()
    {
        var config = new AppConfig
        {
            ActiveProviderId = "openai-official",
            Providers =
            {
                ProviderTemplateCatalog.CreateProvider(ProviderTemplateCatalog.OpenAiOfficialBuiltinId, []),
                ProviderTemplateCatalog.CreateProvider(ProviderTemplateCatalog.AnthropicBuiltinId, ["openai-official"]),
                ProviderTemplateCatalog.CreateProvider(ProviderTemplateCatalog.DeepSeekBuiltinId, ["openai-official", "anthropic"]),
                ProviderTemplateCatalog.CreateProvider(ProviderTemplateCatalog.XiaomiBuiltinId, ["openai-official", "anthropic", "deepseek"])
            }
        };

        var selection = ProviderRoutingResolver.Resolve(config, "claude-sonnet-4-5");

        Assert.NotNull(selection);
        Assert.Equal("openai-official", selection!.Provider.Id);
        Assert.Equal(CodexSwitchDefaults.ManagedCodexModel, selection.Model?.Id);

        var deepSeekSelection = ProviderRoutingResolver.Resolve(config, "deepseek-reasoner");
        Assert.NotNull(deepSeekSelection);
        Assert.Equal("openai-official", deepSeekSelection!.Provider.Id);
        Assert.Equal(CodexSwitchDefaults.ManagedCodexModel, deepSeekSelection.Model?.Id);

        var xiaomiSelection = ProviderRoutingResolver.Resolve(config, "mimo-v2-pro");
        Assert.NotNull(xiaomiSelection);
        Assert.Equal("openai-official", xiaomiSelection!.Provider.Id);
        Assert.Equal(CodexSwitchDefaults.ManagedCodexModel, xiaomiSelection.Model?.Id);

        var listings = ProviderRoutingResolver.CollectModelListings(config);
        var gpt54 = Assert.Single(listings, item => item.Id == "gpt-5.4");
        var deepSeekFlash = Assert.Single(listings, item => item.Id == "deepseek-flash");
        var mimoPro = Assert.Single(listings, item => item.Id == "mimo-v2.5-pro");
        Assert.Contains("openai-official", gpt54.ProviderIds);
        Assert.Contains("deepseek", deepSeekFlash.ProviderIds);
        Assert.Contains("xiaomi-mimo", mimoPro.ProviderIds);
    }

    [Fact]
    public void LoadConfig_DoesNotRestoreDeletedBuiltInProvider()
    {
        var root = CreateTempDirectory();
        try
        {
            var paths = new AppPaths(root, Path.Combine(root, ".codex"));
            var store = new ConfigurationStore(paths);
            var legacy = new AppConfig
            {
                ActiveProviderId = ProviderTemplateCatalog.AiossPlusBuiltinId,
                ActiveCodexProviderId = ProviderTemplateCatalog.AiossPlusBuiltinId,
                DeletedBuiltinProviderIds = { ProviderTemplateCatalog.AiossProBuiltinId },
                Providers =
                {
                    ProviderTemplateCatalog.CreateProvider(ProviderTemplateCatalog.AiossPlusBuiltinId, [])
                }
            };

            WriteJson(paths.ConfigPath, legacy);

            var loaded = store.LoadConfig();

            Assert.DoesNotContain(loaded.Providers, provider =>
                string.Equals(provider.BuiltinId, ProviderTemplateCatalog.AiossProBuiltinId, StringComparison.OrdinalIgnoreCase));
            Assert.Contains(loaded.DeletedBuiltinProviderIds, id =>
                string.Equals(id, ProviderTemplateCatalog.AiossProBuiltinId, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ProviderRoutingResolver_UsesDefaultConversionForActiveProvider()
    {
        foreach (var templateId in new[]
                 {
                     ProviderTemplateCatalog.AnthropicBuiltinId,
                     ProviderTemplateCatalog.DeepSeekBuiltinId,
                     ProviderTemplateCatalog.XiaomiBuiltinId
                 })
        {
            var openAi = ProviderTemplateCatalog.CreateProvider(ProviderTemplateCatalog.OpenAiOfficialBuiltinId, []);
            var provider = ProviderTemplateCatalog.CreateProvider(templateId, [openAi.Id]);
            var expectedRoute = provider.Models.FirstOrDefault(model =>
                string.Equals(model.Id, provider.DefaultModel, StringComparison.OrdinalIgnoreCase));
            var expectedUpstream = string.IsNullOrWhiteSpace(expectedRoute?.UpstreamModel)
                ? provider.DefaultModel
                : expectedRoute.UpstreamModel;
            var config = new AppConfig
            {
                ActiveProviderId = provider.Id,
                Providers = { openAi, provider }
            };

            var selection = ProviderRoutingResolver.Resolve(config, CodexSwitchDefaults.ManagedCodexModel);

            Assert.NotNull(selection);
            Assert.Equal(provider.Id, selection!.Provider.Id);
            Assert.Equal(CodexSwitchDefaults.ManagedCodexModel, selection.Model?.Id);
            Assert.Equal(expectedUpstream, selection.Model?.UpstreamModel);
        }
    }

    [Fact]
    public void ProviderRoutingResolver_IgnoresDisabledDefaultConversion()
    {
        var provider = ProviderTemplateCatalog.CreateProvider(ProviderTemplateCatalog.AnthropicBuiltinId, []);
        var conversion = Assert.Single(provider.ModelConversions, ProviderTemplateCatalog.IsDefaultModelConversion);
        conversion.Enabled = false;

        Assert.False(ProviderRoutingResolver.ProviderSupports(provider, [CodexSwitchDefaults.ManagedCodexModel]));

        var listings = ProviderRoutingResolver.CollectModelListings(new AppConfig
        {
            ActiveProviderId = provider.Id,
            Providers = { provider }
        });

        Assert.DoesNotContain(listings, listing =>
            string.Equals(listing.Id, CodexSwitchDefaults.ManagedCodexModel, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ProviderRoutingResolver_ListsEnabledConversionSources()
    {
        var provider = ProviderTemplateCatalog.CreateProvider(ProviderTemplateCatalog.AnthropicBuiltinId, []);
        var listings = ProviderRoutingResolver.CollectModelListings(new AppConfig
        {
            ActiveProviderId = provider.Id,
            Providers = { provider }
        });

        var listing = Assert.Single(listings, item => item.Id == CodexSwitchDefaults.ManagedCodexModel);
        Assert.Contains(provider.Id, listing.ProviderIds);
    }

    [Fact]
    public void EnsureValidDefaults_SeedsAndPreservesDefaultModelConversions()
    {
        var disabledProvider = new ProviderConfig
        {
            Id = "disabled",
            DisplayName = "Disabled",
            BaseUrl = "https://example.com/v1",
            Protocol = ProviderProtocol.AnthropicMessages,
            DefaultModel = "claude-custom",
            ModelConversions =
            {
                new ModelConversionConfig
                {
                    SourceModel = "gpt-5.5",
                    UseDefaultModel = true,
                    Enabled = false
                }
            }
        };
        var config = new AppConfig
        {
            ActiveProviderId = disabledProvider.Id,
            Providers = { disabledProvider }
        };

        ConfigurationStore.EnsureValidDefaults(config);

        var provider = Assert.Single(config.Providers, item => item.Id == "disabled");
        var conversion = Assert.Single(provider.ModelConversions, ProviderTemplateCatalog.IsDefaultModelConversion);
        Assert.Equal(CodexSwitchDefaults.ManagedCodexModel, conversion.SourceModel);
        Assert.False(conversion.Enabled);

        conversion.Enabled = true;
        provider.DefaultModel = "claude-new-default";
        var selection = ProviderRoutingResolver.Resolve(new AppConfig
        {
            ActiveProviderId = provider.Id,
            Providers = { provider }
        }, CodexSwitchDefaults.ManagedCodexModel);

        Assert.Equal("claude-new-default", selection?.Model?.UpstreamModel);
    }

    [Fact]
    public void IconCacheService_UsesOfficialXiaomiFallbackIconUrl()
    {
        var root = CreateTempDirectory();
        try
        {
            var paths = new AppPaths(root, Path.Combine(root, ".codex"));
            using var httpClient = new HttpClient();
            var icons = new IconCacheService(paths, httpClient);

            Assert.Equal(
                "https://platform.xiaomimimo.com/static/favicon.874c9507.png",
                icons.GetIconUrl("xiaomi"));
            Assert.Equal("xiaomi", IconCacheService.ResolveModelIconSlug("mimo-v2.5-pro"));
            Assert.Equal("grok", IconCacheService.ResolveModelIconSlug("grok-4.6"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ProviderRoutingResolver_UsesProviderBillingMultiplierInsteadOfRouteMultiplier()
    {
        var config = new AppConfig
        {
            GlobalCost = new ProviderCostSettings { Multiplier = 1m }
        };
        var provider = new ProviderConfig
        {
            Cost = new ProviderCostSettings { Multiplier = 0.13m }
        };
        var route = new ModelRouteConfig
        {
            Cost = new ProviderCostSettings
            {
                Multiplier = 9m,
                FastMode = true,
                MatchMode = CostMatchMode.ResponseModel
            }
        };

        var resolved = ProviderRoutingResolver.ResolveCostSettings(config, provider, route);

        Assert.Equal(0.13m, resolved.Multiplier);
        Assert.True(resolved.FastMode);
        Assert.Equal(CostMatchMode.ResponseModel, resolved.MatchMode);
    }

    private static void AssertAiossProvider(ProviderConfig provider, string expectedId)
    {
        Assert.Equal(expectedId, provider.Id);
        Assert.Equal(expectedId, provider.BuiltinId);
        Assert.Equal(expectedId, provider.DisplayName);
        Assert.Equal(ProviderTemplateCatalog.AiossBaseUrl, provider.BaseUrl);
        Assert.Equal("https://aioss.cc", provider.Website);
        Assert.Equal(ProviderProtocol.OpenAiResponses, provider.Protocol);
        Assert.Equal(CodexSwitchDefaults.ManagedCodexModel, provider.DefaultModel);
        Assert.True(provider.SupportsCodex);
        Assert.False(provider.SupportsClaudeCode);
        Assert.True(provider.SupportsWebSockets == true);
        Assert.False(provider.Codex.EnableOneMillionContext);
        Assert.False(provider.OverrideRequestModel);
        Assert.Null(provider.ServiceTier);
        Assert.False(provider.Cost?.FastMode ?? true);
        Assert.Equal(0.13m, provider.Cost?.Multiplier);

        Assert.Contains(provider.Models, model => model.Id == "gpt-6-astra");
        Assert.Contains(provider.Models, model => model.Id == "gpt-6-sol");
        Assert.Contains(provider.Models, model => model.Id == "gpt-6-luna");
        Assert.Contains(provider.Models, model => model.Id == "gpt-5.6-sol");
        Assert.DoesNotContain(provider.Models, model => model.Id == "gpt-5.6");
        Assert.DoesNotContain(provider.Models, model => model.Id == "gpt-5.4-mini");
        Assert.DoesNotContain(provider.Models, model => model.Id == "gpt-5.3-codex");
        var model = Assert.Single(provider.Models, model => model.Id == CodexSwitchDefaults.ManagedCodexModel);
        Assert.Equal(ProviderProtocol.OpenAiResponses, model.Protocol);
        Assert.Null(model.UpstreamModel);
        Assert.Null(model.ServiceTier);
        Assert.False(model.Cost?.FastMode ?? true);

        var query = Assert.IsType<ProviderUsageQueryConfig>(provider.UsageQuery);
        Assert.True(query.Enabled);
        Assert.Equal(UsageQueryTemplateCatalog.AiossTemplateId, query.TemplateId);
        Assert.Equal("GET", query.Method);
        Assert.Equal(20, query.TimeoutSeconds);
        Assert.Equal("{{origin}}/v1/usage", query.Url);
        Assert.Equal("Bearer {{apiKey}}", query.Headers["Authorization"]);
        Assert.Null(query.JsonBody);
        Assert.Equal("remaining", query.Extractor.RemainingPath);
        Assert.Equal("USD", query.Extractor.Unit);
        Assert.Null(query.Extractor.UnitPath);
        AssertDefaultConversion(provider);
    }

    private static void AssertGptPricing(
        IEnumerable<ModelPricingRule> rules,
        string modelId,
        decimal input,
        decimal longInput,
        decimal cachedInput,
        decimal longCachedInput,
        decimal? cacheCreationInput,
        decimal? longCacheCreationInput,
        decimal output,
        decimal longOutput)
    {
        var rule = Assert.Single(rules, item => item.Id == modelId);
        Assert.Equal("openai", rule.IconSlug);
        Assert.Contains(modelId + "*", rule.Aliases);
        Assert.Equal(BuiltInModelCatalog.OpenAiLongContextThresholdTokens, rule.ContextPricingThresholdTokens);
        AssertTieredPrices(rule.Input, input, longInput);
        AssertTieredPrices(rule.CachedInput, cachedInput, longCachedInput);
        if (cacheCreationInput.HasValue && longCacheCreationInput.HasValue)
            AssertTieredPrices(rule.CacheCreationInput, cacheCreationInput.Value, longCacheCreationInput.Value);
        else
            Assert.Empty(rule.CacheCreationInput.Tiers);
        AssertTieredPrices(rule.Output, output, longOutput);
    }

    private static void AssertClaudePricing(
        IEnumerable<ModelPricingRule> rules,
        string modelId,
        decimal input,
        decimal cachedInput,
        decimal cacheCreation5Minute,
        decimal cacheCreation1Hour,
        decimal output)
    {
        var rule = Assert.Single(rules, item => item.Id == modelId);
        Assert.Equal("claude", rule.IconSlug);
        Assert.Null(rule.ContextPricingThresholdTokens);
        AssertFlatPrice(rule.Input, input);
        AssertFlatPrice(rule.CachedInput, cachedInput);
        AssertFlatPrice(rule.CacheCreationInput, cacheCreation5Minute);
        AssertFlatPrice(rule.CacheCreationInput1Hour, cacheCreation1Hour);
        AssertFlatPrice(rule.Output, output);
    }

    private static void AssertFlatPrice(TokenPriceTable table, decimal price)
    {
        var tier = Assert.Single(table.Tiers);
        Assert.Null(tier.UpToTokens);
        Assert.Equal(price, tier.PricePerUnit);
    }

    private static void AssertTieredPrices(TokenPriceTable table, decimal shortContextPrice, decimal longContextPrice)
    {
        Assert.Collection(
            table.Tiers,
            tier =>
            {
                Assert.Equal(BuiltInModelCatalog.OpenAiLongContextThresholdTokens, tier.UpToTokens);
                Assert.Equal(shortContextPrice, tier.PricePerUnit);
            },
            tier =>
            {
                Assert.Null(tier.UpToTokens);
                Assert.Equal(longContextPrice, tier.PricePerUnit);
            });
    }

    private static TokenPriceTable FlatTable(decimal price)
    {
        var table = new TokenPriceTable();
        table.Tiers.Add(new PricingTier { UpToTokens = null, PricePerUnit = price });
        return table;
    }

    private static void WriteJson<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions));
    }

    private static void AssertDefaultConversion(ProviderConfig provider)
    {
        var conversion = Assert.Single(provider.ModelConversions, ProviderTemplateCatalog.IsDefaultModelConversion);
        Assert.True(conversion.Enabled);
        Assert.True(conversion.UseDefaultModel);
        Assert.Null(conversion.TargetModel);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "CodexSwitchTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
