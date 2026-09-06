using System.Net;
using System.Text;
using System.Text.Json;
using CodexSwitch.Models;
using CodexSwitch.Proxy;
using CodexSwitch.Services;
using Microsoft.AspNetCore.Http;

namespace CodexSwitch.Tests;

public sealed class OpenAiResponsesAdapterMessagesTests
{
    [Fact]
    public async Task HandleMessagesAsync_NonStreaming_ConvertsAnthropicMessagesToResponses()
    {
        using var requestDocument = JsonDocument.Parse(
            """
            {
              "model": "deepseek-v4-flash",
              "system": [{ "type": "text", "text": "You answer tersely." }],
              "thinking": { "type": "enabled", "budget_tokens": 4096 },
              "max_tokens": 128,
              "tools": [
                {
                  "name": "lookup",
                  "description": "Look up a value.",
                  "input_schema": {
                    "type": "object",
                    "properties": {
                      "query": { "type": "string" }
                    },
                    "required": ["query"]
                  }
                }
              ],
              "tool_choice": { "type": "tool", "name": "lookup" },
              "messages": [
                {
                  "role": "assistant",
                  "content": [
                    { "type": "thinking", "thinking": "Need lookup." },
                    { "type": "text", "text": "I will check." },
                    { "type": "tool_use", "id": "call_lookup_1", "name": "lookup", "input": { "query": "codex" } }
                  ]
                },
                {
                  "role": "user",
                  "content": [
                    { "type": "tool_result", "tool_use_id": "call_lookup_1", "content": "Codex found." },
                    { "type": "text", "text": "Summarize." }
                  ]
                }
              ]
            }
            """);

        using var fixture = new AdapterFixture(
            requestDocument,
            """
            {
              "id": "resp_1",
              "object": "response",
              "status": "completed",
              "model": "deepseek-upstream",
              "output": [
                {
                  "id": "rs_1",
                  "type": "reasoning",
                  "content": [{ "type": "reasoning_text", "text": "Reasoned." }]
                },
                {
                  "id": "msg_1",
                  "type": "message",
                  "role": "assistant",
                  "content": [{ "type": "output_text", "text": "Done." }]
                }
              ],
              "usage": {
                "input_tokens": 20,
                "input_tokens_details": { "cached_tokens": 3 },
                "output_tokens": 5,
                "output_tokens_details": { "reasoning_tokens": 1 }
              }
            }
            """);

        await fixture.InvokeAsync();

        Assert.Single(fixture.Handler.Requests);
        var upstream = fixture.Handler.Requests[0];
        Assert.Equal(HttpMethod.Post, upstream.Method);
        Assert.Equal("/v1/responses", upstream.RequestUri?.AbsolutePath);
        Assert.Equal("Bearer", upstream.Authorization?.Scheme);
        Assert.Equal("provider-secret", upstream.Authorization?.Parameter);

        using var upstreamPayload = JsonDocument.Parse(upstream.Body);
        var root = upstreamPayload.RootElement;
        Assert.Equal("deepseek-upstream", root.GetProperty("model").GetString());
        Assert.Equal("You answer tersely.", root.GetProperty("instructions").GetString());
        Assert.Equal(128, root.GetProperty("max_output_tokens").GetInt32());
        Assert.Equal("enabled", root.GetProperty("thinking").GetProperty("type").GetString());

        var tool = root.GetProperty("tools")[0];
        Assert.Equal("function", tool.GetProperty("type").GetString());
        Assert.Equal("lookup", tool.GetProperty("name").GetString());
        Assert.False(tool.GetProperty("strict").GetBoolean());
        Assert.Equal(JsonValueKind.Object, tool.GetProperty("parameters").ValueKind);
        Assert.Equal("function", root.GetProperty("tool_choice").GetProperty("type").GetString());
        Assert.Equal("lookup", root.GetProperty("tool_choice").GetProperty("name").GetString());

        var input = root.GetProperty("input").EnumerateArray().ToArray();
        Assert.Equal("assistant", input[0].GetProperty("role").GetString());
        Assert.Equal("Need lookup.", input[0].GetProperty("reasoning_content").GetString());
        Assert.Equal("I will check.", input[0].GetProperty("content")[0].GetProperty("text").GetString());
        Assert.Equal("function_call", input[1].GetProperty("type").GetString());
        Assert.Equal("lookup", input[1].GetProperty("name").GetString());
        Assert.Equal("codex", input[1].GetProperty("arguments").GetString()?.Contains("codex") == true ? "codex" : "");
        Assert.Equal("function_call_output", input[2].GetProperty("type").GetString());
        Assert.Equal("Codex found.", input[2].GetProperty("output").GetString());
        Assert.Equal("user", input[3].GetProperty("role").GetString());
        Assert.Equal("Summarize.", input[3].GetProperty("content")[0].GetProperty("text").GetString());

        Assert.Equal(StatusCodes.Status200OK, fixture.HttpContext.Response.StatusCode);
        using var downstream = JsonDocument.Parse(fixture.ResponseBody());
        var response = downstream.RootElement;
        Assert.Equal("message", response.GetProperty("type").GetString());
        Assert.Equal("assistant", response.GetProperty("role").GetString());
        var content = response.GetProperty("content").EnumerateArray().ToArray();
        Assert.Equal("thinking", content[0].GetProperty("type").GetString());
        Assert.Equal("Reasoned.", content[0].GetProperty("thinking").GetString());
        Assert.Equal("text", content[1].GetProperty("type").GetString());
        Assert.Equal("Done.", content[1].GetProperty("text").GetString());
        Assert.Equal("end_turn", response.GetProperty("stop_reason").GetString());
        Assert.Equal(17, response.GetProperty("usage").GetProperty("input_tokens").GetInt64());
        Assert.Equal(3, response.GetProperty("usage").GetProperty("cache_read_input_tokens").GetInt64());
        Assert.Equal(5, response.GetProperty("usage").GetProperty("output_tokens").GetInt64());

        Assert.Equal(1, fixture.UsageMeter.Snapshot.Requests);
        Assert.Equal(17, fixture.UsageMeter.Snapshot.InputTokens);
        Assert.Equal(3, fixture.UsageMeter.Snapshot.CachedInputTokens);
        Assert.Equal(5, fixture.UsageMeter.Snapshot.OutputTokens);
    }

    [Fact]
    public async Task HandleMessagesAsync_RequestOptions_MapsClaudeSpecificControlsToResponses()
    {
        using var requestDocument = JsonDocument.Parse(
            """
            {
              "model": "deepseek-v4-flash",
              "system": "Return JSON.",
              "max_tokens": 64,
              "service_tier": "standard_only",
              "metadata": { "uerd_id": "user-123", "feature": "claude-code" },
              "cache_control": { "type": "ephemeral" },
              "inference_geo": "us",
              "output_config": {
                "format": {
                  "type": "json_schema",
                  "strict": true,
                  "schema": {
                    "title": "LookupResult",
                    "type": "object",
                    "properties": {
                      "answer": { "type": "string" }
                    }
                  }
                }
              },
              "tools": [
                {
                  "name": "lookup",
                  "description": "Look up a value.",
                  "strict": true,
                  "input_schema": {
                    "type": "object",
                    "properties": {
                      "query": { "type": "string" }
                    }
                  }
                },
                {
                  "type": "web_search_20250305",
                  "name": "web_search",
                  "max_uses": 1
                }
              ],
              "tool_choice": { "type": "auto", "disable_parallel_tool_use": true },
              "messages": [
                { "role": "user", "content": "Find codex." }
              ]
            }
            """);

        using var fixture = new AdapterFixture(
            requestDocument,
            """
            {
              "id": "resp_options",
              "object": "response",
              "status": "completed",
              "model": "deepseek-upstream",
              "output": [
                {
                  "id": "msg_1",
                  "type": "message",
                  "role": "assistant",
                  "content": [{ "type": "output_text", "text": "{\"answer\":\"Done\"}" }]
                }
              ],
              "usage": {
                "input_tokens": 9,
                "output_tokens": 4
              }
            }
            """);

        await fixture.InvokeAsync();

        using var upstreamPayload = JsonDocument.Parse(fixture.Handler.Requests[0].Body);
        var root = upstreamPayload.RootElement;
        Assert.Equal("default", root.GetProperty("service_tier").GetString());
        Assert.Equal("user-123", root.GetProperty("prompt_cache_key").GetString());
        Assert.False(root.TryGetProperty("metadata", out _));
        Assert.False(root.TryGetProperty("safety_identifier", out _));
        Assert.False(root.GetProperty("parallel_tool_calls").GetBoolean());
        Assert.False(root.TryGetProperty("cache_control", out _));
        Assert.False(root.TryGetProperty("inference_geo", out _));
        Assert.False(root.TryGetProperty("output_config", out _));

        var format = root.GetProperty("text").GetProperty("format");
        Assert.Equal("json_schema", format.GetProperty("type").GetString());
        Assert.Equal("LookupResult", format.GetProperty("name").GetString());
        Assert.True(format.GetProperty("strict").GetBoolean());
        Assert.Equal("object", format.GetProperty("schema").GetProperty("type").GetString());

        var tools = root.GetProperty("tools").EnumerateArray().ToArray();
        Assert.Single(tools);
        Assert.Equal("lookup", tools[0].GetProperty("name").GetString());
        Assert.True(tools[0].GetProperty("strict").GetBoolean());
        Assert.True(tools[0].GetProperty("parameters").GetProperty("additionalProperties").ValueKind == JsonValueKind.False);
        Assert.Equal("query", tools[0].GetProperty("parameters").GetProperty("required")[0].GetString());
        Assert.Equal("auto", root.GetProperty("tool_choice").GetString());
    }

    [Fact]
    public async Task HandleMessagesAsync_RequestOptions_UsesStableDefaultPromptCacheKeyWhenMetadataUserIdIsMissing()
    {
        using var firstRequestDocument = JsonDocument.Parse(
            """
            {
              "model": "deepseek-v4-flash",
              "metadata": { "feature": "claude-code" },
              "max_tokens": 64,
              "messages": [
                { "role": "user", "content": "First." }
              ]
            }
            """);
        using var secondRequestDocument = JsonDocument.Parse(
            """
            {
              "model": "deepseek-v4-flash",
              "max_tokens": 64,
              "messages": [
                { "role": "user", "content": "Second." }
              ]
            }
            """);

        using var firstFixture = new AdapterFixture(firstRequestDocument, BasicResponsesBody());
        using var secondFixture = new AdapterFixture(secondRequestDocument, BasicResponsesBody());

        await firstFixture.InvokeAsync();
        await secondFixture.InvokeAsync();

        using var firstPayload = JsonDocument.Parse(firstFixture.Handler.Requests[0].Body);
        using var secondPayload = JsonDocument.Parse(secondFixture.Handler.Requests[0].Body);
        var firstKey = firstPayload.RootElement.GetProperty("prompt_cache_key").GetString();
        var secondKey = secondPayload.RootElement.GetProperty("prompt_cache_key").GetString();

        Assert.False(firstPayload.RootElement.TryGetProperty("metadata", out _));
        Assert.False(string.IsNullOrWhiteSpace(firstKey));
        Assert.StartsWith("codexswitch-", firstKey);
        Assert.Equal(firstKey, secondKey);
    }

    [Fact]
    public async Task HandleMessagesAsync_OpenAiReasoningHistory_DropsClaudeThinkingAndKeepsToolTurnCompatible()
    {
        using var requestDocument = JsonDocument.Parse(
            """
            {
              "model": "gpt-5.5",
              "max_tokens": 64,
              "messages": [
                {
                  "role": "user",
                  "content": "Use lookup."
                },
                {
                  "role": "assistant",
                  "content": [
                    { "type": "thinking", "thinking": "Need lookup." },
                    { "type": "tool_use", "id": "call_lookup_1", "name": "lookup", "input": { "query": "codex" } }
                  ]
                },
                {
                  "role": "user",
                  "content": [
                    { "type": "tool_result", "tool_use_id": "call_lookup_1", "content": "Codex found." }
                  ]
                }
              ],
              "tools": [
                {
                  "name": "lookup",
                  "input_schema": {
                    "type": "object",
                    "properties": {
                      "query": { "type": "string" }
                    },
                    "required": ["query"]
                  }
                }
              ]
            }
            """);

        using var fixture = new AdapterFixture(
            requestDocument,
            BasicResponsesBody(),
            modelId: "gpt-5.5",
            upstreamModel: "gpt-5.5",
            providerDisplayName: "OpenAI Official",
            providerDefaultModel: "gpt-5.5");

        await fixture.InvokeAsync();

        using var upstreamPayload = JsonDocument.Parse(fixture.Handler.Requests[0].Body);
        var input = upstreamPayload.RootElement.GetProperty("input").EnumerateArray().ToArray();

        Assert.Equal("message", input[0].GetProperty("type").GetString());
        Assert.Equal("function_call", input[1].GetProperty("type").GetString());
        Assert.False(input[1].TryGetProperty("status", out _));
        Assert.Equal("call_lookup_1", input[1].GetProperty("call_id").GetString());
        Assert.Equal("function_call_output", input[2].GetProperty("type").GetString());
        Assert.Equal("call_lookup_1", input[2].GetProperty("call_id").GetString());
        Assert.DoesNotContain(input, item => item.GetProperty("type").GetString() == "reasoning");
        Assert.DoesNotContain(
            input,
            item => item.GetProperty("type").GetString() == "message" &&
                item.GetProperty("role").GetString() == "assistant" &&
                item.GetProperty("content").GetArrayLength() == 0);
    }

    [Fact]
    public async Task HandleMessagesAsync_ToolResultContentBlocks_ConvertsToResponsesOutputContentArray()
    {
        using var requestDocument = JsonDocument.Parse(
            """
            {
              "model": "gpt-5.5",
              "max_tokens": 64,
              "messages": [
                {
                  "role": "assistant",
                  "content": [
                    { "type": "tool_use", "id": "call_lookup_1", "name": "lookup", "input": { "query": "codex" } }
                  ]
                },
                {
                  "role": "user",
                  "content": [
                    {
                      "type": "tool_result",
                      "tool_use_id": "call_lookup_1",
                      "content": [
                        { "type": "text", "text": "Screenshot attached." },
                        {
                          "type": "image",
                          "source": {
                            "type": "base64",
                            "media_type": "image/png",
                            "data": "aGVsbG8="
                          }
                        }
                      ]
                    }
                  ]
                }
              ],
              "tools": [
                {
                  "name": "lookup",
                  "input_schema": {
                    "type": "object",
                    "properties": {
                      "query": { "type": "string" }
                    }
                  }
                }
              ]
            }
            """);

        using var fixture = new AdapterFixture(
            requestDocument,
            BasicResponsesBody(),
            modelId: "gpt-5.5",
            upstreamModel: "gpt-5.5",
            providerDisplayName: "OpenAI Official",
            providerDefaultModel: "gpt-5.5");

        await fixture.InvokeAsync();

        using var upstreamPayload = JsonDocument.Parse(fixture.Handler.Requests[0].Body);
        var output = upstreamPayload.RootElement.GetProperty("input")[1].GetProperty("output");
        Assert.Equal(JsonValueKind.Array, output.ValueKind);
        Assert.Equal("input_text", output[0].GetProperty("type").GetString());
        Assert.Equal("Screenshot attached.", output[0].GetProperty("text").GetString());
        Assert.Equal("input_image", output[1].GetProperty("type").GetString());
        Assert.Equal("data:image/png;base64,aGVsbG8=", output[1].GetProperty("image_url").GetString());
    }

    [Fact]
    public async Task HandleMessagesAsync_Streaming_ConvertsResponsesSseToAnthropicSse()
    {
        using var requestDocument = JsonDocument.Parse(
            """
            {
              "model": "deepseek-v4-flash",
              "stream": true,
              "max_tokens": 32,
              "messages": [
                { "role": "user", "content": "Stream a result." }
              ]
            }
            """);

        using var fixture = new AdapterFixture(
            requestDocument,
            """
            event: response.created
            data: {"type":"response.created","response":{"id":"resp_stream","model":"deepseek-upstream"}}

            event: response.output_item.added
            data: {"type":"response.output_item.added","output_index":0,"item":{"id":"rs_1","type":"reasoning"}}

            event: response.reasoning_text.delta
            data: {"type":"response.reasoning_text.delta","output_index":0,"delta":"Think"}

            event: response.output_text.delta
            data: {"type":"response.output_text.delta","output_index":1,"delta":"Hi"}

            event: response.output_item.added
            data: {"type":"response.output_item.added","output_index":2,"item":{"id":"fc_1","type":"function_call","call_id":"call_1","name":"lookup"}}

            event: response.function_call_arguments.delta
            data: {"type":"response.function_call_arguments.delta","output_index":2,"delta":"{\"q\""}

            event: response.function_call_arguments.delta
            data: {"type":"response.function_call_arguments.delta","output_index":2,"delta":":\"x\"}"}

            event: response.completed
            data: {"type":"response.completed","response":{"id":"resp_stream","model":"deepseek-upstream","usage":{"input_tokens":8,"output_tokens":4}}}

            """,
            mediaType: "text/event-stream");

        await fixture.InvokeAsync();

        using var upstreamPayload = JsonDocument.Parse(fixture.Handler.Requests[0].Body);
        Assert.True(upstreamPayload.RootElement.GetProperty("stream").GetBoolean());

        var downstream = fixture.ResponseBody();
        Assert.Contains("event: message_start", downstream);
        Assert.Contains("\"model\":\"deepseek-upstream\"", downstream);
        Assert.Contains("\"type\":\"thinking\"", downstream);
        Assert.Contains("\"type\":\"thinking_delta\"", downstream);
        Assert.Contains("\"thinking\":\"Think\"", downstream);
        Assert.Contains("\"type\":\"text_delta\"", downstream);
        Assert.Contains("\"text\":\"Hi\"", downstream);
        Assert.Contains("\"type\":\"tool_use\"", downstream);
        Assert.Contains("\"type\":\"input_json_delta\"", downstream);
        Assert.Contains("q", downstream);
        Assert.Contains("x", downstream);
        Assert.Contains("event: message_delta", downstream);
        Assert.Contains("\"stop_reason\":\"tool_use\"", downstream);
        Assert.Contains("event: message_stop", downstream);

        Assert.Equal(1, fixture.UsageMeter.Snapshot.Requests);
        Assert.Equal(8, fixture.UsageMeter.Snapshot.InputTokens);
        Assert.Equal(4, fixture.UsageMeter.Snapshot.OutputTokens);
    }

    private sealed class AdapterFixture : IDisposable
    {
        private readonly AppPaths _paths;
        private readonly UsageLogWriter _usageLogWriter;

        public AdapterFixture(
            JsonDocument requestDocument,
            string upstreamBody,
            string mediaType = "application/json",
            string modelId = "deepseek-v4-flash",
            string upstreamModel = "deepseek-upstream",
            string providerDisplayName = "DeepSeek Responses",
            string providerDefaultModel = "deepseek-v4-flash")
        {
            var root = Path.Combine(Path.GetTempPath(), "CodexSwitch.Tests", Guid.NewGuid().ToString("N"));
            _paths = new AppPaths(
                root,
                Path.Combine(root, ".codex"),
                Path.Combine(root, ".claude"));

            Handler = new CapturingHttpMessageHandler(upstreamBody, mediaType);
            HttpContext = new DefaultHttpContext();
            HttpContext.Response.Body = new MemoryStream();
            UsageMeter = new UsageMeter(PriceCalculator);
            _usageLogWriter = new UsageLogWriter(_paths);

            var provider = new ProviderConfig
            {
                Id = "provider-openai-responses",
                DisplayName = providerDisplayName,
                Protocol = ProviderProtocol.OpenAiResponses,
                BaseUrl = "https://upstream.example/v1",
                ApiKey = "provider-secret",
                DefaultModel = providerDefaultModel,
                SupportsClaudeCode = true
            };

            var route = new ModelRouteConfig
            {
                Id = modelId,
                Protocol = ProviderProtocol.OpenAiResponses,
                UpstreamModel = upstreamModel
            };

            provider.Models.Add(route);

            var config = new AppConfig
            {
                ActiveClaudeCodeProviderId = provider.Id,
                Providers = { provider }
            };

            var authService = new ProviderAuthService(
                new ConfigurationStore(_paths),
                config,
                new HttpClient(new CapturingHttpMessageHandler("{}")));

            Context = new ProviderRequestContext(
                HttpContext,
                config,
                ClientAppKind.ClaudeCode,
                provider,
                route,
                new ProviderCostSettings(),
                accessToken: null,
                providerAuthService: authService,
                requestDocument: requestDocument,
                responseStateStore: new ResponsesConversationStateStore(),
                usageMeter: UsageMeter,
                priceCalculator: PriceCalculator,
                usageLogWriter: _usageLogWriter);
        }

        public CapturingHttpMessageHandler Handler { get; }

        public DefaultHttpContext HttpContext { get; }

        public ProviderRequestContext Context { get; }

        public UsageMeter UsageMeter { get; }

        private PriceCalculator PriceCalculator { get; } = new(new ModelPricingCatalog());

        public async Task InvokeAsync()
        {
            var adapter = new OpenAiResponsesAdapter(new HttpClient(Handler));
            await ((IProviderProtocolAdapter)adapter).HandleMessagesAsync(Context, CancellationToken.None);
        }

        public string ResponseBody()
        {
            HttpContext.Response.Body.Position = 0;
            using var reader = new StreamReader(HttpContext.Response.Body, Encoding.UTF8, leaveOpen: true);
            return reader.ReadToEnd();
        }

        public void Dispose()
        {
            _usageLogWriter.DisposeAsync().AsTask().GetAwaiter().GetResult();
            if (Directory.Exists(_paths.RootDirectory))
                Directory.Delete(_paths.RootDirectory, recursive: true);
        }
    }

    private sealed class CapturingHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _body;
        private readonly string _mediaType;
        private readonly HttpStatusCode _statusCode;

        public CapturingHttpMessageHandler(
            string body,
            string mediaType = "application/json",
            HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            _body = body;
            _mediaType = mediaType;
            _statusCode = statusCode;
        }

        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? ""
                : await request.Content.ReadAsStringAsync(cancellationToken);

            Requests.Add(new CapturedRequest(
                request.Method,
                request.RequestUri,
                request.Headers.Authorization,
                body));

            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_body, Encoding.UTF8, _mediaType)
            };
        }
    }

    private sealed record CapturedRequest(
        HttpMethod Method,
        Uri? RequestUri,
        System.Net.Http.Headers.AuthenticationHeaderValue? Authorization,
        string Body);

    private static string BasicResponsesBody()
    {
        return """
        {
          "id": "resp_basic",
          "object": "response",
          "status": "completed",
          "model": "deepseek-upstream",
          "output": [
            {
              "id": "msg_basic",
              "type": "message",
              "role": "assistant",
              "content": [{ "type": "output_text", "text": "Done." }]
            }
          ],
          "usage": {
            "input_tokens": 1,
            "output_tokens": 1
          }
        }
        """;
    }
}
