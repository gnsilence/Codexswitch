using System.Text;
using CodexSwitch.Models;
using CodexSwitch.Services;

namespace CodexSwitch.Proxy;

internal static class AnthropicMessagesToResponsesPayloadBuilder
{
    private static readonly string DefaultPromptCacheKey = "codexswitch-" + Guid.NewGuid().ToString("N");

    public static byte[] Build(ProviderRequestContext context, string requestModel)
    {
        var root = context.RequestRoot;
        if (root.ValueKind != JsonValueKind.Object)
            throw new ProtocolConversionException("Anthropic Messages request body must be a JSON object.");

        var upstreamModel = ResolveMessagesResponsesModel(context, requestModel);
        var thinkingMode = ExtractThinkingMode(root);
        JsonElement? requestedServiceTier = null;
        JsonElement? toolsValue = null;
        JsonElement? toolChoiceValue = null;
        JsonElement? outputConfigValue = null;
        string? promptCacheKey = null;
        var wroteModel = false;
        var wroteServiceTier = false;
        var wroteInstructions = false;
        var wroteInput = false;
        var wroteThinkingControl = false;
        var wroteParallelToolCalls = false;
        var wrotePromptCacheKey = false;
        var wroteTextConfig = false;

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();

            foreach (var property in root.EnumerateObject())
            {
                switch (property.Name)
                {
                    case "model":
                        wroteModel = true;
                        writer.WriteString("model", upstreamModel);
                        break;

                    case "system":
                        wroteInstructions = true;
                        writer.WritePropertyName("instructions");
                        WriteInstructions(writer, property.Value);
                        break;

                    case "messages":
                        wroteInput = true;
                        writer.WritePropertyName("input");
                        WriteInputItems(writer, property.Value, ShouldWriteReasoningContentField(context));
                        break;

                    case "max_tokens":
                        writer.WritePropertyName("max_output_tokens");
                        property.Value.WriteTo(writer);
                        break;

                    case "stop_sequences":
                        writer.WritePropertyName("stop");
                        property.Value.WriteTo(writer);
                        break;

                    case "tools":
                        toolsValue = property.Value.Clone();
                        break;

                    case "tool_choice":
                        toolChoiceValue = property.Value.Clone();
                        break;

                    case "output_config":
                        outputConfigValue = property.Value.Clone();
                        break;

                    case "metadata":
                        promptCacheKey = ExtractMetadataUserIdentifier(property.Value) ?? promptCacheKey;
                        break;

                    case "prompt_cache_key":
                        wrotePromptCacheKey = true;
                        property.WriteTo(writer);
                        break;

                    case "text":
                        wroteTextConfig = true;
                        property.WriteTo(writer);
                        break;

                    case "parallel_tool_calls":
                        wroteParallelToolCalls = true;
                        property.WriteTo(writer);
                        break;

                    case "service_tier":
                        wroteServiceTier = true;
                        requestedServiceTier = NormalizeAnthropicServiceTier(property.Value);
                        break;

                    case "thinking":
                        break;

                    case "reasoning":
                        // Claude Code sends Anthropic `thinking`; Responses providers use either
                        // OpenAI `reasoning` or a provider-specific `thinking` object written below.
                        break;

                    case "cache_control":
                    case "inference_geo":
                    case "betas":
                    case "container":
                    case "context_management":
                    case "mcp_servers":
                    case "top_k":
                        break;

                    default:
                        property.WriteTo(writer);
                        break;
                }
            }

            if (!wroteModel)
                writer.WriteString("model", upstreamModel);

            if (!wroteInstructions && root.TryGetProperty("system", out var systemValue))
            {
                writer.WritePropertyName("instructions");
                WriteInstructions(writer, systemValue);
            }

            if (!wroteInput)
            {
                if (!root.TryGetProperty("messages", out var messagesValue))
                    throw new ProtocolConversionException("Anthropic Messages request requires a messages array.");

                writer.WritePropertyName("input");
                WriteInputItems(writer, messagesValue, ShouldWriteReasoningContentField(context));
            }

            if (toolsValue.HasValue)
            {
                writer.WritePropertyName("tools");
                WriteResponsesTools(writer, toolsValue.Value);
            }

            if (toolChoiceValue.HasValue)
                WriteResponsesToolChoice(writer, toolChoiceValue.Value);

            if (toolChoiceValue.HasValue && !wroteParallelToolCalls &&
                TryGetBool(toolChoiceValue.Value, "disable_parallel_tool_use", out var disableParallelToolUse))
            {
                writer.WriteBoolean("parallel_tool_calls", !disableParallelToolUse);
            }

            if (outputConfigValue.HasValue && !wroteTextConfig)
                WriteResponsesTextConfig(writer, outputConfigValue.Value);

            if (!wrotePromptCacheKey)
                writer.WriteString("prompt_cache_key", NormalizePromptCacheKey(promptCacheKey));

            if (thinkingMode.HasValue)
            {
                WriteThinkingControl(writer, context, thinkingMode.Value);
                wroteThinkingControl = true;
            }

            if (!wroteThinkingControl && ShouldUseProviderThinkingObject(context) && context.Provider.ClaudeCode.AlwaysThinkingEnabled)
            {
                WriteThinkingControl(writer, context, ThinkingMode.EnabledDefault);
            }

            if (wroteServiceTier || context.CostSettings.FastMode ||
                !string.IsNullOrWhiteSpace(context.Model?.ServiceTier) ||
                !string.IsNullOrWhiteSpace(context.Provider.ServiceTier))
            {
                ProtocolAdapterCommon.WriteServiceTierProperty(
                    writer,
                    "service_tier",
                    context.Provider,
                    context.Model,
                    context.CostSettings,
                    requestedServiceTier);
            }

            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }

    private static string ResolveMessagesResponsesModel(ProviderRequestContext context, string requestModel)
    {
        var upstreamModel = ProtocolAdapterCommon.ResolveUpstreamModel(context.Provider, context.Model);
        if (!string.IsNullOrWhiteSpace(upstreamModel))
            return ClaudeCodeConfigWriter.StripOneMillionSuffix(upstreamModel);

        if (!string.IsNullOrWhiteSpace(requestModel))
            return ClaudeCodeConfigWriter.StripOneMillionSuffix(requestModel);

        return ClaudeCodeConfigWriter.StripOneMillionSuffix(context.Provider.DefaultModel);
    }

    private static void WriteInstructions(Utf8JsonWriter writer, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            writer.WriteStringValue(value.GetString());
            return;
        }

        if (value.ValueKind == JsonValueKind.Array && TryJoinTextParts(value, out var text))
        {
            writer.WriteStringValue(text);
            return;
        }

        writer.WriteStringValue(ConvertJsonElementToText(value));
    }

    private static void WriteInputItems(Utf8JsonWriter writer, JsonElement messagesValue, bool writeReasoningContentField)
    {
        if (messagesValue.ValueKind != JsonValueKind.Array)
            throw new ProtocolConversionException("Anthropic Messages request requires a messages array.");

        writer.WriteStartArray();
        foreach (var message in messagesValue.EnumerateArray())
            WriteMessageInputItems(writer, message, writeReasoningContentField);
        writer.WriteEndArray();
    }

    private static void WriteMessageInputItems(Utf8JsonWriter writer, JsonElement message, bool writeReasoningContentField)
    {
        if (message.ValueKind != JsonValueKind.Object)
            return;

        var role = NormalizeRole(TryGetString(message, "role"));
        if (!message.TryGetProperty("content", out var content))
        {
            WriteResponsesMessageItem(writer, role, [], null);
            return;
        }

        if (content.ValueKind == JsonValueKind.String)
        {
            WriteResponsesMessageItem(writer, role, [CreateTextContentPart(role, content.GetString() ?? string.Empty)], null);
            return;
        }

        if (content.ValueKind != JsonValueKind.Array)
        {
            WriteResponsesMessageItem(writer, role, [CreateTextContentPart(role, ConvertJsonElementToText(content))], null);
            return;
        }

        var contentParts = new List<JsonElement>();
        var reasoning = new StringBuilder();
        foreach (var part in content.EnumerateArray())
        {
            if (part.ValueKind != JsonValueKind.Object)
            {
                contentParts.Add(CreateTextContentPart(role, ConvertJsonElementToText(part)));
                continue;
            }

            var type = TryGetString(part, "type");
            switch (type)
            {
                case "thinking":
                    if (string.Equals(role, "assistant", StringComparison.Ordinal))
                    {
                        AppendWithNewline(reasoning, TryGetString(part, "thinking") ?? ExtractTextFromContentPart(part) ?? string.Empty);
                    }
                    else
                    {
                        contentParts.Add(CreateTextContentPart(role, ConvertJsonElementToText(part)));
                    }

                    break;

                case "redacted_thinking":
                    break;

                case "tool_use":
                    FlushMessageItem(writer, role, contentParts, reasoning, writeReasoningContentField);
                    WriteFunctionCallItem(writer, part);
                    break;

                case "tool_result":
                    FlushMessageItem(writer, role, contentParts, reasoning, writeReasoningContentField);
                    WriteFunctionCallOutputItem(writer, part);
                    break;

                case "image":
                    contentParts.Add(CreateImageContentPart(part, role));
                    break;

                case "document":
                    contentParts.Add(CreateDocumentContentPart(part, role));
                    break;

                case "text":
                    contentParts.Add(CreateTextContentPart(role, ExtractTextFromContentPart(part) ?? string.Empty));
                    break;

                default:
                    contentParts.Add(CreateTextContentPart(role, ExtractTextFromContentPart(part) ?? ConvertJsonElementToText(part)));
                    break;
            }
        }

        FlushMessageItem(writer, role, contentParts, reasoning, writeReasoningContentField);
    }

    private static void FlushMessageItem(
        Utf8JsonWriter writer,
        string role,
        List<JsonElement> contentParts,
        StringBuilder reasoning,
        bool writeReasoningContentField)
    {
        var reasoningText = reasoning.ToString();
        if (contentParts.Count > 0 || (writeReasoningContentField && !string.IsNullOrWhiteSpace(reasoningText)))
            WriteResponsesMessageItem(writer, role, contentParts, writeReasoningContentField ? reasoningText : null);

        contentParts.Clear();
        reasoning.Clear();
    }

    private static void WriteResponsesMessageItem(
        Utf8JsonWriter writer,
        string role,
        IReadOnlyList<JsonElement> contentParts,
        string? reasoningContent)
    {
        writer.WriteStartObject();
        writer.WriteString("type", "message");
        writer.WriteString("role", role);
        if (!string.IsNullOrWhiteSpace(reasoningContent))
            writer.WriteString("reasoning_content", reasoningContent);
        writer.WritePropertyName("content");
        writer.WriteStartArray();
        foreach (var part in contentParts)
            part.WriteTo(writer);
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static JsonElement CreateTextContentPart(string role, string text)
    {
        var type = string.Equals(role, "assistant", StringComparison.Ordinal) ? "output_text" : "input_text";
        var json = ProtocolAdapterCommon.SerializeJson(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("type", type);
            writer.WriteString("text", text);
            writer.WriteEndObject();
        });

        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static JsonElement CreateImageContentPart(JsonElement part, string role)
    {
        if (!string.Equals(role, "user", StringComparison.Ordinal) || !TryExtractAnthropicMediaUrl(part, out var url))
            return CreateTextContentPart(role, ConvertJsonElementToText(part));

        var json = ProtocolAdapterCommon.SerializeJson(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("type", "input_image");
            writer.WriteString("image_url", url);
            writer.WriteEndObject();
        });

        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static JsonElement CreateDocumentContentPart(JsonElement part, string role)
    {
        if (!string.Equals(role, "user", StringComparison.Ordinal) || !part.TryGetProperty("source", out var source))
            return CreateTextContentPart(role, ConvertJsonElementToText(part));

        var sourceType = TryGetString(source, "type");
        var json = ProtocolAdapterCommon.SerializeJson(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("type", "input_file");
            if (string.Equals(sourceType, "url", StringComparison.Ordinal))
            {
                writer.WriteString("file_url", TryGetString(source, "url") ?? string.Empty);
            }
            else if (string.Equals(sourceType, "base64", StringComparison.Ordinal))
            {
                var mediaType = TryGetString(source, "media_type") ?? "application/pdf";
                writer.WriteString("filename", TryGetString(part, "title") ?? "document");
                writer.WriteString("file_data", $"data:{mediaType};base64,{TryGetString(source, "data") ?? string.Empty}");
            }
            else
            {
                writer.WriteString("file_data", ConvertJsonElementToText(part));
            }

            writer.WriteEndObject();
        });

        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static void WriteFunctionCallItem(Utf8JsonWriter writer, JsonElement toolUse)
    {
        var callId = TryGetString(toolUse, "id") ?? ProtocolAdapterCommon.CreateFunctionCallId();
        writer.WriteStartObject();
        writer.WriteString("type", "function_call");
        writer.WriteString("id", "fc_" + callId);
        writer.WriteString("call_id", callId);
        writer.WriteString("name", TryGetString(toolUse, "name") ?? "tool");
        writer.WriteString("arguments", toolUse.TryGetProperty("input", out var input) ? input.GetRawText() : "{}");
        writer.WriteEndObject();
    }

    private static void WriteFunctionCallOutputItem(Utf8JsonWriter writer, JsonElement toolResult)
    {
        writer.WriteStartObject();
        writer.WriteString("type", "function_call_output");
        writer.WriteString(
            "call_id",
            TryGetString(toolResult, "tool_use_id") ??
            TryGetString(toolResult, "tool_call_id") ??
            TryGetString(toolResult, "id") ??
            ProtocolAdapterCommon.CreateFunctionCallId());
        writer.WritePropertyName("output");
        WriteFunctionCallOutputValue(writer, toolResult);
        writer.WriteEndObject();
    }

    private static void WriteFunctionCallOutputValue(Utf8JsonWriter writer, JsonElement toolResult)
    {
        if (!toolResult.TryGetProperty("content", out var content))
        {
            writer.WriteStringValue(string.Empty);
            return;
        }

        switch (content.ValueKind)
        {
            case JsonValueKind.String:
                writer.WriteStringValue(content.GetString() ?? string.Empty);
                return;

            case JsonValueKind.Array:
                if (content.GetArrayLength() == 0)
                {
                    writer.WriteStringValue(string.Empty);
                    return;
                }

                if (TryJoinTextParts(content, out var text))
                {
                    writer.WriteStringValue(text);
                    return;
                }

                writer.WriteStartArray();
                foreach (var part in content.EnumerateArray())
                    WriteFunctionCallOutputContentPart(writer, part);
                writer.WriteEndArray();
                return;

            case JsonValueKind.Null:
                writer.WriteStringValue(string.Empty);
                return;

            default:
                writer.WriteStringValue(ConvertJsonElementToText(content));
                return;
        }
    }

    private static void WriteFunctionCallOutputContentPart(Utf8JsonWriter writer, JsonElement part)
    {
        if (part.ValueKind == JsonValueKind.String)
        {
            WriteFunctionCallOutputTextPart(writer, part.GetString() ?? string.Empty);
            return;
        }

        if (part.ValueKind != JsonValueKind.Object)
        {
            WriteFunctionCallOutputTextPart(writer, ConvertJsonElementToText(part));
            return;
        }

        switch (TryGetString(part, "type"))
        {
            case "text":
                WriteFunctionCallOutputTextPart(writer, ExtractTextFromContentPart(part) ?? string.Empty);
                return;

            case "image":
                if (TryWriteFunctionCallOutputImagePart(writer, part))
                    return;
                break;

            case "document":
                if (TryWriteFunctionCallOutputDocumentPart(writer, part))
                    return;
                break;
        }

        WriteFunctionCallOutputTextPart(writer, ExtractTextFromContentPart(part) ?? ConvertJsonElementToText(part));
    }

    private static void WriteFunctionCallOutputTextPart(Utf8JsonWriter writer, string text)
    {
        writer.WriteStartObject();
        writer.WriteString("type", "input_text");
        writer.WriteString("text", text);
        writer.WriteEndObject();
    }

    private static bool TryWriteFunctionCallOutputImagePart(Utf8JsonWriter writer, JsonElement part)
    {
        if (!TryExtractAnthropicMediaUrl(part, out var url))
            return false;

        writer.WriteStartObject();
        writer.WriteString("type", "input_image");
        writer.WriteString("image_url", url);
        writer.WriteEndObject();
        return true;
    }

    private static bool TryWriteFunctionCallOutputDocumentPart(Utf8JsonWriter writer, JsonElement part)
    {
        if (!part.TryGetProperty("source", out var source) || source.ValueKind != JsonValueKind.Object)
            return false;

        var sourceType = TryGetString(source, "type");
        if (!string.Equals(sourceType, "url", StringComparison.Ordinal) &&
            !string.Equals(sourceType, "base64", StringComparison.Ordinal))
        {
            return false;
        }

        writer.WriteStartObject();
        writer.WriteString("type", "input_file");
        if (string.Equals(sourceType, "url", StringComparison.Ordinal))
        {
            writer.WriteString("file_url", TryGetString(source, "url") ?? string.Empty);
        }
        else if (string.Equals(sourceType, "base64", StringComparison.Ordinal))
        {
            var mediaType = TryGetString(source, "media_type") ?? "application/octet-stream";
            writer.WriteString("filename", TryGetString(part, "title") ?? "document");
            writer.WriteString("file_data", $"data:{mediaType};base64,{TryGetString(source, "data") ?? string.Empty}");
        }

        writer.WriteEndObject();
        return true;
    }

    private static void WriteResponsesTools(Utf8JsonWriter writer, JsonElement toolsValue)
    {
        writer.WriteStartArray();
        if (toolsValue.ValueKind == JsonValueKind.Array)
        {
            foreach (var tool in toolsValue.EnumerateArray())
            {
                if (tool.ValueKind != JsonValueKind.Object)
                    continue;

                var toolType = TryGetString(tool, "type");
                if (!IsFunctionLikeToolType(toolType) && !LooksLikeImplicitFunctionTool(tool))
                    continue;

                var toolBody = ResolveFunctionToolBody(tool);
                var name = TryGetString(toolBody, "name") ?? TryGetString(tool, "name");
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                writer.WriteStartObject();
                writer.WriteString("type", "function");
                writer.WriteString("name", name);
                if (TryGetToolProperty(toolBody, tool, "description", out var description) &&
                    description.ValueKind == JsonValueKind.String)
                {
                    writer.WriteString("description", description.GetString());
                }

                var strict = TryGetToolProperty(toolBody, tool, "strict", out var strictValue) &&
                    strictValue.ValueKind == JsonValueKind.True;
                writer.WriteBoolean("strict", strict);

                writer.WritePropertyName("parameters");
                if (TryGetToolSchema(toolBody, tool, out var parameters))
                {
                    if (strict)
                        WriteStrictJsonSchema(writer, parameters);
                    else
                        parameters.WriteTo(writer);
                }
                else
                {
                    WriteEmptyParameters(writer, strict);
                }

                writer.WriteEndObject();
            }
        }

        writer.WriteEndArray();
    }

    private static bool IsFunctionLikeToolType(string? toolType)
    {
        return string.IsNullOrWhiteSpace(toolType) ||
            string.Equals(toolType, "function", StringComparison.Ordinal) ||
            string.Equals(toolType, "tool", StringComparison.Ordinal) ||
            string.Equals(toolType, "custom", StringComparison.Ordinal);
    }

    private static bool LooksLikeImplicitFunctionTool(JsonElement tool)
    {
        return tool.ValueKind == JsonValueKind.Object &&
            (tool.TryGetProperty("name", out _) || tool.TryGetProperty("function", out _)) &&
            (tool.TryGetProperty("parameters", out _) || tool.TryGetProperty("input_schema", out _));
    }

    private static JsonElement ResolveFunctionToolBody(JsonElement tool)
    {
        return tool.TryGetProperty("function", out var functionValue) && functionValue.ValueKind == JsonValueKind.Object
            ? functionValue
            : tool;
    }

    private static bool TryGetToolProperty(
        JsonElement preferred,
        JsonElement fallback,
        string propertyName,
        out JsonElement value)
    {
        if (preferred.ValueKind == JsonValueKind.Object && preferred.TryGetProperty(propertyName, out value))
            return true;

        if (fallback.ValueKind == JsonValueKind.Object && fallback.TryGetProperty(propertyName, out value))
            return true;

        value = default;
        return false;
    }

    private static bool TryGetToolSchema(JsonElement preferred, JsonElement fallback, out JsonElement schema)
    {
        if (TryGetToolProperty(preferred, fallback, "parameters", out schema))
            return true;

        if (TryGetToolProperty(preferred, fallback, "input_schema", out schema))
            return true;

        schema = default;
        return false;
    }

    private static void WriteStrictJsonSchema(Utf8JsonWriter writer, JsonElement schema)
    {
        if (schema.ValueKind != JsonValueKind.Object)
        {
            schema.WriteTo(writer);
            return;
        }

        writer.WriteStartObject();

        var propertyNames = new List<string>();
        JsonElement? originalRequired = null;
        var hasRequired = false;
        var hasAdditionalProperties = false;

        foreach (var property in schema.EnumerateObject())
        {
            if (property.NameEquals("properties") && property.Value.ValueKind == JsonValueKind.Object)
            {
                writer.WritePropertyName("properties");
                writer.WriteStartObject();
                foreach (var nestedProperty in property.Value.EnumerateObject())
                {
                    propertyNames.Add(nestedProperty.Name);
                    writer.WritePropertyName(nestedProperty.Name);
                    WriteStrictJsonSchema(writer, nestedProperty.Value);
                }

                writer.WriteEndObject();
                continue;
            }

            if (property.NameEquals("required"))
            {
                hasRequired = true;
                originalRequired = property.Value.Clone();
                continue;
            }

            if (property.NameEquals("additionalProperties"))
            {
                hasAdditionalProperties = true;
                property.WriteTo(writer);
                continue;
            }

            if ((property.NameEquals("anyOf") ||
                 property.NameEquals("allOf") ||
                 property.NameEquals("oneOf")) &&
                property.Value.ValueKind == JsonValueKind.Array)
            {
                writer.WritePropertyName(property.Name);
                writer.WriteStartArray();
                foreach (var child in property.Value.EnumerateArray())
                    WriteStrictJsonSchema(writer, child);
                writer.WriteEndArray();
                continue;
            }

            if (property.NameEquals("items") && property.Value.ValueKind == JsonValueKind.Object)
            {
                writer.WritePropertyName("items");
                WriteStrictJsonSchema(writer, property.Value);
                continue;
            }

            property.WriteTo(writer);
        }

        if (propertyNames.Count > 0)
        {
            writer.WritePropertyName("required");
            writer.WriteStartArray();
            foreach (var name in propertyNames)
                writer.WriteStringValue(name);
            writer.WriteEndArray();
        }
        else if (hasRequired && originalRequired.HasValue)
        {
            writer.WritePropertyName("required");
            originalRequired.Value.WriteTo(writer);
        }

        if (!hasAdditionalProperties &&
            schema.TryGetProperty("type", out var typeValue) &&
            typeValue.ValueKind == JsonValueKind.String &&
            string.Equals(typeValue.GetString(), "object", StringComparison.Ordinal))
        {
            writer.WriteBoolean("additionalProperties", false);
        }

        writer.WriteEndObject();
    }

    private static void WriteEmptyParameters(Utf8JsonWriter writer, bool strict)
    {
        writer.WriteStartObject();
        writer.WriteString("type", "object");
        writer.WritePropertyName("properties");
        writer.WriteStartObject();
        writer.WriteEndObject();
        writer.WritePropertyName("required");
        writer.WriteStartArray();
        writer.WriteEndArray();
        if (strict)
            writer.WriteBoolean("additionalProperties", false);
        writer.WriteEndObject();
    }

    private static void WriteResponsesToolChoice(Utf8JsonWriter writer, JsonElement toolChoice)
    {
        writer.WritePropertyName("tool_choice");
        if (toolChoice.ValueKind == JsonValueKind.String)
        {
            WriteToolChoiceString(writer, toolChoice.GetString());
            return;
        }

        if (toolChoice.ValueKind != JsonValueKind.Object)
        {
            writer.WriteStringValue("auto");
            return;
        }

        var type = TryGetString(toolChoice, "type");
        if (string.Equals(type, "any", StringComparison.Ordinal) ||
            string.Equals(type, "required", StringComparison.Ordinal))
        {
            writer.WriteStringValue("required");
            return;
        }

        if (string.Equals(type, "tool", StringComparison.Ordinal) ||
            string.Equals(type, "function", StringComparison.Ordinal))
        {
            var name = TryGetString(toolChoice, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                writer.WriteStringValue("auto");
                return;
            }

            writer.WriteStartObject();
            writer.WriteString("type", "function");
            writer.WriteString("name", name);
            writer.WriteEndObject();
            return;
        }

        WriteToolChoiceString(writer, type);
    }

    private static void WriteToolChoiceString(Utf8JsonWriter writer, string? value)
    {
        if (string.Equals(value, "any", StringComparison.Ordinal))
        {
            writer.WriteStringValue("required");
            return;
        }

        if (string.Equals(value, "auto", StringComparison.Ordinal) ||
            string.Equals(value, "required", StringComparison.Ordinal) ||
            string.Equals(value, "none", StringComparison.Ordinal))
        {
            writer.WriteStringValue(value);
            return;
        }

        writer.WriteStringValue("auto");
    }

    private static void WriteThinkingControl(Utf8JsonWriter writer, ProviderRequestContext context, ThinkingMode mode)
    {
        if (ShouldUseProviderThinkingObject(context))
        {
            writer.WritePropertyName("thinking");
            writer.WriteStartObject();
            writer.WriteString("type", mode.Enabled ? "enabled" : "disabled");
            writer.WriteEndObject();
            return;
        }

        if (!mode.Enabled)
            return;

        writer.WritePropertyName("reasoning");
        writer.WriteStartObject();
        writer.WriteString("effort", mode.Effort);
        writer.WriteEndObject();
    }

    private static void WriteResponsesTextConfig(Utf8JsonWriter writer, JsonElement outputConfig)
    {
        if (outputConfig.ValueKind != JsonValueKind.Object ||
            !TryGetProperty(outputConfig, "format", "format_", out var format) ||
            format.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        writer.WritePropertyName("text");
        writer.WriteStartObject();
        writer.WritePropertyName("format");
        WriteResponsesTextFormat(writer, format);
        writer.WriteEndObject();
    }

    private static void WriteResponsesTextFormat(Utf8JsonWriter writer, JsonElement format)
    {
        var type = TryGetString(format, "type");
        if (!string.Equals(type, "json_schema", StringComparison.Ordinal))
        {
            format.WriteTo(writer);
            return;
        }

        writer.WriteStartObject();
        writer.WriteString("type", "json_schema");
        writer.WriteString("name", ResolveResponseFormatName(format));
        if (format.TryGetProperty("description", out var description) &&
            description.ValueKind == JsonValueKind.String)
        {
            writer.WriteString("description", description.GetString());
        }

        writer.WritePropertyName("schema");
        if (format.TryGetProperty("schema", out var schema))
            schema.WriteTo(writer);
        else
            WriteEmptyParameters(writer, strict: true);

        if (TryGetBool(format, "strict", out var strict))
            writer.WriteBoolean("strict", strict);

        writer.WriteEndObject();
    }

    private static string ResolveResponseFormatName(JsonElement format)
    {
        var name = TryGetString(format, "name");
        if (!string.IsNullOrWhiteSpace(name))
            return SanitizeResponseFormatName(name);

        if (format.TryGetProperty("schema", out var schema) &&
            schema.ValueKind == JsonValueKind.Object)
        {
            var title = TryGetString(schema, "title");
            if (!string.IsNullOrWhiteSpace(title))
                return SanitizeResponseFormatName(title);
        }

        return "response";
    }

    private static string SanitizeResponseFormatName(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsAsciiLetterOrDigit(ch) || ch == '_' || ch == '-')
                builder.Append(ch);
            else if (char.IsWhiteSpace(ch))
                builder.Append('_');
        }

        if (builder.Length == 0)
            return "response";

        return builder.Length <= 64 ? builder.ToString() : builder.ToString(0, 64);
    }

    private static ThinkingMode? ExtractThinkingMode(JsonElement root)
    {
        if (root.TryGetProperty("thinking", out var thinking) && thinking.ValueKind == JsonValueKind.Object)
        {
            var type = TryGetString(thinking, "type");
            var enabled = !string.Equals(type, "disabled", StringComparison.OrdinalIgnoreCase);
            return new ThinkingMode(enabled, MapBudgetToEffort(TryGetInt64(thinking, "budget_tokens")));
        }

        if (root.TryGetProperty("reasoning", out var reasoning) && reasoning.ValueKind == JsonValueKind.Object)
        {
            var effort = TryGetString(reasoning, "effort");
            if (string.Equals(effort, "none", StringComparison.OrdinalIgnoreCase))
                return ThinkingMode.Disabled;

            return string.IsNullOrWhiteSpace(effort)
                ? ThinkingMode.EnabledDefault
                : new ThinkingMode(true, effort);
        }

        return null;
    }

    private static string MapBudgetToEffort(long? budgetTokens)
    {
        return budgetTokens switch
        {
            <= 1024 => "low",
            <= 4096 => "medium",
            <= 8192 => "high",
            > 8192 => "xhigh",
            _ => "medium"
        };
    }

    private static bool ShouldUseProviderThinkingObject(ProviderRequestContext context)
    {
        return ContainsDeepSeek(context.Model?.Id) ||
            ContainsDeepSeek(context.Model?.UpstreamModel) ||
            ContainsDeepSeek(context.Provider.DefaultModel) ||
            ContainsDeepSeek(context.Provider.BuiltinId) ||
            ContainsDeepSeek(context.Provider.BaseUrl) ||
            ContainsDeepSeek(context.Provider.DisplayName);
    }

    private static bool ShouldWriteReasoningContentField(ProviderRequestContext context)
    {
        return ShouldUseProviderThinkingObject(context);
    }

    private static bool ContainsDeepSeek(string? value)
    {
        return value?.IndexOf("deepseek", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string NormalizeRole(string? role)
    {
        return role switch
        {
            "assistant" => "assistant",
            "system" => "system",
            "developer" => "developer",
            _ => "user"
        };
    }

    private static void AppendWithNewline(StringBuilder builder, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        if (builder.Length > 0)
            builder.Append('\n');
        builder.Append(value);
    }

    private static bool TryExtractAnthropicMediaUrl(JsonElement part, out string url)
    {
        url = string.Empty;
        if (!part.TryGetProperty("source", out var source) || source.ValueKind != JsonValueKind.Object)
            return false;

        var sourceType = TryGetString(source, "type");
        if (string.Equals(sourceType, "url", StringComparison.Ordinal))
        {
            url = TryGetString(source, "url") ?? string.Empty;
            return !string.IsNullOrWhiteSpace(url);
        }

        if (string.Equals(sourceType, "base64", StringComparison.Ordinal))
        {
            var mediaType = TryGetString(source, "media_type") ?? "application/octet-stream";
            var data = TryGetString(source, "data") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(data))
                return false;

            url = $"data:{mediaType};base64,{data}";
            return true;
        }

        return false;
    }

    private static bool TryJoinTextParts(JsonElement contentArray, out string text)
    {
        var builder = new StringBuilder();
        foreach (var part in contentArray.EnumerateArray())
        {
            var partText = part.ValueKind == JsonValueKind.String
                ? part.GetString()
                : ExtractTextFromContentPart(part);
            if (partText is null)
            {
                text = string.Empty;
                return false;
            }

            if (builder.Length > 0)
                builder.Append('\n');
            builder.Append(partText);
        }

        text = builder.ToString();
        return true;
    }

    private static string ConvertJsonElementToText(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Null => string.Empty,
            JsonValueKind.Array => TryJoinTextParts(value, out var text) ? text : value.GetRawText(),
            _ => ExtractTextFromContentPart(value) ?? value.GetRawText()
        };
    }

    private static string? ExtractTextFromContentPart(JsonElement part)
    {
        if (part.ValueKind == JsonValueKind.String)
            return part.GetString();

        if (part.ValueKind == JsonValueKind.Object &&
            part.TryGetProperty("text", out var textValue) &&
            textValue.ValueKind == JsonValueKind.String)
        {
            return textValue.GetString();
        }

        return null;
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(propertyName, out var value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static bool TryGetBool(JsonElement element, string propertyName, out bool value)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out var propertyValue) &&
            (propertyValue.ValueKind == JsonValueKind.True || propertyValue.ValueKind == JsonValueKind.False))
        {
            value = propertyValue.GetBoolean();
            return true;
        }

        value = false;
        return false;
    }

    private static bool TryGetProperty(
        JsonElement element,
        string primaryName,
        string fallbackName,
        out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(primaryName, out value))
            return true;

        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(fallbackName, out value))
            return true;

        value = default;
        return false;
    }

    private static string? ExtractMetadataUserIdentifier(JsonElement metadata)
    {
        if (metadata.ValueKind != JsonValueKind.Object)
            return null;

        return TryGetString(metadata, "user_id") ??
            TryGetString(metadata, "uerd_id") ??
            TryGetString(metadata, "userId");
    }

    private static string NormalizePromptCacheKey(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? DefaultPromptCacheKey
            : value;
    }

    private static JsonElement NormalizeAnthropicServiceTier(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String)
            return value.Clone();

        var mapped = value.GetString() switch
        {
            "standard_only" => "default",
            "standard" => "default",
            _ => value.GetString()
        };

        return CreateStringElement(mapped ?? string.Empty);
    }

    private static JsonElement CreateStringElement(string value)
    {
        var json = ProtocolAdapterCommon.SerializeJson(writer => writer.WriteStringValue(value));
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static long? TryGetInt64(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(propertyName, out var value) &&
               value.ValueKind == JsonValueKind.Number &&
               value.TryGetInt64(out var number)
            ? number
            : null;
    }

    private readonly record struct ThinkingMode(bool Enabled, string Effort)
    {
        public static ThinkingMode EnabledDefault { get; } = new(true, "medium");

        public static ThinkingMode Disabled { get; } = new(false, "none");
    }
}
