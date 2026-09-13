using System.Net;
using CodexSwitch.Models;
using CodexSwitch.Services;

namespace CodexSwitch.Tests;

public sealed class ProviderModelDiscoveryServiceTests
{
    [Fact]
    public async Task FetchRoutesAsync_CallsModelsEndpointWithBearerTokenAndReturnsDistinctRoutes()
    {
        using var handler = new CaptureHandler("""
            {
              "object": "list",
              "data": [
                { "id": "grok-4.6" },
                { "id": "gpt-5.6-terra" },
                { "id": "gpt-5.6-terra" },
                { "id": "custom-model" }
              ]
            }
            """);
        using var httpClient = new HttpClient(handler);
        var root = CreateTempDirectory();
        try
        {
            var config = new AppConfig();
            var store = new ConfigurationStore(new AppPaths(root, Path.Combine(root, ".codex")));
            var auth = new ProviderAuthService(store, config, httpClient);
            var service = new ProviderModelDiscoveryService(httpClient, auth);

            var routes = await service.FetchRoutesAsync(
                new ProviderConfig
                {
                    BaseUrl = "https://aioss.cc/v1/",
                    ApiKey = "test-key",
                    Protocol = ProviderProtocol.OpenAiChat
                },
                CancellationToken.None);

            var request = Assert.Single(handler.Requests);
            Assert.Equal("https://aioss.cc/v1/models", request.Uri?.AbsoluteUri);
            Assert.Equal("Bearer", request.AuthorizationScheme);
            Assert.Equal("test-key", request.AuthorizationParameter);
            Assert.Equal(["gpt-5.6-terra", "grok-4.6", "custom-model"], routes.Select(route => route.Id));
            Assert.All(routes, route => Assert.Equal(ProviderProtocol.OpenAiChat, route.Protocol));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task FetchRoutesAsync_AnthropicMessagesUsesAnthropicModelsEndpointAndHeaders()
    {
        using var handler = new CaptureHandler("""
            {
              "data": [
                { "id": "claude-sonnet-5", "display_name": "Claude Sonnet 5" }
              ]
            }
            """);
        using var httpClient = new HttpClient(handler);
        var root = CreateTempDirectory();
        try
        {
            var config = new AppConfig();
            var store = new ConfigurationStore(new AppPaths(root, Path.Combine(root, ".codex")));
            var auth = new ProviderAuthService(store, config, httpClient);
            var service = new ProviderModelDiscoveryService(httpClient, auth);

            var routes = await service.FetchRoutesAsync(
                new ProviderConfig
                {
                    BaseUrl = "https://api.anthropic.com/v1",
                    ApiKey = "anthropic-key",
                    Protocol = ProviderProtocol.AnthropicMessages
                },
                CancellationToken.None);

            var request = Assert.Single(handler.Requests);
            Assert.Equal("https://api.anthropic.com/v1/models?limit=1000", request.Uri?.AbsoluteUri);
            Assert.Null(request.AuthorizationScheme);
            Assert.Equal("anthropic-key", request.ApiKey);
            Assert.Equal("2023-06-01", request.AnthropicVersion);
            var route = Assert.Single(routes);
            Assert.Equal("claude-sonnet-5", route.Id);
            Assert.Equal("Claude Sonnet 5", route.DisplayName);
            Assert.Equal(ProviderProtocol.AnthropicMessages, route.Protocol);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task FetchRoutesAsync_AnthropicMessagesRetriesWithBearerForCompatibleRelays()
    {
        using var handler = new CaptureHandler(
            (HttpStatusCode.Unauthorized, """{ "error": "unauthorized" }"""),
            (HttpStatusCode.OK, """{ "data": [ { "id": "claude-sonnet-5" } ] }"""));
        using var httpClient = new HttpClient(handler);
        var root = CreateTempDirectory();
        try
        {
            var config = new AppConfig();
            var store = new ConfigurationStore(new AppPaths(root, Path.Combine(root, ".codex")));
            var auth = new ProviderAuthService(store, config, httpClient);
            var service = new ProviderModelDiscoveryService(httpClient, auth);

            var routes = await service.FetchRoutesAsync(
                new ProviderConfig
                {
                    BaseUrl = "https://aioss.cc/v1",
                    ApiKey = "relay-key",
                    Protocol = ProviderProtocol.AnthropicMessages
                },
                CancellationToken.None);

            Assert.Equal(2, handler.Requests.Count);
            Assert.Equal("relay-key", handler.Requests[0].ApiKey);
            Assert.Null(handler.Requests[0].AuthorizationScheme);
            Assert.Equal("Bearer", handler.Requests[1].AuthorizationScheme);
            Assert.Equal("relay-key", handler.Requests[1].AuthorizationParameter);
            Assert.Equal("claude-sonnet-5", Assert.Single(routes).Id);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task FetchRoutesAsync_RequiresCredential()
    {
        using var handler = new CaptureHandler("""{ "data": [] }""");
        using var httpClient = new HttpClient(handler);
        var root = CreateTempDirectory();
        try
        {
            var config = new AppConfig();
            var store = new ConfigurationStore(new AppPaths(root, Path.Combine(root, ".codex")));
            var auth = new ProviderAuthService(store, config, httpClient);
            var service = new ProviderModelDiscoveryService(httpClient, auth);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.FetchRoutesAsync(
                    new ProviderConfig
                    {
                        BaseUrl = "https://aioss.cc/v1",
                        Protocol = ProviderProtocol.OpenAiResponses
                    },
                    CancellationToken.None));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "codexswitch-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class CaptureHandler : HttpMessageHandler, IDisposable
    {
        private readonly Queue<(HttpStatusCode StatusCode, string Content)> _responses;

        public CaptureHandler(string content)
            : this((HttpStatusCode.OK, content))
        {
        }

        public CaptureHandler(params (HttpStatusCode StatusCode, string Content)[] responses)
        {
            _responses = new Queue<(HttpStatusCode StatusCode, string Content)>(responses);
        }

        public List<CapturedRequest> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new CapturedRequest(
                request.RequestUri,
                request.Headers.Authorization?.Scheme,
                request.Headers.Authorization?.Parameter,
                request.Headers.TryGetValues("x-api-key", out var apiKeys) ? apiKeys.SingleOrDefault() : null,
                request.Headers.TryGetValues("anthropic-version", out var versions) ? versions.SingleOrDefault() : null));

            var response = _responses.Count > 0
                ? _responses.Dequeue()
                : (HttpStatusCode.OK, """{ "data": [] }""");
            return Task.FromResult(new HttpResponseMessage(response.Item1)
            {
                Content = new StringContent(response.Item2)
            });
        }

        public sealed record CapturedRequest(
            Uri? Uri,
            string? AuthorizationScheme,
            string? AuthorizationParameter,
            string? ApiKey,
            string? AnthropicVersion);
    }
}
