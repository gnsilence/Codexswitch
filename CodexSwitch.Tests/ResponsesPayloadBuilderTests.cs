using System.Text;
using System.Text.Json;
using CodexSwitch.Models;
using CodexSwitch.Proxy;

namespace CodexSwitch.Tests;

public sealed class ResponsesPayloadBuilderTests
{
    [Fact]
    public void Build_WithSnapshotAndNoBodyRewrite_ReusesOriginalBytes()
    {
        var json = """{ "model" : "gpt-5.5", "input" : [ { "role" : "user", "content" : "hi" } ] }""";
        var raw = Encoding.UTF8.GetBytes(json);
        using var snapshot = ResponsesRequestSnapshot.Parse(raw);
        var provider = new ProviderConfig
        {
            DefaultModel = "gpt-5.5",
            Protocol = ProviderProtocol.OpenAiResponses
        };
        var model = new ModelRouteConfig
        {
            Id = "gpt-5.5",
            Protocol = ProviderProtocol.OpenAiResponses
        };

        var bytes = ResponsesPayloadBuilder.Build(snapshot, provider, model, new ProviderCostSettings());

        Assert.Same(raw, bytes);
        Assert.Equal(json, Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void Build_WithSnapshotOverrides_RewritesSelectedTopLevelFields()
    {
        using var snapshot = ResponsesRequestSnapshot.Parse(
            """
            {
              "model": "local-alias",
              "service_tier": "standard",
              "store": true,
              "instructions": "old",
              "metadata": { "trace": "drop" },
              "input": "hello"
            }
            """);
        var provider = new ProviderConfig
        {
            DefaultModel = "fallback",
            Protocol = ProviderProtocol.OpenAiResponses,
            RequestOverrides = new ProviderRequestOverrides
            {
                ForceStoreFalse = true,
                Instructions = "new",
                OmitBodyKeys = { "metadata" }
            }
        };
        var model = new ModelRouteConfig
        {
            Id = "local-alias",
            UpstreamModel = "gpt-5.5",
            ServiceTier = "priority",
            Protocol = ProviderProtocol.OpenAiResponses
        };

        var bytes = ResponsesPayloadBuilder.Build(snapshot, provider, model, new ProviderCostSettings());

        using var output = JsonDocument.Parse(bytes);
        var root = output.RootElement;
        Assert.Equal("gpt-5.5", root.GetProperty("model").GetString());
        Assert.Equal("priority", root.GetProperty("service_tier").GetString());
        Assert.False(root.GetProperty("store").GetBoolean());
        Assert.Equal("new", root.GetProperty("instructions").GetString());
        Assert.Equal("hello", root.GetProperty("input").GetString());
        Assert.False(root.TryGetProperty("metadata", out _));
    }

    [Fact]
    public void Build_ForFastModel_AddsPriorityServiceTier()
    {
        using var document = JsonDocument.Parse("""{"model":"gpt-5.5","input":"hello"}""");
        var provider = new ProviderConfig
        {
            DefaultModel = "gpt-5.5",
            Protocol = ProviderProtocol.OpenAiResponses
        };
        var model = new ModelRouteConfig
        {
            Id = "gpt-5.5",
            Protocol = ProviderProtocol.OpenAiResponses
        };

        var bytes = ResponsesPayloadBuilder.Build(
            document.RootElement,
            provider,
            model,
            new ProviderCostSettings { FastMode = true });

        using var output = JsonDocument.Parse(bytes);
        Assert.Equal("priority", output.RootElement.GetProperty("service_tier").GetString());
        Assert.Equal("hello", output.RootElement.GetProperty("input").GetString());
    }

    [Fact]
    public void Build_WithModelUpstreamMapping_RewritesModelOnly()
    {
        using var document = JsonDocument.Parse("""{"model":"local-alias","input":[{"role":"user","content":"hi"}],"tools":[{"type":"web_search_preview"}]}""");
        var provider = new ProviderConfig
        {
            DefaultModel = "fallback",
            Protocol = ProviderProtocol.OpenAiResponses
        };
        var model = new ModelRouteConfig
        {
            Id = "local-alias",
            UpstreamModel = "gpt-5.5",
            Protocol = ProviderProtocol.OpenAiResponses
        };

        var bytes = ResponsesPayloadBuilder.Build(
            document.RootElement,
            provider,
            model,
            new ProviderCostSettings());

        using var output = JsonDocument.Parse(bytes);
        Assert.Equal("gpt-5.5", output.RootElement.GetProperty("model").GetString());
        Assert.Equal(JsonValueKind.Array, output.RootElement.GetProperty("input").ValueKind);
        Assert.Equal(JsonValueKind.Array, output.RootElement.GetProperty("tools").ValueKind);
    }

    [Fact]
    public void Build_WithDefaultModelConversion_RewritesToProviderDefaultRoute()
    {
        using var document = JsonDocument.Parse("""{"model":"gpt-5.5","input":"hello"}""");
        var provider = new ProviderConfig
        {
            DefaultModel = "provider-default",
            Protocol = ProviderProtocol.OpenAiResponses,
            Models =
            {
                new ModelRouteConfig
                {
                    Id = "provider-default",
                    UpstreamModel = "provider-upstream",
                    Protocol = ProviderProtocol.OpenAiResponses
                }
            },
            ModelConversions =
            {
                new ModelConversionConfig
                {
                    SourceModel = "gpt-5.5",
                    UseDefaultModel = true,
                    Enabled = true
                }
            }
        };

        var model = ProviderRoutingResolver.ResolveModel(provider, "gpt-5.5");
        var bytes = ResponsesPayloadBuilder.Build(
            document.RootElement,
            provider,
            model,
            new ProviderCostSettings());

        using var output = JsonDocument.Parse(bytes);
        Assert.Equal("provider-upstream", output.RootElement.GetProperty("model").GetString());
    }

    [Fact]
    public void Build_WithExplicitModelConversion_RewritesToTargetModel()
    {
        using var document = JsonDocument.Parse("""{"model":"codex-alias","input":"hello"}""");
        var provider = new ProviderConfig
        {
            DefaultModel = "fallback",
            Protocol = ProviderProtocol.OpenAiResponses,
            ModelConversions =
            {
                new ModelConversionConfig
                {
                    SourceModel = "codex-alias",
                    TargetModel = "explicit-upstream",
                    Enabled = true
                }
            }
        };

        var model = ProviderRoutingResolver.ResolveModel(provider, "codex-alias");
        var bytes = ResponsesPayloadBuilder.Build(
            document.RootElement,
            provider,
            model,
            new ProviderCostSettings());

        using var output = JsonDocument.Parse(bytes);
        Assert.Equal("explicit-upstream", output.RootElement.GetProperty("model").GetString());
    }
}
