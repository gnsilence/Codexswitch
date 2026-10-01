using System.Net;
using System.Text;
using CodexSwitch.Models;
using Microsoft.AspNetCore.Http;

namespace CodexSwitch.Proxy;

internal static class ChatCompletionsBridge
{
    public static byte[] BuildResponsesRequest(
        ProviderRequestContext context)
    {
        var root = context.RequestRoot;
        if (root.ValueKind != JsonValueKind.Object)
            throw new ProtocolConversionException("Chat completions request body must be a JSON object.");

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            var wroteModel = false;
            var wroteStream = false;
            var wroteMaxOutputTokens = false;
            JsonElement? tools = null;
            JsonElement? toolChoice = null;

            foreach (var property in root.EnumerateObject())
            {
                switch (property.Name)
                {
                    case "model":
                        wroteModel = true;
                        writer.WriteString("model", ResolveModel(context, property.Value));
                        break;

                    case "messages":
                        writer.WritePropertyName("input");
                        WriteInputItems(writer, property.Value);
                        break;

                    case "tools":
                        tools = property.Value.Clone();
                        break;

                    case "tool_choice":
                        toolChoice = property.Value.Clone();
                        break;

                    case "stream":
                        wroteStream = true;
                        writer.WritePropertyName("stream");
                        property.Value.WriteTo(writer);
                        break;

                    case "max_tokens":
                    case "max_completion_tokens":
                        wroteMaxOutputTokens = true;
                        writer.WritePropertyName("max_output_tokens");
                        property.Value.WriteTo(writer);
                        break;

                    case "reasoning_effort":
                        if (property.Value.ValueKind == JsonValueKind.String)
                        {
                            writer.WritePropertyName("reasoning");
                            writer.WriteStartObject();
                            writer.WriteString(
                                "effort",
                                ProtocolAdapterCommon.NormalizeReasoningEffortForUpstream(
                                    context.Provider,
                                    context.Model,
                                    ResponsesPayloadBuilder.ExtractRequestModel(root),
                                    property.Value.GetString()));
                            writer.WriteEndObject();
                        }
                        break;

                    case "service_tier":
                    case "user":
                    case "metadata":
                    case "temperature":
                    case "top_p":
                    case "parallel_tool_calls":
                        property.WriteTo(writer);
                        break;

                    case "response_format":
                        writer.WritePropertyName("text");
                        writer.WriteStartObject();
                        writer.WritePropertyName("format");
                        property.Value.WriteTo(writer);
                        writer.WriteEndObject();
                        break;

                    case "n":
                    case "presence_penalty":
                    case "frequency_penalty":
                    case "logit_bias":
                    case "seed":
                    case "stream_options":
                        // No Responses equivalent is required for Codex clients.
                        break;

                    default:
                        property.WriteTo(writer);
                        break;
                }
            }

            if (!wroteModel)
                writer.WriteString("model", ResolveModel(context, default));
            if (!wroteStream)
                writer.WriteBoolean("stream", false);
            if (!wroteMaxOutputTokens &&
                root.TryGetProperty("max_completion_tokens", out var maxCompletionTokens))
            {
                writer.WritePropertyName("max_output_tokens");
                maxCompletionTokens.WriteTo(writer);
            }

            if (tools.HasValue)
            {
                writer.WritePropertyName("tools");
                WriteResponsesTools(writer, tools.Value);
            }

            if (toolChoice.HasValue)
            {
                writer.WritePropertyName("tool_choice");
                WriteResponsesToolChoice(writer, toolChoice.Value);
            }

            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    public static async Task<ProviderAdapterResult> ForwardResponsesAsync(
        ProviderRequestContext originalContext,
        byte[] responsesPayload,
        Func<ProviderRequestContext, CancellationToken, Task<ProviderAdapterResult>> forward,
        CancellationToken cancellationToken)
    {
        using var requestDocument = JsonDocument.Parse(responsesPayload);
        var bridgeHttpContext = CreateBridgeHttpContext(originalContext.HttpContext);
        using var bridgeBody = new MemoryStream();
        bridgeHttpContext.Response.Body = bridgeBody;

        var bridgeContext = new ProviderRequestContext(
            bridgeHttpContext,
            originalContext.AppConfig,
            originalContext.ClientApp,
            originalContext.Provider,
            originalContext.Model,
            originalContext.CostSettings,
            originalContext.AccessToken,
            originalContext.ProviderAuthService,
            requestDocument,
            originalContext.ResponseStateStore,
            originalContext.UsageMeter,
            originalContext.PriceCalculator,
            originalContext.UsageLogWriter);
        bridgeContext.SetRetryAttempt(
            originalContext.RetryAttempt,
            originalContext.MaxRetryAttempts);

        var result = await forward(bridgeContext, cancellationToken);
        originalContext.ResponseServiceTier = bridgeContext.ResponseServiceTier;

        if (result.Kind == ProviderAdapterResultKind.Success)
        {
            bridgeBody.Position = 0;
            var stream = ResponsesPayloadBuilder.ExtractStream(requestDocument.RootElement);
            if (stream)
                await WriteChatStreamAsync(originalContext.HttpContext, bridgeBody, cancellationToken);
            else
                await WriteChatResponseAsync(originalContext.HttpContext, bridgeBody, cancellationToken);

            return result;
        }

        if (bridgeBody.Length > 0 &&
            result.Kind != ProviderAdapterResultKind.RetryableFailureBeforeResponseStarted)
        {
            await CopyBufferedFailureAsync(originalContext.HttpContext, bridgeHttpContext, bridgeBody, cancellationToken);
            return result;
        }

        if (bridgeBody.Length > 0 &&
            originalContext.IsFinalRetryAttempt)
        {
            await CopyBufferedFailureAsync(originalContext.HttpContext, bridgeHttpContext, bridgeBody, cancellationToken);
            return ProviderAdapterResult.ResponseAlreadyStartedFailure(
                result.StatusCode,
                result.Error);
        }

        return result;
    }

    private static DefaultHttpContext CreateBridgeHttpContext(HttpContext source)
    {
        var target = new DefaultHttpContext();
        target.TraceIdentifier = source.TraceIdentifier;
        target.Request.Method = source.Request.Method;
        target.Request.Scheme = source.Request.Scheme;
        target.Request.Host = source.Request.Host;
        target.Request.Path = source.Request.Path;
        foreach (var header in source.Request.Headers)
            target.Request.Headers[header.Key] = header.Value;
        if (source.Items.TryGetValue(ProtocolAdapterCommon.OutputActivityItemKey, out var activity))
            target.Items[ProtocolAdapterCommon.OutputActivityItemKey] = activity;
        return target;
    }

    private static async Task CopyBufferedFailureAsync(
        HttpContext destination,
        HttpContext source,
        MemoryStream body,
        CancellationToken cancellationToken)
    {
        destination.Response.StatusCode = source.Response.StatusCode;
        destination.Response.ContentType = source.Response.ContentType;
        foreach (var header in source.Response.Headers)
        {
            if (!string.Equals(header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase))
                destination.Response.Headers[header.Key] = header.Value;
        }

        body.Position = 0;
        await body.CopyToAsync(destination.Response.Body, cancellationToken);
    }

    private static async Task WriteChatResponseAsync(
        HttpContext httpContext,
        Stream responseStream,
        CancellationToken cancellationToken)
    {
        using var document = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken);
        var json = BuildChatResponse(document.RootElement);
        httpContext.Response.StatusCode = StatusCodes.Status200OK;
        httpContext.Response.ContentType = "application/json";
        await httpContext.Response.WriteAsync(json, cancellationToken);
    }

    private static async Task WriteChatStreamAsync(
        HttpContext httpContext,
        Stream responseStream,
        CancellationToken cancellationToken)
    {
        httpContext.Response.StatusCode = StatusCodes.Status200OK;
        httpContext.Response.ContentType = "text/event-stream";
        using var reader = new StreamReader(responseStream, Encoding.UTF8);
        var state = new ChatStreamState();
        string? eventName = null;
        var data = new StringBuilder();

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (line.Length > 0)
            {
                if (line.StartsWith("event:", StringComparison.Ordinal))
                    eventName = line[6..].Trim();
                else if (line.StartsWith("data:", StringComparison.Ordinal))
                    data.AppendLine(line[5..].TrimStart());
                continue;
            }

            if (data.Length > 0)
            {
                await ProcessStreamEventAsync(
                    httpContext,
                    state,
                    eventName,
                    data.ToString().Trim(),
                    cancellationToken);
            }

            eventName = null;
            data.Clear();
        }

        if (data.Length > 0)
        {
            await ProcessStreamEventAsync(
                httpContext,
                state,
                eventName,
                data.ToString().Trim(),
                cancellationToken);
        }

        if (!state.Completed)
        {
            await WriteChatChunkAsync(
                httpContext,
                state,
                finishReason: state.ToolCalls.Count > 0 ? "tool_calls" : "stop",
                includeUsage: true,
                cancellationToken: cancellationToken);
        }

        await httpContext.Response.WriteAsync("data: [DONE]\n\n", cancellationToken);
        await httpContext.Response.Body.FlushAsync(cancellationToken);
    }

    private static async Task ProcessStreamEventAsync(
        HttpContext httpContext,
        ChatStreamState state,
        string? eventName,
        string data,
        CancellationToken cancellationToken)
    {
        if (string.Equals(data, "[DONE]", StringComparison.Ordinal))
            return;

        using var document = JsonDocument.Parse(data);
        var root = document.RootElement;
        var type = TryGetString(root, "type") ?? eventName;

        if (string.Equals(type, "response.output_text.delta", StringComparison.Ordinal))
        {
            await WriteChatChunkAsync(
                httpContext,
                state,
                content: TryGetString(root, "delta") ?? "",
                cancellationToken: cancellationToken);
            return;
        }

        if (string.Equals(type, "response.reasoning_text.delta", StringComparison.Ordinal) ||
            string.Equals(type, "response.reasoning_summary_text.delta", StringComparison.Ordinal))
        {
            await WriteChatChunkAsync(
                httpContext,
                state,
                reasoning: TryGetString(root, "delta") ?? "",
                cancellationToken: cancellationToken);
            return;
        }

        if (string.Equals(type, "response.output_item.added", StringComparison.Ordinal) &&
            root.TryGetProperty("item", out var item) &&
            string.Equals(TryGetString(item, "type"), "function_call", StringComparison.Ordinal))
        {
            var call = state.GetOrCreateToolCall(item);
            await WriteChatChunkAsync(
                httpContext,
                state,
                toolCall: call,
                cancellationToken: cancellationToken);
            return;
        }

        if (string.Equals(type, "response.function_call_arguments.delta", StringComparison.Ordinal))
        {
            var itemId = TryGetString(root, "item_id");
            var call = state.GetOrCreateToolCall(itemId, TryGetString(root, "call_id"));
            await WriteChatChunkAsync(
                httpContext,
                state,
                toolCall: call,
                arguments: TryGetString(root, "delta") ?? "",
                cancellationToken: cancellationToken);
            return;
        }

        if (string.Equals(type, "response.completed", StringComparison.Ordinal) ||
            string.Equals(type, "response.incomplete", StringComparison.Ordinal))
        {
            if (root.TryGetProperty("response", out var response))
            {
                state.Model = TryGetString(response, "model") ?? state.Model;
                if (response.TryGetProperty("usage", out var usage))
                    state.Usage = ParseUsage(usage);
            }

            state.Completed = true;
            await WriteChatChunkAsync(
                httpContext,
                state,
                finishReason: state.ToolCalls.Count > 0 ? "tool_calls" : "stop",
                includeUsage: true,
                cancellationToken: cancellationToken);
        }
    }

    private static string BuildChatResponse(JsonElement response)
    {
        var id = TryGetString(response, "id") ?? ProtocolAdapterCommon.CreateResponseId();
        var model = TryGetString(response, "model") ?? "";
        var reasoning = new StringBuilder();
        var text = new StringBuilder();
        var tools = new List<JsonElement>();
        if (response.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in output.EnumerateArray())
            {
                var type = TryGetString(item, "type");
                if (string.Equals(type, "reasoning", StringComparison.Ordinal))
                    reasoning.Append(ExtractReasoningText(item));
                else if (string.Equals(type, "message", StringComparison.Ordinal))
                    text.Append(ExtractMessageText(item));
                else if (string.Equals(type, "function_call", StringComparison.Ordinal))
                    tools.Add(item.Clone());
            }
        }

        var status = TryGetString(response, "status");
        var finishReason = tools.Count > 0
            ? "tool_calls"
            : string.Equals(status, "incomplete", StringComparison.Ordinal) ? "length" : "stop";
        var usage = response.TryGetProperty("usage", out var usageValue)
            ? ParseUsage(usageValue)
            : default;

        return ProtocolAdapterCommon.SerializeJson(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("id", id);
            writer.WriteString("object", "chat.completion");
            writer.WriteNumber("created", ProtocolAdapterCommon.UnixNow());
            writer.WriteString("model", model);
            writer.WritePropertyName("choices");
            writer.WriteStartArray();
            writer.WriteStartObject();
            writer.WriteNumber("index", 0);
            writer.WritePropertyName("message");
            writer.WriteStartObject();
            writer.WriteString("role", "assistant");
            writer.WriteString("content", text.Length == 0 ? null : text.ToString());
            if (reasoning.Length > 0)
                writer.WriteString("reasoning_content", reasoning.ToString());
            WriteToolCalls(writer, tools);
            writer.WriteEndObject();
            writer.WriteString("finish_reason", finishReason);
            writer.WriteEndObject();
            writer.WriteEndArray();
            WriteUsage(writer, usage);
            writer.WriteEndObject();
        });
    }

    private static async Task WriteChatChunkAsync(
        HttpContext httpContext,
        ChatStreamState state,
        string? content = null,
        string? reasoning = null,
        ChatToolCall? toolCall = null,
        string? arguments = null,
        string? finishReason = null,
        bool includeUsage = false,
        CancellationToken cancellationToken = default)
    {
        if (content is null && reasoning is null && toolCall is null &&
            arguments is null && finishReason is null && !includeUsage)
            return;

        if (toolCall is not null)
            state.ToolCalls[toolCall.ItemId] = toolCall with
            {
                Name = string.IsNullOrWhiteSpace(toolCall.Name) ? toolCall.Name : toolCall.Name
            };

        var json = ProtocolAdapterCommon.SerializeJson(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("id", state.Id);
            writer.WriteString("object", "chat.completion.chunk");
            writer.WriteNumber("created", state.Created);
            writer.WriteString("model", state.Model ?? "");
            writer.WritePropertyName("choices");
            writer.WriteStartArray();
            writer.WriteStartObject();
            writer.WriteNumber("index", 0);
            writer.WritePropertyName("delta");
            writer.WriteStartObject();
            if (state.FirstChunk)
            {
                writer.WriteString("role", "assistant");
                state.FirstChunk = false;
            }

            if (content is not null)
                writer.WriteString("content", content);
            if (reasoning is not null)
                writer.WriteString("reasoning_content", reasoning);
            if (toolCall is not null || arguments is not null)
                WriteToolDelta(writer, toolCall, arguments);
            writer.WriteEndObject();
            if (finishReason is not null)
                writer.WriteString("finish_reason", finishReason);
            else
                writer.WriteNull("finish_reason");
            writer.WriteEndObject();
            writer.WriteEndArray();
            if (includeUsage)
            {
                writer.WritePropertyName("usage");
                WriteUsageObject(writer, state.Usage);
            }
            else
            {
                writer.WriteNull("usage");
            }
            writer.WriteEndObject();
        });

        await httpContext.Response.WriteAsync("data: " + json + "\n\n", cancellationToken);
        await httpContext.Response.Body.FlushAsync(cancellationToken);
    }

    private static void WriteToolDelta(Utf8JsonWriter writer, ChatToolCall? call, string? arguments)
    {
        writer.WritePropertyName("tool_calls");
        writer.WriteStartArray();
        writer.WriteStartObject();
        writer.WriteNumber("index", call?.Index ?? 0);
        if (call is not null)
            writer.WriteString("id", call.ItemId);
        writer.WritePropertyName("function");
        writer.WriteStartObject();
        if (call is not null && !string.IsNullOrWhiteSpace(call.Name))
            writer.WriteString("name", call.Name);
        if (arguments is not null)
            writer.WriteString("arguments", arguments);
        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.WriteEndArray();
    }

    private static void WriteToolCalls(Utf8JsonWriter writer, IReadOnlyList<JsonElement> tools)
    {
        if (tools.Count == 0)
            return;

        writer.WritePropertyName("tool_calls");
        writer.WriteStartArray();
        foreach (var tool in tools)
        {
            writer.WriteStartObject();
            writer.WriteString("id", TryGetString(tool, "call_id") ?? TryGetString(tool, "id"));
            writer.WriteString("type", "function");
            writer.WritePropertyName("function");
            writer.WriteStartObject();
            writer.WriteString("name", TryGetString(tool, "name") ?? "tool");
            writer.WriteString("arguments", TryGetString(tool, "arguments") ?? "{}");
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static void WriteUsage(Utf8JsonWriter writer, UsageTokens usage)
    {
        writer.WritePropertyName("usage");
        WriteUsageObject(writer, usage);
    }

    private static void WriteUsageObject(Utf8JsonWriter writer, UsageTokens usage)
    {
        writer.WriteStartObject();
        writer.WriteNumber("prompt_tokens", usage.InputTokens + usage.CachedInputTokens);
        writer.WriteNumber("completion_tokens", usage.OutputTokens);
        writer.WriteNumber("total_tokens", usage.InputTokens + usage.CachedInputTokens + usage.OutputTokens);
        writer.WriteEndObject();
    }

    private static void WriteInputItems(Utf8JsonWriter writer, JsonElement messages)
    {
        if (messages.ValueKind != JsonValueKind.Array)
            throw new ProtocolConversionException("Chat completions request requires a messages array.");

        writer.WriteStartArray();
        foreach (var message in messages.EnumerateArray())
        {
            if (message.ValueKind != JsonValueKind.Object)
                continue;

            var role = TryGetString(message, "role") ?? "user";
            if (string.Equals(role, "tool", StringComparison.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("type", "function_call_output");
                writer.WriteString("call_id", TryGetString(message, "tool_call_id") ?? "");
                writer.WriteString("output", ExtractContentText(message));
                writer.WriteEndObject();
                continue;
            }

            if (string.Equals(role, "assistant", StringComparison.Ordinal) &&
                message.TryGetProperty("tool_calls", out var toolCalls) &&
                toolCalls.ValueKind == JsonValueKind.Array)
            {
                foreach (var toolCall in toolCalls.EnumerateArray())
                {
                    var function = toolCall.TryGetProperty("function", out var functionValue)
                        ? functionValue
                        : default;
                    writer.WriteStartObject();
                    writer.WriteString("type", "function_call");
                    writer.WriteString("call_id", TryGetString(toolCall, "id") ?? ProtocolAdapterCommon.CreateFunctionCallId());
                    writer.WriteString("name", TryGetString(function, "name") ?? "tool");
                    writer.WriteString("arguments", TryGetString(function, "arguments") ?? "{}");
                    writer.WriteEndObject();
                }
            }

            writer.WriteStartObject();
            writer.WriteString("type", "message");
            writer.WriteString("role", role is "developer" ? "system" : role);
            writer.WritePropertyName("content");
            writer.WriteStartArray();
            writer.WriteStartObject();
            writer.WriteString(
                "type",
                string.Equals(role, "assistant", StringComparison.Ordinal) ? "output_text" : "input_text");
            writer.WriteString("text", ExtractContentText(message));
            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static void WriteResponsesTools(Utf8JsonWriter writer, JsonElement tools)
    {
        writer.WriteStartArray();
        if (tools.ValueKind == JsonValueKind.Array)
        {
            foreach (var tool in tools.EnumerateArray())
            {
                var function = tool.TryGetProperty("function", out var functionValue)
                    ? functionValue
                    : tool;
                writer.WriteStartObject();
                writer.WriteString("type", "function");
                writer.WriteString("name", TryGetString(function, "name") ?? "tool");
                writer.WriteString("description", TryGetString(function, "description"));
                if (function.TryGetProperty("parameters", out var parameters))
                {
                    writer.WritePropertyName("parameters");
                    parameters.WriteTo(writer);
                }
                writer.WriteEndObject();
            }
        }
        writer.WriteEndArray();
    }

    private static void WriteResponsesToolChoice(Utf8JsonWriter writer, JsonElement choice)
    {
        if (choice.ValueKind == JsonValueKind.String)
        {
            var value = choice.GetString();
            if (value is "auto" or "none")
                writer.WriteStringValue(value);
            else
            {
                writer.WriteStartObject();
                writer.WriteString("type", "function");
                writer.WritePropertyName("name");
                writer.WriteStringValue(value);
                writer.WriteEndObject();
            }
            return;
        }

        if (choice.ValueKind == JsonValueKind.Object &&
            choice.TryGetProperty("function", out var function) &&
            function.TryGetProperty("name", out var name))
        {
            writer.WriteStartObject();
            writer.WriteString("type", "function");
            writer.WritePropertyName("name");
            name.WriteTo(writer);
            writer.WriteEndObject();
            return;
        }

        choice.WriteTo(writer);
    }

    private static string ResolveModel(ProviderRequestContext context, JsonElement requested)
    {
        var requestModel = requested.ValueKind == JsonValueKind.String
            ? requested.GetString()
            : ResponsesPayloadBuilder.ExtractRequestModel(context.RequestRoot);
        var upstream = ProtocolAdapterCommon.ResolveUpstreamModel(context.Provider, context.Model);
        return string.IsNullOrWhiteSpace(upstream)
            ? requestModel ?? context.Provider.DefaultModel
            : upstream;
    }

    private static string ExtractContentText(JsonElement message)
    {
        if (!message.TryGetProperty("content", out var content))
            return "";
        if (content.ValueKind == JsonValueKind.String)
            return content.GetString() ?? "";
        if (content.ValueKind != JsonValueKind.Array)
            return content.GetRawText();

        return string.Join(
            "",
            content.EnumerateArray()
                .Select(part => TryGetString(part, "text") ?? TryGetString(part, "input_text") ?? ""));
    }

    private static string ExtractMessageText(JsonElement item)
    {
        if (!item.TryGetProperty("content", out var content))
            return "";
        return content.ValueKind == JsonValueKind.String
            ? content.GetString() ?? ""
            : string.Join(
                "",
                content.EnumerateArray()
                    .Select(part => TryGetString(part, "text") ?? ""));
    }

    private static string ExtractReasoningText(JsonElement item)
    {
        var values = new List<string>();
        foreach (var name in new[] { "summary", "content" })
        {
            if (!item.TryGetProperty(name, out var value))
                continue;
            if (value.ValueKind == JsonValueKind.String)
                values.Add(value.GetString() ?? "");
            else if (value.ValueKind == JsonValueKind.Array)
                values.AddRange(value.EnumerateArray().Select(part => TryGetString(part, "text") ?? ""));
        }
        return string.Join("", values);
    }

    private static UsageTokens ParseUsage(JsonElement usage)
    {
        return new UsageTokens(
            TryGetInt64(usage, "input_tokens") ?? TryGetInt64(usage, "prompt_tokens") ?? 0,
            TryGetInt64(usage, "cached_input_tokens") ?? 0,
            0,
            TryGetInt64(usage, "output_tokens") ?? TryGetInt64(usage, "completion_tokens") ?? 0,
            0);
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(propertyName, out var value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static long? TryGetInt64(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(propertyName, out var value) &&
               value.ValueKind == JsonValueKind.Number &&
               value.TryGetInt64(out var result)
            ? result
            : null;
    }

    internal sealed class ProtocolConversionException(string message) : Exception(message);

    private sealed class ChatStreamState
    {
        public string Id { get; } = ProtocolAdapterCommon.CreateResponseId();
        public long Created { get; } = ProtocolAdapterCommon.UnixNow();
        public string? Model { get; set; }
        public bool FirstChunk { get; set; } = true;
        public bool Completed { get; set; }
        public UsageTokens Usage { get; set; }
        public Dictionary<string, ChatToolCall> ToolCalls { get; } = new(StringComparer.Ordinal);

        public ChatToolCall GetOrCreateToolCall(JsonElement item)
        {
            var id = TryGetString(item, "call_id") ?? TryGetString(item, "id") ?? ProtocolAdapterCommon.CreateFunctionCallId();
            var call = GetOrCreateToolCall(id, id);
            call = call with { Name = TryGetString(item, "name") ?? call.Name };
            ToolCalls[id] = call;
            return call;
        }

        public ChatToolCall GetOrCreateToolCall(string? itemId, string? callId)
        {
            var id = callId ?? itemId ?? ProtocolAdapterCommon.CreateFunctionCallId();
            if (ToolCalls.TryGetValue(id, out var existing))
                return existing;
            var created = new ChatToolCall(
                itemId ?? id,
                id,
                "",
                ToolCalls.Count);
            ToolCalls[id] = created;
            return created;
        }
    }

    private sealed record ChatToolCall(string ItemId, string CallId, string Name, int Index);
}
