using System.Net;
using System.Text;
using System.Text.Json;
using CodexSwitch.Models;
using CodexSwitch.Proxy;
using CodexSwitch.Services;
using CodexSwitch.ViewModels;
using Microsoft.AspNetCore.Http;

namespace CodexSwitch.Tests;

public sealed class Gpt61SolUsageTests
{
    [Theory]
    [InlineData(false, "default", false, null, 1.485)]
    [InlineData(false, "default", true, null, 1.485)]
    [InlineData(false, "priority", false, null, 2.97)]
    [InlineData(false, "flex", false, null, 0.7425)]
    [InlineData(false, null, true, null, 2.97)]
    [InlineData(false, null, false, "flex", 0.7425)]
    [InlineData(true, "default", false, null, 1.485)]
    [InlineData(true, "default", true, null, 1.485)]
    [InlineData(true, "priority", false, null, 2.97)]
    [InlineData(true, "flex", false, null, 0.7425)]
    [InlineData(true, null, true, null, 2.97)]
    [InlineData(true, null, false, "flex", 0.7425)]
    public async Task HandleResponses_Gpt61SolMapsUltraToUpstreamMaxAndLogsActualServiceTier(
        bool stream, string? responseTier, bool fastMode, string? requestTier, double expectedCost)
    {
        var root = Path.Combine(Path.GetTempPath(), "CodexSwitchTests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(root, Path.Combine(root, ".codex"));
            var calculator = new PriceCalculator(new ModelPricingCatalog
            {
                Models = BuiltInModelCatalog.CreatePricingRules()
            });
            var meter = new UsageMeter(calculator);
            await using var writer = new UsageLogWriter(paths);
            var provider = new ProviderConfig
            {
                Id = "openai",
                Protocol = ProviderProtocol.OpenAiResponses,
                BaseUrl = "https://upstream.example/v1",
                DefaultModel = "gpt-6.1-sol"
            };
            var config = new AppConfig { Providers = { provider } };
            var response = new
            {
                id = "resp_gpt61",
                model = "gpt-6.1-sol",
                service_tier = responseTier,
                output = Array.Empty<object>(),
                usage = new
                {
                    input_tokens = 375_000,
                    input_tokens_details = new { cached_tokens = 50_000, cache_creation_input_tokens = 25_000 },
                    output_tokens = 10_000
                }
            };
            var body = stream
                ? $"event: response.completed\ndata: {JsonSerializer.Serialize(new { type = "response.completed", response })}\n\n"
                : JsonSerializer.Serialize(response);
            var handler = new ResponseHandler(body, stream);
            using var client = new HttpClient(handler);
            using var snapshot = ResponsesRequestSnapshot.Parse(JsonSerializer.Serialize(new
            {
                model = "gpt-6.1-sol",
                input = "ping",
                stream,
                service_tier = requestTier,
                reasoning = new { effort = "ultra" }
            }));
            var httpContext = new DefaultHttpContext();
            httpContext.Response.Body = new MemoryStream();
            var context = new ProviderRequestContext(
                httpContext, config, ClientAppKind.Codex, provider, null,
                new ProviderCostSettings { FastMode = fastMode }, null,
                new ProviderAuthService(new ConfigurationStore(paths), config, client),
                snapshot, new ResponsesConversationStateStore(), meter, calculator, writer);

            var result = await new OpenAiResponsesAdapter(client).HandleResponsesAsync(context, CancellationToken.None);
            await writer.DisposeAsync();

            Assert.Equal(ProviderAdapterResultKind.Success, result.Kind);
            Assert.Equal(1, meter.Snapshot.Requests);
            Assert.Equal(0, meter.Snapshot.Errors);
            Assert.Equal((decimal)expectedCost, meter.Snapshot.EstimatedCost);
            var dashboard = new UsageLogReader(paths).Read(UsageTimeRange.Last24Hours);
            var record = Assert.Single(dashboard.Logs);
            Assert.Equal("gpt-6.1-sol", record.BilledModel);
            Assert.Equal(responseTier ?? (fastMode ? "priority" : requestTier), record.ServiceTier);
            Assert.Equal((decimal)expectedCost, record.EstimatedCost);
            Assert.Equal(DisplayFormatters.FormatCost(record.EstimatedCost), UsageLogItem.From(record).Cost);
            Assert.Equal(record.ServiceTier ?? "default", UsageLogItem.From(record).ServiceTier);
            using var upstream = JsonDocument.Parse(handler.RequestBody!);
            Assert.Equal("max", upstream.RootElement.GetProperty("reasoning").GetProperty("effort").GetString());
            if (fastMode)
                Assert.Equal("priority", upstream.RootElement.GetProperty("service_tier").GetString());
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(false, "priority", null, "priority")]
    [InlineData(false, "priority", "flex", "flex")]
    [InlineData(true, null, "flex", "flex")]
    public void BillingServiceTierMatchesConfiguredUpstreamPayload(
        bool snapshotMode, string? providerTier, string? modelTier, string expectedTier)
    {
        var provider = new ProviderConfig { DefaultModel = "gpt-6.1-sol", ServiceTier = providerTier };
        var model = new ModelRouteConfig { Id = "gpt-6.1-sol", ServiceTier = modelTier };
        using var snapshot = ResponsesRequestSnapshot.Parse("""{"model":"gpt-6.1-sol","input":"ping"}""");
        var payload = snapshotMode
            ? ResponsesPayloadBuilder.Build(snapshot, provider, model, new ProviderCostSettings())
            : ResponsesPayloadBuilder.Build(snapshot.RootElement, provider, model, new ProviderCostSettings());
        using var upstream = JsonDocument.Parse(payload);
        Assert.Equal(expectedTier, upstream.RootElement.GetProperty("service_tier").GetString());
    }

    private sealed class ResponseHandler(string body, bool stream) : HttpMessageHandler
    {
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, stream ? "text/event-stream" : "application/json")
            };
        }
    }
}
