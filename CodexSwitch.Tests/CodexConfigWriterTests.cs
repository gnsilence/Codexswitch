using System.Text;
using System.Text.Json;
using CodexSwitch.Models;
using CodexSwitch.Services;

namespace CodexSwitch.Tests;

public sealed class CodexConfigWriterTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        "CodexSwitchTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Apply_SkipsDuplicateBackups_WhenCalledTwiceInQuickSuccession()
    {
        var appRoot = Path.Combine(_tempDirectory, "appdata");
        var codexRoot = Path.Combine(_tempDirectory, "codex");
        var paths = new AppPaths(appRoot, codexRoot);
        Directory.CreateDirectory(paths.CodexDirectory);

        File.WriteAllText(paths.CodexConfigPath, "model = \"before\"\n");
        File.WriteAllText(paths.CodexAuthPath, "{\"openai_api_key\":\"before\"}\n");

        var writer = new CodexConfigWriter(paths);
        var config = new AppConfig
        {
            Proxy = new ProxySettings
            {
                Host = "127.0.0.1",
                Port = 12785,
                InboundApiKey = "test-key"
            }
        };
        writer.Apply(config);
        writer.Apply(config);

        var backups = Directory.GetFiles(paths.CodexDirectory, "*.bak");
        Assert.Equal(2, backups.Length);
        Assert.Equal(1, backups.Count(path => string.Equals(Path.GetFileName(path), "config.toml.bak", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(1, backups.Count(path => string.Equals(Path.GetFileName(path), "auth.json.bak", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal("model = \"before\"\n", File.ReadAllText(BackupPath(paths.CodexConfigPath)));
        Assert.Equal("{\"openai_api_key\":\"before\"}\n", File.ReadAllText(BackupPath(paths.CodexAuthPath)));
    }

    [Fact]
    public void Apply_ReplacesCodexConfig_WithManagedMeteorProfile()
    {
        var appRoot = Path.Combine(_tempDirectory, "managed-appdata");
        var codexRoot = Path.Combine(_tempDirectory, "managed-codex");
        var paths = new AppPaths(appRoot, codexRoot);
        Directory.CreateDirectory(paths.CodexDirectory);
        File.WriteAllText(paths.CodexConfigPath, "model = \"user-model\"\n");

        var writer = new CodexConfigWriter(paths);
        writer.Apply(new AppConfig
        {
            Proxy = new ProxySettings
            {
                Host = "127.0.0.1",
                Port = 12785,
                InboundApiKey = "local-secret"
            }
        });

        var configToml = File.ReadAllText(paths.CodexConfigPath);
        Assert.DoesNotContain("user-model", configToml);
        Assert.DoesNotContain("codexswitch-managed", configToml);
        Assert.Contains($"model = \"{CodexSwitchDefaults.ManagedCodexModel}\"", configToml, StringComparison.Ordinal);
        Assert.Contains("model_provider = \"meteor-ai\"", configToml, StringComparison.Ordinal);
        Assert.Contains("base_url = \"http://127.0.0.1:12785/v1\"", configToml, StringComparison.Ordinal);
        Assert.Contains("responses_websockets_v2 = true", configToml, StringComparison.Ordinal);
        Assert.Contains("prevent_idle_sleep = true", configToml, StringComparison.Ordinal);
        Assert.True(File.Exists(BackupPath(paths.CodexConfigPath)));
        Assert.Equal("model = \"user-model\"\n", File.ReadAllText(BackupPath(paths.CodexConfigPath)));

        var authJson = File.ReadAllText(paths.CodexAuthPath);
        Assert.Contains("\"auth_mode\": \"apikey\"", authJson, StringComparison.Ordinal);
        Assert.Contains("\"OPENAI_API_KEY\": \"local-secret\"", authJson, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_WritesCodexOneMillionContext_WhenActiveProviderEnablesIt()
    {
        var appRoot = Path.Combine(_tempDirectory, "codex-1m-appdata");
        var codexRoot = Path.Combine(_tempDirectory, "codex-1m-codex");
        var paths = new AppPaths(appRoot, codexRoot);
        var writer = new CodexConfigWriter(paths);

        writer.Apply(new AppConfig
        {
            ActiveProviderId = "openai",
            ActiveCodexProviderId = "openai",
            Providers =
            {
                new ProviderConfig
                {
                    Id = "openai",
                    Protocol = ProviderProtocol.OpenAiResponses,
                    SupportsCodex = true,
                    DefaultModel = "gpt-5.5",
                    Codex = { EnableOneMillionContext = true },
                    Models =
                    {
                        new ModelRouteConfig { Id = "gpt-5.5", Protocol = ProviderProtocol.OpenAiResponses }
                    }
                }
            }
        });

        var configToml = File.ReadAllText(paths.CodexConfigPath);
        Assert.Contains("model_context_window = 1000000", configToml, StringComparison.Ordinal);
        Assert.Contains("model_auto_compact_token_limit = 900000", configToml, StringComparison.Ordinal);

        using var catalog = JsonDocument.Parse(File.ReadAllText(paths.CodexModelCatalogPath));
        var model = catalog.RootElement.GetProperty("models")[0];
        Assert.Equal(1_000_000, model.GetProperty("context_window").GetInt32());
        Assert.Equal(1_000_000, model.GetProperty("max_context_window").GetInt32());
    }

    [Fact]
    public void Apply_WritesManagedModelCatalogForActiveProvider()
    {
        var appRoot = Path.Combine(_tempDirectory, "catalog-appdata");
        var codexRoot = Path.Combine(_tempDirectory, "catalog-codex");
        var paths = new AppPaths(appRoot, codexRoot);
        var writer = new CodexConfigWriter(paths);

        writer.Apply(new AppConfig
        {
            ActiveCodexProviderId = "deepseek",
            ActiveProviderId = "deepseek",
            Proxy = { ManageCodexConfig = true },
            Providers =
            {
                new ProviderConfig
                {
                    Id = "deepseek",
                    SupportsCodex = true,
                    DefaultModel = "deepseek-v4-flash",
                    Models =
                    {
                        new ModelRouteConfig
                        {
                            Id = "deepseek-v4-flash",
                            DisplayName = "DeepSeek V4 Flash",
                            Protocol = ProviderProtocol.OpenAiChat
                        },
                        new ModelRouteConfig
                        {
                            Id = "deepseek-v4-pro",
                            DisplayName = "DeepSeek V4 Pro",
                            Protocol = ProviderProtocol.OpenAiChat
                        }
                    }
                }
            }
        });

        var configToml = File.ReadAllText(paths.CodexConfigPath);
        Assert.Contains("model = \"deepseek-v4-flash\"", configToml, StringComparison.Ordinal);
        Assert.Contains(
            $"model_catalog_json = \"{CodexConfigWriter.ManagedModelCatalogFileName}\"",
            configToml,
            StringComparison.Ordinal);

        using var catalog = JsonDocument.Parse(File.ReadAllText(paths.CodexModelCatalogPath));
        var models = catalog.RootElement.GetProperty("models");
        Assert.Equal(2, models.GetArrayLength());
        Assert.Equal("deepseek-v4-flash", models[0].GetProperty("slug").GetString());
        Assert.Equal("DeepSeek V4 Pro", models[1].GetProperty("display_name").GetString());
        Assert.True(models[0].GetProperty("model_messages").ValueKind == JsonValueKind.Object);
        Assert.True(models[0].GetProperty("supports_reasoning_summaries").GetBoolean());
        Assert.Equal(272_000, models[0].GetProperty("context_window").GetInt32());
        Assert.Equal(272_000, models[0].GetProperty("max_context_window").GetInt32());
        Assert.Equal(95, models[0].GetProperty("effective_context_window_percent").GetInt32());
    }

    [Fact]
    public void Apply_WritesBuiltInProviderModelCatalogsForCodexSync()
    {
        AssertBuiltInCatalogIncludes(
            ProviderTemplateCatalog.DeepSeekBuiltinId,
            "deepseek-flash",
            "deepseek-v4-pro");
        AssertBuiltInCatalogIncludes(
            ProviderTemplateCatalog.AnthropicBuiltinId,
            "claude-sonnet-5",
            "claude-sonnet-4-5");
        AssertBuiltInCatalogIncludes(
            ProviderTemplateCatalog.GrokBuiltinId,
            "grok-4.6");
    }

    [Fact]
    public void Apply_MergesBuiltInGptRoutesAndWritesAllReasoningLevels()
    {
        var appRoot = Path.Combine(_tempDirectory, "gpt-catalog-appdata");
        var codexRoot = Path.Combine(_tempDirectory, "gpt-catalog-codex");
        var paths = new AppPaths(appRoot, codexRoot);
        var writer = new CodexConfigWriter(paths);

        writer.Apply(new AppConfig
        {
            ActiveCodexProviderId = "custom-gpt",
            ActiveProviderId = "custom-gpt",
            Proxy = { ManageCodexConfig = true },
            Providers =
            {
                new ProviderConfig
                {
                    Id = "custom-gpt",
                    Protocol = ProviderProtocol.OpenAiResponses,
                    SupportsCodex = true,
                    DefaultModel = "gpt-5.5",
                    Models =
                    {
                        new ModelRouteConfig
                        {
                            Id = "gpt-5.5",
                            DisplayName = "My GPT 5.5",
                            Protocol = ProviderProtocol.OpenAiResponses
                        },
                        new ModelRouteConfig
                        {
                            Id = "custom-model",
                            Protocol = ProviderProtocol.OpenAiResponses
                        }
                    }
                }
            }
        });

        using var catalog = JsonDocument.Parse(File.ReadAllText(paths.CodexModelCatalogPath));
        var models = catalog.RootElement.GetProperty("models");
        var slugs = models.EnumerateArray()
            .Select(model => model.GetProperty("slug").GetString())
            .ToArray();
        Assert.Contains("gpt-6-astra", slugs);
        Assert.Contains("gpt-5.6-sol", slugs);
        Assert.Contains("gpt-5.6-terra", slugs);
        Assert.Contains("gpt-5.6-luna", slugs);
        Assert.DoesNotContain("gpt-5", slugs);
        Assert.DoesNotContain("gpt-5.4-mini", slugs);
        Assert.DoesNotContain("gpt-5.3-codex", slugs);
        Assert.Contains("custom-model", slugs);
        Assert.Equal("My GPT 5.5", models.EnumerateArray()
            .Single(model => model.GetProperty("slug").GetString() == "gpt-5.5")
            .GetProperty("display_name").GetString());

        var reasoningLevels = models[0].GetProperty("supported_reasoning_levels")
            .EnumerateArray()
            .Select(level => level.GetProperty("effort").GetString() ?? "")
            .ToArray();
        Assert.Equal(["low", "medium", "high", "xhigh", "max", "ultra"], reasoningLevels);
        Assert.Equal("medium", models[0].GetProperty("default_reasoning_level").GetString());

        Assert.Equal(
            ["low", "medium", "high", "xhigh", "max", "ultra"],
            models.EnumerateArray()
                .Single(model => model.GetProperty("slug").GetString() == "gpt-5.6-terra")
                .GetProperty("supported_reasoning_levels")
                .EnumerateArray()
                .Select(level => level.GetProperty("effort").GetString() ?? "")
                .ToArray());
        Assert.Equal(
            ["low", "medium", "high", "xhigh", "max"],
            models.EnumerateArray()
                .Single(model => model.GetProperty("slug").GetString() == "gpt-5.6-luna")
                .GetProperty("supported_reasoning_levels")
                .EnumerateArray()
                .Select(level => level.GetProperty("effort").GetString() ?? "")
                .ToArray());
        Assert.Equal(
            ["low", "medium", "high", "xhigh"],
            models.EnumerateArray()
                .Single(model => model.GetProperty("slug").GetString() == "gpt-5.5")
                .GetProperty("supported_reasoning_levels")
                .EnumerateArray()
                .Select(level => level.GetProperty("effort").GetString() ?? "")
                .ToArray());
    }

    [Fact]
    public void Apply_DefaultsRootReasoningEffortToMediumAndPreservesExistingSelection()
    {
        var appRoot = Path.Combine(_tempDirectory, "reasoning-appdata");
        var codexRoot = Path.Combine(_tempDirectory, "reasoning-codex");
        var paths = new AppPaths(appRoot, codexRoot);
        Directory.CreateDirectory(paths.CodexDirectory);
        File.WriteAllText(paths.CodexConfigPath, "model_reasoning_effort = \"high\"\n");

        var writer = new CodexConfigWriter(paths);
        writer.Apply(new AppConfig
        {
            ActiveCodexProviderId = "gpt",
            Providers =
            {
                new ProviderConfig
                {
                    Id = "gpt",
                    Protocol = ProviderProtocol.OpenAiResponses,
                    SupportsCodex = true,
                    DefaultModel = "gpt-5.5"
                }
            }
        });

        Assert.Contains("model_reasoning_effort = \"high\"", File.ReadAllText(paths.CodexConfigPath), StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_NormalizesUnsupportedRootReasoningEffort()
    {
        var appRoot = Path.Combine(_tempDirectory, "reasoning-invalid-appdata");
        var codexRoot = Path.Combine(_tempDirectory, "reasoning-invalid-codex");
        var paths = new AppPaths(appRoot, codexRoot);
        Directory.CreateDirectory(paths.CodexDirectory);
        File.WriteAllText(paths.CodexConfigPath, "model_reasoning_effort = \"invalid\"\n");

        new CodexConfigWriter(paths).Apply(new AppConfig
        {
            ActiveCodexProviderId = "gpt",
            Providers =
            {
                new ProviderConfig
                {
                    Id = "gpt",
                    Protocol = ProviderProtocol.OpenAiResponses,
                    SupportsCodex = true,
                    DefaultModel = "gpt-5.5"
                }
            }
        });

        Assert.Contains("model_reasoning_effort = \"medium\"", File.ReadAllText(paths.CodexConfigPath), StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_PreservesMaxRootReasoningEffortForLatestModels()
    {
        var appRoot = Path.Combine(_tempDirectory, "reasoning-max-appdata");
        var codexRoot = Path.Combine(_tempDirectory, "reasoning-max-codex");
        var paths = new AppPaths(appRoot, codexRoot);
        Directory.CreateDirectory(paths.CodexDirectory);
        File.WriteAllText(paths.CodexConfigPath, "model_reasoning_effort = \"max\"\n");

        new CodexConfigWriter(paths).Apply(new AppConfig
        {
            ActiveCodexProviderId = "gpt",
            Providers =
            {
                new ProviderConfig
                {
                    Id = "gpt",
                    Protocol = ProviderProtocol.OpenAiResponses,
                    SupportsCodex = true,
                    DefaultModel = "gpt-5.6-terra"
                }
            }
        });

        Assert.Contains("model_reasoning_effort = \"max\"", File.ReadAllText(paths.CodexConfigPath), StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_PreservesUltraRootReasoningEffortForLatestModels()
    {
        var appRoot = Path.Combine(_tempDirectory, "reasoning-ultra-appdata");
        var codexRoot = Path.Combine(_tempDirectory, "reasoning-ultra-codex");
        var paths = new AppPaths(appRoot, codexRoot);
        Directory.CreateDirectory(paths.CodexDirectory);
        File.WriteAllText(paths.CodexConfigPath, "model_reasoning_effort = \"ultra\"\n");

        new CodexConfigWriter(paths).Apply(new AppConfig
        {
            ActiveCodexProviderId = "gpt",
            Providers =
            {
                new ProviderConfig
                {
                    Id = "gpt",
                    Protocol = ProviderProtocol.OpenAiResponses,
                    SupportsCodex = true,
                    DefaultModel = "gpt-6-astra"
                }
            }
        });

        Assert.Contains("model_reasoning_effort = \"ultra\"", File.ReadAllText(paths.CodexConfigPath), StringComparison.Ordinal);
    }

    [Fact]
    public void RestoreOriginal_RestoresManagedModelCatalog()
    {
        var appRoot = Path.Combine(_tempDirectory, "catalog-restore-appdata");
        var codexRoot = Path.Combine(_tempDirectory, "catalog-restore-codex");
        var paths = new AppPaths(appRoot, codexRoot);
        Directory.CreateDirectory(paths.CodexDirectory);
        File.WriteAllText(paths.CodexModelCatalogPath, "{\"models\":[{\"slug\":\"before\"}]}\n");

        var writer = new CodexConfigWriter(paths);
        writer.Apply(new AppConfig
        {
            ActiveCodexProviderId = "deepseek",
            Proxy = { ManageCodexConfig = true },
            Providers =
            {
                new ProviderConfig
                {
                    Id = "deepseek",
                    SupportsCodex = true,
                    DefaultModel = "deepseek-v4-flash"
                }
            }
        });

        Assert.Contains("deepseek-v4-flash", File.ReadAllText(paths.CodexModelCatalogPath), StringComparison.Ordinal);
        writer.RestoreOriginal();
        Assert.Contains("\"before\"", File.ReadAllText(paths.CodexModelCatalogPath), StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_WithoutActiveCodexProvider_RemovesManagedModelCatalog()
    {
        var appRoot = Path.Combine(_tempDirectory, "catalog-remove-appdata");
        var codexRoot = Path.Combine(_tempDirectory, "catalog-remove-codex");
        var paths = new AppPaths(appRoot, codexRoot);
        var writer = new CodexConfigWriter(paths);
        var config = new AppConfig
        {
            ActiveCodexProviderId = "deepseek",
            Proxy = { ManageCodexConfig = true },
            Providers =
            {
                new ProviderConfig
                {
                    Id = "deepseek",
                    SupportsCodex = true,
                    DefaultModel = "deepseek-v4-flash"
                }
            }
        };

        writer.Apply(config);
        Assert.True(File.Exists(paths.CodexModelCatalogPath));

        config.ActiveCodexProviderId = "missing";
        writer.Apply(config);

        Assert.False(File.Exists(paths.CodexModelCatalogPath));
        Assert.False(File.Exists(paths.CodexModelCatalogPath + ".absent"));
    }

    [Fact]
    public void Apply_WritesCodexOneMillionContext_ForCustomCodexProviderWhenEnabled()
    {
        var appRoot = Path.Combine(_tempDirectory, "codex-1m-custom-appdata");
        var codexRoot = Path.Combine(_tempDirectory, "codex-1m-custom-codex");
        var paths = new AppPaths(appRoot, codexRoot);
        var writer = new CodexConfigWriter(paths);

        writer.Apply(new AppConfig
        {
            ActiveProviderId = "custom",
            ActiveCodexProviderId = "custom",
            Providers =
            {
                new ProviderConfig
                {
                    Id = "custom",
                    Protocol = ProviderProtocol.OpenAiChat,
                    SupportsCodex = true,
                    DefaultModel = "custom-long-context-model",
                    Codex = { EnableOneMillionContext = true }
                }
            }
        });

        var configToml = File.ReadAllText(paths.CodexConfigPath);
        Assert.Contains("model_context_window = 1000000", configToml, StringComparison.Ordinal);
        Assert.Contains("model_auto_compact_token_limit = 900000", configToml, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_RemovesCodexOneMillionContext_WhenActiveProviderDoesNotSupportIt()
    {
        var appRoot = Path.Combine(_tempDirectory, "codex-1m-remove-appdata");
        var codexRoot = Path.Combine(_tempDirectory, "codex-1m-remove-codex");
        var paths = new AppPaths(appRoot, codexRoot);
        var writer = new CodexConfigWriter(paths);
        var config = new AppConfig
        {
            ActiveProviderId = "openai",
            ActiveCodexProviderId = "openai",
            Providers =
            {
                new ProviderConfig
                {
                    Id = "openai",
                    Protocol = ProviderProtocol.OpenAiResponses,
                    SupportsCodex = true,
                    DefaultModel = "gpt-5.5",
                    Codex = { EnableOneMillionContext = true },
                    Models =
                    {
                        new ModelRouteConfig { Id = "gpt-5.5", Protocol = ProviderProtocol.OpenAiResponses }
                    }
                },
                new ProviderConfig
                {
                    Id = "deepseek",
                    Protocol = ProviderProtocol.OpenAiChat,
                    SupportsCodex = false,
                    DefaultModel = "deepseek-v4-flash",
                    Codex = { EnableOneMillionContext = true },
                    Models =
                    {
                        new ModelRouteConfig { Id = "deepseek-v4-flash", Protocol = ProviderProtocol.OpenAiChat }
                    }
                }
            }
        };

        writer.Apply(config);
        config.ActiveProviderId = "deepseek";
        config.ActiveCodexProviderId = "deepseek";
        writer.Apply(config);

        var configToml = File.ReadAllText(paths.CodexConfigPath);
        Assert.DoesNotContain("model_context_window", configToml, StringComparison.Ordinal);
        Assert.DoesNotContain("model_auto_compact_token_limit", configToml, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_WritesCodexSupportsWebSockets_WhenActiveProviderEnablesIt()
    {
        var appRoot = Path.Combine(_tempDirectory, "codex-websocket-appdata");
        var codexRoot = Path.Combine(_tempDirectory, "codex-websocket-codex");
        var paths = new AppPaths(appRoot, codexRoot);
        var writer = new CodexConfigWriter(paths);
        var config = new AppConfig
        {
            ActiveProviderId = "codex",
            ActiveCodexProviderId = "codex",
            Providers =
            {
                new ProviderConfig
                {
                    Id = "codex",
                    Protocol = ProviderProtocol.OpenAiResponses,
                    SupportsCodex = true,
                    SupportsWebSockets = true
                }
            }
        };

        writer.Apply(config);

        Assert.Contains("supports_websockets = true", File.ReadAllText(paths.CodexConfigPath), StringComparison.Ordinal);

        config.Providers[0].SupportsWebSockets = false;
        writer.Apply(config);

        Assert.DoesNotContain("supports_websockets", File.ReadAllText(paths.CodexConfigPath), StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_PreservesCustomCodexTomlSections_WhenProviderChanges()
    {
        var appRoot = Path.Combine(_tempDirectory, "preserve-toml-appdata");
        var codexRoot = Path.Combine(_tempDirectory, "preserve-toml-codex");
        var paths = new AppPaths(appRoot, codexRoot);
        Directory.CreateDirectory(paths.CodexDirectory);
        File.WriteAllText(
            paths.CodexConfigPath,
            """
            custom_root = true

            [mcp_servers.files]
            command = "npx"
            args = ["-y", "@modelcontextprotocol/server-filesystem"]

            [features]
            user_experiment = true
            """);

        var writer = new CodexConfigWriter(paths);
        var config = new AppConfig
        {
            ActiveProviderId = "fast",
            ActiveCodexProviderId = "fast",
            Providers =
            {
                new ProviderConfig
                {
                    Id = "fast",
                    Protocol = ProviderProtocol.OpenAiChat,
                    SupportsCodex = true,
                    DefaultModel = "deepseek-v4-flash"
                },
                new ProviderConfig
                {
                    Id = "long-context",
                    Protocol = ProviderProtocol.OpenAiResponses,
                    SupportsCodex = true,
                    DefaultModel = "gpt-5.5",
                    Codex = { EnableOneMillionContext = true },
                    Models =
                    {
                        new ModelRouteConfig { Id = "gpt-5.5", Protocol = ProviderProtocol.OpenAiResponses }
                    }
                }
            }
        };

        writer.Apply(config);
        config.ActiveProviderId = "long-context";
        config.ActiveCodexProviderId = "long-context";
        writer.Apply(config);

        var configToml = File.ReadAllText(paths.CodexConfigPath);
        Assert.Contains("custom_root = true", configToml, StringComparison.Ordinal);
        Assert.Contains("[mcp_servers.files]", configToml, StringComparison.Ordinal);
        Assert.Contains("command = \"npx\"", configToml, StringComparison.Ordinal);
        Assert.Contains("args = [\"-y\", \"@modelcontextprotocol/server-filesystem\"]", configToml, StringComparison.Ordinal);
        Assert.Contains("user_experiment = true", configToml, StringComparison.Ordinal);
        Assert.Contains("responses_websockets_v2 = true", configToml, StringComparison.Ordinal);
        Assert.Contains("model_context_window = 1000000", configToml, StringComparison.Ordinal);
        Assert.Contains("model_auto_compact_token_limit = 900000", configToml, StringComparison.Ordinal);
    }

    [Fact]
    public void ClaudeCodeApply_MergesSettingsAndRestoresOriginal()
    {
        var appRoot = Path.Combine(_tempDirectory, "claude-appdata");
        var codexRoot = Path.Combine(_tempDirectory, "claude-codex");
        var claudeRoot = Path.Combine(_tempDirectory, "claude-home");
        var paths = new AppPaths(appRoot, codexRoot, claudeRoot);
        Directory.CreateDirectory(paths.ClaudeDirectory);
        var original = """
        {
          "theme": "dark",
          "env": {
            "EXISTING": "keep"
          }
        }
        """;
        File.WriteAllText(paths.ClaudeSettingsPath, original);

        var writer = new ClaudeCodeConfigWriter(paths);
        writer.Apply(new AppConfig
        {
            ActiveClaudeCodeProviderId = "anthropic",
            Proxy =
            {
                Host = "127.0.0.1",
                Port = 12785,
                InboundApiKey = "local-secret",
                ManageClaudeCodeConfig = true
            },
            Providers =
            {
                new ProviderConfig
                {
                    Id = "anthropic",
                    SupportsClaudeCode = true,
                    DefaultModel = "claude-sonnet-4-5",
                    ClaudeCode =
                    {
                        Model = "claude-sonnet-4-5",
                        AlwaysThinkingEnabled = true,
                        SkipDangerousModePermissionPrompt = true,
                        EnableOneMillionContext = true
                    },
                    Models =
                    {
                        new ModelRouteConfig { Id = "claude-sonnet-4-5", Protocol = ProviderProtocol.AnthropicMessages }
                    }
                }
            }
        });

        using (var document = JsonDocument.Parse(File.ReadAllText(paths.ClaudeSettingsPath)))
        {
            var root = document.RootElement;
            Assert.Equal("dark", root.GetProperty("theme").GetString());
            Assert.Equal("keep", root.GetProperty("env").GetProperty("EXISTING").GetString());
            Assert.Equal("http://127.0.0.1:12785", root.GetProperty("env").GetProperty("ANTHROPIC_BASE_URL").GetString());
            Assert.Equal("local-secret", root.GetProperty("env").GetProperty("ANTHROPIC_AUTH_TOKEN").GetString());
            Assert.Equal("claude-sonnet-4-5[1m]", root.GetProperty("model").GetString());
            Assert.True(root.GetProperty("alwaysThinkingEnabled").GetBoolean());
            Assert.True(root.GetProperty("skipDangerousModePermissionPrompt").GetBoolean());
        }

        Assert.True(File.Exists(BackupPath(paths.ClaudeSettingsPath)));
        Assert.Equal(original, File.ReadAllText(BackupPath(paths.ClaudeSettingsPath)));

        writer.RestoreOriginal();

        Assert.Equal(original, File.ReadAllText(paths.ClaudeSettingsPath));
        Assert.False(File.Exists(BackupPath(paths.ClaudeSettingsPath)));
    }

    [Fact]
    public void Apply_PreserveCodexAppAuth_RestoresCapturedAuth()
    {
        var appRoot = Path.Combine(_tempDirectory, "preserve-appdata");
        var codexRoot = Path.Combine(_tempDirectory, "preserve-codex");
        var paths = new AppPaths(appRoot, codexRoot);
        Directory.CreateDirectory(paths.CodexDirectory);
        var originalAuth = "{\"auth_mode\":\"chatgpt\",\"tokens\":{\"access_token\":\"chatgpt-token\"}}\n";
        File.WriteAllText(paths.CodexAuthPath, originalAuth);

        var writer = new CodexConfigWriter(paths);
        writer.Apply(new AppConfig
        {
            Proxy =
            {
                Host = "127.0.0.1",
                Port = 12785,
                InboundApiKey = "local-secret"
            }
        });

        Assert.Contains("\"auth_mode\": \"apikey\"", File.ReadAllText(paths.CodexAuthPath), StringComparison.Ordinal);

        writer.Apply(new AppConfig
        {
            Proxy =
            {
                Host = "127.0.0.1",
                Port = 12785,
                InboundApiKey = "local-secret",
                PreserveCodexAppAuth = true
            }
        });

        Assert.Equal(originalAuth, File.ReadAllText(paths.CodexAuthPath));
        Assert.True(File.Exists(BackupPath(paths.CodexAuthPath)));
        Assert.Equal(originalAuth, File.ReadAllText(BackupPath(paths.CodexAuthPath)));
        Assert.Contains("model_provider = \"meteor-ai\"", File.ReadAllText(paths.CodexConfigPath), StringComparison.Ordinal);

        writer.RestoreOriginal();

        Assert.Equal(originalAuth, File.ReadAllText(paths.CodexAuthPath));
        Assert.False(File.Exists(BackupPath(paths.CodexAuthPath)));
    }

    [Fact]
    public void Apply_PreserveCodexAppAuth_DeletesManagedAuthWhenNoOriginalAuth()
    {
        var appRoot = Path.Combine(_tempDirectory, "preserve-delete-appdata");
        var codexRoot = Path.Combine(_tempDirectory, "preserve-delete-codex");
        var paths = new AppPaths(appRoot, codexRoot);

        var writer = new CodexConfigWriter(paths);
        writer.Apply(new AppConfig
        {
            Proxy =
            {
                InboundApiKey = "local-secret"
            }
        });

        Assert.True(File.Exists(paths.CodexAuthPath));

        writer.Apply(new AppConfig
        {
            Proxy =
            {
                InboundApiKey = "local-secret",
                PreserveCodexAppAuth = true
            }
        });

        Assert.False(File.Exists(paths.CodexAuthPath));
        Assert.False(File.Exists(BackupPath(paths.CodexAuthPath)));
        Assert.Contains("model_provider = \"meteor-ai\"", File.ReadAllText(paths.CodexConfigPath), StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_UseFakeCodexAppAuth_WritesFakeAuthAndRestoresOriginal()
    {
        var appRoot = Path.Combine(_tempDirectory, "fake-auth-appdata");
        var codexRoot = Path.Combine(_tempDirectory, "fake-auth-codex");
        var paths = new AppPaths(appRoot, codexRoot);
        Directory.CreateDirectory(paths.CodexDirectory);
        var originalAuth = "{\"auth_mode\":\"chatgpt\",\"tokens\":{\"access_token\":\"real-token-placeholder\"}}\n";
        File.WriteAllText(paths.CodexAuthPath, originalAuth);

        var writer = new CodexConfigWriter(paths);
        writer.Apply(new AppConfig
        {
            Proxy =
            {
                InboundApiKey = "local-secret",
                UseFakeCodexAppAuth = true
            }
        });

        var fakeAuth = File.ReadAllText(paths.CodexAuthPath);
        Assert.Contains("\"auth_mode\": \"chatgpt\"", fakeAuth, StringComparison.Ordinal);
        Assert.Contains("Fake Codex App auth fixture", fakeAuth, StringComparison.Ordinal);
        Assert.Contains("fake-refresh-token-for-local-test-only", fakeAuth, StringComparison.Ordinal);
        Assert.DoesNotContain("real-token-placeholder", fakeAuth, StringComparison.Ordinal);
        Assert.True(File.Exists(BackupPath(paths.CodexAuthPath)));
        Assert.Equal(originalAuth, File.ReadAllText(BackupPath(paths.CodexAuthPath)));

        writer.RestoreOriginal();

        Assert.Equal(originalAuth, File.ReadAllText(paths.CodexAuthPath));
        Assert.False(File.Exists(BackupPath(paths.CodexAuthPath)));
    }

    [Fact]
    public void RestoreOriginal_RestoresOriginalCodexFiles()
    {
        var appRoot = Path.Combine(_tempDirectory, "restore-appdata");
        var codexRoot = Path.Combine(_tempDirectory, "restore-codex");
        var paths = new AppPaths(appRoot, codexRoot);
        Directory.CreateDirectory(paths.CodexDirectory);
        File.WriteAllText(paths.CodexConfigPath, "model = \"before\"\n");
        File.WriteAllText(paths.CodexAuthPath, "{\"OPENAI_API_KEY\":\"before\"}\n");

        var writer = new CodexConfigWriter(paths);
        writer.Apply(new AppConfig());

        writer.RestoreOriginal();

        Assert.Equal("model = \"before\"\n", File.ReadAllText(paths.CodexConfigPath));
        Assert.Equal("{\"OPENAI_API_KEY\":\"before\"}\n", File.ReadAllText(paths.CodexAuthPath));
        Assert.False(File.Exists(BackupPath(paths.CodexConfigPath)));
        Assert.False(File.Exists(BackupPath(paths.CodexAuthPath)));
    }

    [Fact]
    public void RestoreOriginal_WritesAuthJsonWithoutUtf8Bom()
    {
        var appRoot = Path.Combine(_tempDirectory, "restore-no-bom-appdata");
        var codexRoot = Path.Combine(_tempDirectory, "restore-no-bom-codex");
        var paths = new AppPaths(appRoot, codexRoot);
        Directory.CreateDirectory(paths.CodexDirectory);
        const string originalAuth = "{\"auth_mode\":\"chatgpt\",\"tokens\":{\"access_token\":\"before\"}}\n";
        File.WriteAllText(paths.CodexAuthPath, originalAuth, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var writer = new CodexConfigWriter(paths);
        writer.Apply(new AppConfig());

        writer.RestoreOriginal();

        Assert.Equal(Encoding.UTF8.GetBytes(originalAuth), File.ReadAllBytes(paths.CodexAuthPath));
        Assert.False(File.Exists(BackupPath(paths.CodexAuthPath)));
    }

    [Fact]
    public void RestoreOriginal_DeletesManagedFiles_WhenTheyDidNotExistBefore()
    {
        var appRoot = Path.Combine(_tempDirectory, "delete-appdata");
        var codexRoot = Path.Combine(_tempDirectory, "delete-codex");
        var paths = new AppPaths(appRoot, codexRoot);

        var writer = new CodexConfigWriter(paths);
        writer.Apply(new AppConfig());

        // Requirement: config CodexSwitch generated (no pre-existing original) is never backed up.
        Assert.True(File.Exists(paths.CodexConfigPath));
        Assert.False(File.Exists(BackupPath(paths.CodexConfigPath)));
        Assert.False(File.Exists(BackupPath(paths.CodexAuthPath)));

        writer.RestoreOriginal();

        Assert.False(File.Exists(paths.CodexConfigPath));
        Assert.False(File.Exists(paths.CodexAuthPath));
        Assert.False(File.Exists(BackupPath(paths.CodexConfigPath)));
        Assert.False(File.Exists(BackupPath(paths.CodexAuthPath)));
    }

    private static string BackupPath(string path)
    {
        return path + ".bak";
    }

    private void AssertBuiltInCatalogIncludes(string templateId, params string[] expectedModelIds)
    {
        var appRoot = Path.Combine(_tempDirectory, templateId + "-appdata");
        var codexRoot = Path.Combine(_tempDirectory, templateId + "-codex");
        var paths = new AppPaths(appRoot, codexRoot);
        var provider = ProviderTemplateCatalog.CreateProvider(templateId, []);

        new CodexConfigWriter(paths).Apply(new AppConfig
        {
            ActiveProviderId = provider.Id,
            ActiveCodexProviderId = provider.Id,
            Providers = { provider }
        });

        using var catalog = JsonDocument.Parse(File.ReadAllText(paths.CodexModelCatalogPath));
        var models = catalog.RootElement.GetProperty("models").EnumerateArray().ToArray();
        var slugs = models
            .Select(model => model.GetProperty("slug").GetString())
            .ToArray();
        foreach (var expectedModelId in expectedModelIds)
        {
            Assert.Contains(expectedModelId, slugs);
            var model = models.Single(item => item.GetProperty("slug").GetString() == expectedModelId);
            var reasoningLevels = model.GetProperty("supported_reasoning_levels")
                .EnumerateArray()
                .Select(level => level.GetProperty("effort").GetString() ?? "")
                .ToArray();
            Assert.Equal(["none", "low", "medium", "high", "xhigh", "max"], reasoningLevels);
            Assert.Equal("medium", model.GetProperty("default_reasoning_level").GetString());
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
            Directory.Delete(_tempDirectory, recursive: true);
    }
}
