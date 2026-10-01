using System.IO.Compression;
using System.Text;
using CodexSwitch.Models;
using CodexSwitch.Proxy;
using CodexSwitch.Services;

namespace CodexSwitch.Tests;

public sealed class SecurityBaselineTests
{
    [Fact]
    public void ConfigurationStore_MovesCredentialsOutOfConfigAndBackups()
    {
        var root = CreateTempDirectory();
        try
        {
            var paths = new AppPaths(root, Path.Combine(root, ".codex"));
            var config = new AppConfig
            {
                Proxy =
                {
                    InboundApiKey = "inbound-secret"
                },
                Providers =
                {
                    new ProviderConfig
                    {
                        Id = "provider-a",
                        ApiKey = "provider-secret",
                        OAuthAccounts =
                        {
                            new OAuthAccountConfig
                            {
                                Id = "account-a",
                                AccessToken = "access-secret",
                                RefreshToken = "refresh-secret",
                                IdToken = "id-secret"
                            }
                        }
                    }
                }
            };
            var store = new ConfigurationStore(paths);

            store.SaveConfig(config);
            config.Proxy.InboundApiKey = "inbound-secret-updated";
            store.SaveConfig(config);

            var configText = File.ReadAllText(paths.ConfigPath);
            Assert.DoesNotContain("inbound-secret", configText, StringComparison.Ordinal);
            Assert.DoesNotContain("provider-secret", configText, StringComparison.Ordinal);
            Assert.DoesNotContain("access-secret", configText, StringComparison.Ordinal);
            Assert.DoesNotContain("refresh-secret", configText, StringComparison.Ordinal);

            var backupFiles = Directory.EnumerateFiles(paths.ConfigBackupDirectory, "*.json").ToArray();
            Assert.NotEmpty(backupFiles);
            foreach (var backup in backupFiles)
                Assert.DoesNotContain("inbound-secret", File.ReadAllText(backup), StringComparison.Ordinal);

            var reloaded = store.LoadConfig();
            Assert.Equal("inbound-secret-updated", reloaded.Proxy.InboundApiKey);
            Assert.Equal("provider-secret", reloaded.Providers.Single(provider => provider.Id == "provider-a").ApiKey);
            var account = reloaded.Providers.Single(provider => provider.Id == "provider-a").OAuthAccounts.Single();
            Assert.Equal("access-secret", account.AccessToken);
            Assert.Equal("refresh-secret", account.RefreshToken);
            Assert.Equal("id-secret", account.IdToken);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void ConfigurationStore_MigratesLegacyPlaintextCredentialsOnFirstLoad()
    {
        var root = CreateTempDirectory();
        try
        {
            var paths = new AppPaths(root, Path.Combine(root, ".codex"));
            File.WriteAllText(
                paths.ConfigPath,
                """
                {
                  "schemaVersion": 5,
                  "proxy": { "inboundApiKey": "legacy-inbound" },
                  "providers": [
                    {
                      "id": "legacy",
                      "displayName": "Legacy",
                      "baseUrl": "https://example.com/v1",
                      "apiKey": "legacy-api",
                      "oAuthAccounts": [
                        {
                          "id": "account",
                          "accessToken": "legacy-access",
                          "refreshToken": "legacy-refresh",
                          "idToken": "legacy-id"
                        }
                      ]
                    }
                  ],
                  "activeProviderId": "legacy",
                  "activeCodexProviderId": "legacy"
                }
                """,
                Encoding.UTF8);

            var loaded = new ConfigurationStore(paths).LoadConfig();
            var provider = loaded.Providers.Single(item => item.Id == "legacy");
            var account = Assert.Single(provider.OAuthAccounts);

            Assert.Equal("legacy-inbound", loaded.Proxy.InboundApiKey);
            Assert.Equal("legacy-api", provider.ApiKey);
            Assert.Equal("legacy-access", account.AccessToken);
            Assert.Equal("legacy-refresh", account.RefreshToken);
            Assert.DoesNotContain("legacy-inbound", File.ReadAllText(paths.ConfigPath), StringComparison.Ordinal);
            Assert.DoesNotContain("legacy-api", File.ReadAllText(paths.ConfigPath), StringComparison.Ordinal);
            Assert.DoesNotContain("legacy-access", File.ReadAllText(paths.ConfigPath), StringComparison.Ordinal);
            Assert.True(File.Exists(paths.SecretsPath));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void ConfigurationTransfer_WrongPasswordDoesNotExposeOrModifyPayload()
    {
        var root = CreateTempDirectory();
        try
        {
            var paths = new AppPaths(root, Path.Combine(root, ".codex"));
            var service = new ConfigurationTransferService(paths);
            var config = new AppConfig
            {
                Proxy = { InboundApiKey = "transfer-secret" }
            };
            var destination = Path.Combine(root, "config.csx");

            service.Export(config, new ModelPricingCatalog(), "correct-password", destination);

            Assert.DoesNotContain("transfer-secret", File.ReadAllText(destination), StringComparison.Ordinal);
            Assert.Throws<InvalidDataException>(() =>
                service.Import(destination, "wrong-password"));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ExportDiagnostics_RedactsCredentialsAndErrors()
    {
        var root = CreateTempDirectory();
        try
        {
            var paths = new AppPaths(root, Path.Combine(root, ".codex"));
            await using (var writer = new UsageLogWriter(paths))
            {
                writer.Append(new UsageLogRecord
                {
                    ProviderId = "provider-a",
                    Error = "Authorization Bearer log-secret",
                    StatusCode = 500
                });
            }

            var service = new ConfigurationTransferService(paths);
            var destination = Path.Combine(root, "diagnostics.zip");
            service.ExportDiagnostics(
                new AppConfig
                {
                    Proxy = { InboundApiKey = "inbound-secret" },
                    Providers =
                    {
                        new ProviderConfig { Id = "provider-a", ApiKey = "provider-secret" }
                    }
                },
                new ModelPricingCatalog(),
                new ProxyRuntimeState { IsRunning = true },
                destination);

            using var archive = ZipFile.OpenRead(destination);
            var content = string.Join(
                Environment.NewLine,
                archive.Entries.Select(entry =>
                {
                    using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
                    return reader.ReadToEnd();
                }));
            Assert.DoesNotContain("inbound-secret", content, StringComparison.Ordinal);
            Assert.DoesNotContain("provider-secret", content, StringComparison.Ordinal);
            Assert.DoesNotContain("log-secret", content, StringComparison.Ordinal);
            Assert.Contains("[redacted]", content, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "CodexSwitchTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }
}
