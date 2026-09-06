using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using CodexSwitch.Models;
using CodexSwitch.Services;

namespace CodexSwitch.Tests;

public sealed class CodexOAuthJsonImportServiceTests
{
    [Fact]
    public void Import_CodexAuthJsonFixture_AddsOAuthAccountWithoutLogin()
    {
        var service = CreateImportService();
        var json = File.ReadAllText(FindRepoFile("docs", "fake-codex-auth.json"));

        var result = service.Import(json);

        var account = Assert.Single(result.Accounts);
        Assert.Empty(result.Skipped);
        Assert.StartsWith("codex-", account.Id, StringComparison.Ordinal);
        Assert.StartsWith("eyJ", account.AccessToken, StringComparison.Ordinal);
        Assert.Equal("fake-refresh-token-for-local-test-only", account.RefreshToken);
        Assert.Equal("plus", account.PlanType);
        Assert.Equal("fake-account", account.ChatgptAccountId);
        Assert.Equal("fake-codex@example.invalid", account.Email);
        Assert.NotNull(account.ExpiresAt);
        Assert.True(account.ExpiresAt.Value > DateTimeOffset.Parse("2030-01-01T00:00:00Z"));
    }

    [Fact]
    public void Import_OpenCoworkStyleArray_AddsAccounts()
    {
        var service = CreateImportService();
        const string json = """
        [
          {
            "email": "a@example.com",
            "access_token": "access",
            "refresh_token": "refresh",
            "account_id": "workspace-account",
            "plan_type": "team"
          }
        ]
        """;

        var result = service.Import(json);

        var account = Assert.Single(result.Accounts);
        Assert.Equal("access", account.AccessToken);
        Assert.Equal("refresh", account.RefreshToken);
        Assert.Equal("a@example.com", account.Email);
        Assert.Equal("workspace-account", account.ChatgptAccountId);
        Assert.Equal("team", account.PlanType);
        Assert.Equal("a@example.com", account.DisplayName);
    }

    [Fact]
    public async Task ProbeAsync_StoresCodexQuotaHeadersOnRequestedOAuthAccount()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "CodexSwitchTests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(tempRoot, Path.Combine(tempRoot, ".codex"), Path.Combine(tempRoot, ".claude"));
        var config = new AppConfig();
        var provider = ProviderTemplateCatalog.CreateProvider(ProviderTemplateCatalog.CodexOAuthBuiltinId, []);
        provider.BaseUrl = "https://chatgpt.com/backend-api/codex";
        provider.ActiveAccountId = "other";
        provider.OAuthAccounts.Add(new OAuthAccountConfig
        {
            Id = "target",
            DisplayName = "Target",
            AccessToken = "target-token",
            ChatgptAccountId = "chatgpt-account"
        });
        provider.OAuthAccounts.Add(new OAuthAccountConfig
        {
            Id = "other",
            DisplayName = "Other",
            AccessToken = "other-token"
        });
        config.Providers.Add(provider);

        using var httpClient = new HttpClient(new QuotaProbeHandler());
        var authService = new ProviderAuthService(new ConfigurationStore(paths), config, httpClient);
        var service = new CodexQuotaProbeService(httpClient, authService);

        var result = await service.ProbeAsync(provider, "target", CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(result.QuotaUpdated);
        var target = provider.OAuthAccounts.Single(account => account.Id == "target");
        Assert.Equal(31, target.Quota?.PrimaryUsedPercent);
        Assert.Equal(300, target.Quota?.PrimaryWindowMinutes);
        Assert.Equal(7, target.Quota?.SecondaryUsedPercent);
        Assert.Equal(10080, target.Quota?.SecondaryWindowMinutes);
        Assert.Equal("12.50", target.Quota?.CreditsBalance);
        Assert.Equal("plus", target.PlanType);
        Assert.Null(provider.OAuthAccounts.Single(account => account.Id == "other").Quota);
    }

    private static CodexOAuthJsonImportService CreateImportService()
    {
        return new CodexOAuthJsonImportService(new CodexOAuthHelper(new HttpClient()));
    }

    private static string FindRepoFile(string part1, string part2, [CallerFilePath] string sourceFile = "")
    {
        var parts = new[] { part1, part2 };
        var sourceDirectory = Path.GetDirectoryName(sourceFile) ?? "";
        foreach (var start in new[] { sourceDirectory, Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                var candidate = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
                if (File.Exists(candidate))
                    return candidate;

                directory = directory.Parent;
            }
        }

        throw new FileNotFoundException("Could not find repo file.", Path.Combine(parts));
    }

    private sealed class QuotaProbeHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://chatgpt.com/backend-api/codex/responses", request.RequestUri?.ToString());
            Assert.Equal("Bearer target-token", request.Headers.Authorization?.ToString());
            Assert.True(request.Headers.TryGetValues("Chatgpt-Account-Id", out var accountIds));
            Assert.Equal("chatgpt-account", Assert.Single(accountIds));
            Assert.True(request.Headers.TryGetValues("session_id", out var sessionIds));
            Assert.StartsWith("cs_quota_", Assert.Single(sessionIds), StringComparison.Ordinal);

            var body = request.Content is null
                ? ""
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Assert.Contains("\"store\":false", body, StringComparison.Ordinal);
            Assert.Contains("\"model\":\"gpt-5.1-codex\"", body, StringComparison.Ordinal);

            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Headers.TryAddWithoutValidation("x-codex-plan-type", "plus");
            response.Headers.TryAddWithoutValidation("x-codex-primary-used-percent", "31");
            response.Headers.TryAddWithoutValidation("x-codex-primary-window-minutes", "300");
            response.Headers.TryAddWithoutValidation("x-codex-primary-reset-after-seconds", "900");
            response.Headers.TryAddWithoutValidation("x-codex-secondary-used-percent", "7");
            response.Headers.TryAddWithoutValidation("x-codex-secondary-window-minutes", "10080");
            response.Headers.TryAddWithoutValidation("x-codex-credits-has-credits", "true");
            response.Headers.TryAddWithoutValidation("x-codex-credits-balance", "12.50");
            return response;
        }
    }
}
