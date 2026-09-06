using System.Text;
using CodexSwitch.Models;

namespace CodexSwitch.Proxy;

public static class ResponsesUsageScanner
{
    public static bool TryParseResponseUsage(string json, out UsageTokens usage, out string? model)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            usage = default;
            model = null;
            return false;
        }

        return TryParseResponseUsage(Encoding.UTF8.GetBytes(json), out usage, out model);
    }

    public static bool TryParseResponseUsage(ReadOnlySpan<byte> json, out UsageTokens usage, out string? model)
    {
        usage = default;
        model = null;

        try
        {
            var reader = new Utf8JsonReader(json, isFinalBlock: true, state: default);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
                return false;

            var parsed = ReadResponseObject(ref reader);
            usage = parsed.Usage;
            model = parsed.Model;
            return parsed.HasUsage && HasUsage(usage);
        }
        catch (JsonException)
        {
            usage = default;
            model = null;
            return false;
        }
    }

    public static bool TryParseCompletedSse(
        string? eventName,
        StringBuilder dataBuilder,
        out UsageTokens usage,
        out string? model)
    {
        usage = default;
        model = null;

        if (dataBuilder.Length == 0)
            return false;

        var data = dataBuilder.ToString().Trim();
        if (data.Length == 0 || string.Equals(data, "[DONE]", StringComparison.Ordinal))
            return false;

        var bytes = Encoding.UTF8.GetBytes(data);
        if (!IsCompletedEvent(eventName, bytes))
            return false;

        return TryParseResponseUsage(bytes, out usage, out model);
    }

    public static bool TryParseEventType(string message, out string? eventType)
    {
        return TryGetTopLevelString(message, "type"u8, out eventType);
    }

    public static bool TryParseEventType(ReadOnlySpan<byte> message, out string? eventType)
    {
        return TryGetTopLevelString(message, "type"u8, out eventType);
    }

    public static int? TryParseErrorStatus(string message)
    {
        try
        {
            var bytes = Encoding.UTF8.GetBytes(message);
            var reader = new Utf8JsonReader(bytes, isFinalBlock: true, state: default);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
                return null;

            var startDepth = reader.CurrentDepth;
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject && reader.CurrentDepth == startDepth)
                    break;

                if (reader.TokenType != JsonTokenType.PropertyName || reader.CurrentDepth != startDepth + 1)
                    continue;

                var isStatus = reader.ValueTextEquals("status"u8);
                ReadValue(ref reader);
                if (isStatus &&
                    reader.TokenType == JsonTokenType.Number &&
                    reader.TryGetInt32(out var value))
                {
                    return value;
                }

                SkipNestedValue(ref reader);
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    public static string? ExtractErrorMessage(string message)
    {
        try
        {
            var bytes = Encoding.UTF8.GetBytes(message);
            var reader = new Utf8JsonReader(bytes, isFinalBlock: true, state: default);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
                return message;

            string? topLevelMessage = null;
            string? errorMessage = null;
            var startDepth = reader.CurrentDepth;
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject && reader.CurrentDepth == startDepth)
                    break;

                if (reader.TokenType != JsonTokenType.PropertyName || reader.CurrentDepth != startDepth + 1)
                    continue;

                if (reader.ValueTextEquals("message"u8))
                {
                    ReadValue(ref reader);
                    if (reader.TokenType == JsonTokenType.String)
                        topLevelMessage = reader.GetString();
                    SkipNestedValue(ref reader);
                    continue;
                }

                if (reader.ValueTextEquals("error"u8))
                {
                    ReadValue(ref reader);
                    if (reader.TokenType == JsonTokenType.StartObject)
                        errorMessage = ReadErrorObjectMessage(ref reader);
                    else
                        SkipNestedValue(ref reader);
                    continue;
                }

                ReadValue(ref reader);
                SkipNestedValue(ref reader);
            }

            return errorMessage ?? topLevelMessage;
        }
        catch (JsonException)
        {
            return message;
        }
    }

    public static int ExtractOutputDeltaCharacterCount(string? eventName, string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return 0;

        var trimmed = payload.AsSpan().Trim();
        if (trimmed.SequenceEqual("[DONE]".AsSpan()))
            return 0;

        try
        {
            return ExtractOutputDeltaCharacterCount(eventName, Encoding.UTF8.GetBytes(payload));
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    public static int ExtractOutputDeltaCharacterCount(string? eventName, ReadOnlySpan<byte> payload)
    {
        var reader = new Utf8JsonReader(payload, isFinalBlock: true, state: default);
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            return 0;

        string? type = null;
        var topLevelDeltaLength = 0;
        var contentBlockDeltaLength = 0;
        var chatDeltaLength = 0;
        var startDepth = reader.CurrentDepth;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject && reader.CurrentDepth == startDepth)
                break;

            if (reader.TokenType != JsonTokenType.PropertyName || reader.CurrentDepth != startDepth + 1)
                continue;

            if (reader.ValueTextEquals("type"u8))
            {
                ReadValue(ref reader);
                if (reader.TokenType == JsonTokenType.String)
                    type = reader.GetString();
                SkipNestedValue(ref reader);
                continue;
            }

            if (reader.ValueTextEquals("delta"u8))
            {
                ReadValue(ref reader);
                if (reader.TokenType == JsonTokenType.String)
                {
                    topLevelDeltaLength = reader.GetString()?.Length ?? 0;
                }
                else if (reader.TokenType == JsonTokenType.StartObject)
                {
                    contentBlockDeltaLength = ReadContentBlockDeltaLength(ref reader);
                }
                else
                {
                    SkipNestedValue(ref reader);
                }

                continue;
            }

            if (reader.ValueTextEquals("choices"u8))
            {
                ReadValue(ref reader);
                if (reader.TokenType == JsonTokenType.StartArray)
                    chatDeltaLength = ReadChatChoicesDeltaLength(ref reader);
                else
                    SkipNestedValue(ref reader);
                continue;
            }

            ReadValue(ref reader);
            SkipNestedValue(ref reader);
        }

        var resolvedType = type ?? eventName ?? "";
        if (IsResponsesTextDelta(resolvedType))
            return topLevelDeltaLength;

        if (string.Equals(resolvedType, "content_block_delta", StringComparison.Ordinal))
            return contentBlockDeltaLength;

        if (chatDeltaLength > 0)
            return chatDeltaLength;

        return !string.IsNullOrWhiteSpace(eventName) && IsResponsesTextDelta(eventName)
            ? topLevelDeltaLength
            : 0;
    }

    private static bool IsCompletedEvent(string? eventName, ReadOnlySpan<byte> bytes)
    {
        if (string.Equals(eventName, "response.completed", StringComparison.Ordinal))
            return true;

        return TryParseEventType(bytes, out var type) &&
            string.Equals(type, "response.completed", StringComparison.Ordinal);
    }

    private static ParsedResponse ReadResponseObject(ref Utf8JsonReader reader)
    {
        var startDepth = reader.CurrentDepth;
        var parsed = new ParsedResponse();
        ParsedResponse? nestedResponse = null;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject && reader.CurrentDepth == startDepth)
                break;

            if (reader.TokenType != JsonTokenType.PropertyName || reader.CurrentDepth != startDepth + 1)
                continue;

            if (reader.ValueTextEquals("model"u8))
            {
                ReadValue(ref reader);
                if (reader.TokenType == JsonTokenType.String)
                    parsed.Model = reader.GetString();
                SkipNestedValue(ref reader);
                continue;
            }

            if (reader.ValueTextEquals("usage"u8))
            {
                ReadValue(ref reader);
                if (reader.TokenType == JsonTokenType.StartObject)
                {
                    parsed.Usage = ReadUsageObject(ref reader);
                    parsed.HasUsage = true;
                }
                else
                {
                    SkipNestedValue(ref reader);
                }

                continue;
            }

            if (reader.ValueTextEquals("response"u8))
            {
                ReadValue(ref reader);
                if (reader.TokenType == JsonTokenType.StartObject)
                    nestedResponse = ReadResponseObject(ref reader);
                else
                    SkipNestedValue(ref reader);
                continue;
            }

            ReadValue(ref reader);
            SkipNestedValue(ref reader);
        }

        return nestedResponse ?? parsed;
    }

    private static UsageTokens ReadUsageObject(ref Utf8JsonReader reader)
    {
        var startDepth = reader.CurrentDepth;
        long? inputTokens = null;
        long? promptTokens = null;
        long? outputTokens = null;
        long? completionTokens = null;
        long? cached = null;
        long? cacheCreation = null;
        long? detailCached = null;
        long? detailCacheCreation = null;
        long reasoning = 0;
        var hasOpenAiInputDetails = false;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject && reader.CurrentDepth == startDepth)
                break;

            if (reader.TokenType != JsonTokenType.PropertyName || reader.CurrentDepth != startDepth + 1)
                continue;

            if (reader.ValueTextEquals("input_tokens"u8))
            {
                ReadValue(ref reader);
                inputTokens = ReadInt64(ref reader);
                SkipNestedValue(ref reader);
                continue;
            }

            if (reader.ValueTextEquals("prompt_tokens"u8))
            {
                ReadValue(ref reader);
                promptTokens = ReadInt64(ref reader);
                SkipNestedValue(ref reader);
                continue;
            }

            if (reader.ValueTextEquals("output_tokens"u8))
            {
                ReadValue(ref reader);
                outputTokens = ReadInt64(ref reader);
                SkipNestedValue(ref reader);
                continue;
            }

            if (reader.ValueTextEquals("completion_tokens"u8))
            {
                ReadValue(ref reader);
                completionTokens = ReadInt64(ref reader);
                SkipNestedValue(ref reader);
                continue;
            }

            if (reader.ValueTextEquals("cache_read_input_tokens"u8))
            {
                ReadValue(ref reader);
                cached = ReadInt64(ref reader);
                SkipNestedValue(ref reader);
                continue;
            }

            if (reader.ValueTextEquals("cache_creation_input_tokens"u8))
            {
                ReadValue(ref reader);
                cacheCreation = ReadInt64(ref reader);
                SkipNestedValue(ref reader);
                continue;
            }

            if (reader.ValueTextEquals("input_tokens_details"u8) ||
                reader.ValueTextEquals("prompt_tokens_details"u8))
            {
                hasOpenAiInputDetails = true;
                ReadValue(ref reader);
                if (reader.TokenType == JsonTokenType.StartObject)
                {
                    var details = ReadInputDetailsObject(ref reader);
                    detailCached = details.CachedTokens;
                    detailCacheCreation = details.CacheCreationInputTokens ?? detailCacheCreation;
                }
                else
                {
                    SkipNestedValue(ref reader);
                }

                continue;
            }

            if (reader.ValueTextEquals("output_tokens_details"u8))
            {
                ReadValue(ref reader);
                if (reader.TokenType == JsonTokenType.StartObject)
                    reasoning = ReadOutputDetailsReasoningTokens(ref reader);
                else
                    SkipNestedValue(ref reader);
                continue;
            }

            ReadValue(ref reader);
            SkipNestedValue(ref reader);
        }

        var input = inputTokens ?? promptTokens ?? 0;
        var output = outputTokens ?? completionTokens ?? 0;
        var cachedInput = cached ?? 0;
        var cacheCreated = cacheCreation ?? 0;
        if (hasOpenAiInputDetails)
        {
            cachedInput = detailCached ?? 0;
            cacheCreated = detailCacheCreation ?? cacheCreated;
            input = Math.Max(0, input - cachedInput - cacheCreated);
        }

        return new UsageTokens(input, cachedInput, cacheCreated, output, reasoning);
    }

    private static InputDetails ReadInputDetailsObject(ref Utf8JsonReader reader)
    {
        var startDepth = reader.CurrentDepth;
        long? cachedTokens = null;
        long? cacheCreationInputTokens = null;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject && reader.CurrentDepth == startDepth)
                break;

            if (reader.TokenType != JsonTokenType.PropertyName || reader.CurrentDepth != startDepth + 1)
                continue;

            if (reader.ValueTextEquals("cached_tokens"u8) ||
                reader.ValueTextEquals("cache_read_input_tokens"u8))
            {
                ReadValue(ref reader);
                cachedTokens = ReadInt64(ref reader);
                SkipNestedValue(ref reader);
                continue;
            }

            if (reader.ValueTextEquals("cache_creation_input_tokens"u8))
            {
                ReadValue(ref reader);
                cacheCreationInputTokens = ReadInt64(ref reader);
                SkipNestedValue(ref reader);
                continue;
            }

            ReadValue(ref reader);
            SkipNestedValue(ref reader);
        }

        return new InputDetails(cachedTokens, cacheCreationInputTokens);
    }

    private static long ReadOutputDetailsReasoningTokens(ref Utf8JsonReader reader)
    {
        var startDepth = reader.CurrentDepth;
        var reasoning = 0L;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject && reader.CurrentDepth == startDepth)
                break;

            if (reader.TokenType != JsonTokenType.PropertyName || reader.CurrentDepth != startDepth + 1)
                continue;

            var isReasoning = reader.ValueTextEquals("reasoning_tokens"u8);
            ReadValue(ref reader);
            if (isReasoning)
                reasoning = ReadInt64(ref reader) ?? 0;
            SkipNestedValue(ref reader);
        }

        return reasoning;
    }

    private static int ReadContentBlockDeltaLength(ref Utf8JsonReader reader)
    {
        var startDepth = reader.CurrentDepth;
        string? deltaType = null;
        var textLength = 0;
        var thinkingLength = 0;
        var partialJsonLength = 0;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject && reader.CurrentDepth == startDepth)
                break;

            if (reader.TokenType != JsonTokenType.PropertyName || reader.CurrentDepth != startDepth + 1)
                continue;

            if (reader.ValueTextEquals("type"u8))
            {
                ReadValue(ref reader);
                if (reader.TokenType == JsonTokenType.String)
                    deltaType = reader.GetString();
                SkipNestedValue(ref reader);
                continue;
            }

            if (reader.ValueTextEquals("text"u8))
            {
                ReadValue(ref reader);
                textLength = ReadStringLength(ref reader);
                SkipNestedValue(ref reader);
                continue;
            }

            if (reader.ValueTextEquals("thinking"u8))
            {
                ReadValue(ref reader);
                thinkingLength = ReadStringLength(ref reader);
                SkipNestedValue(ref reader);
                continue;
            }

            if (reader.ValueTextEquals("partial_json"u8))
            {
                ReadValue(ref reader);
                partialJsonLength = ReadStringLength(ref reader);
                SkipNestedValue(ref reader);
                continue;
            }

            ReadValue(ref reader);
            SkipNestedValue(ref reader);
        }

        return deltaType switch
        {
            "text_delta" => textLength,
            "thinking_delta" => thinkingLength,
            "input_json_delta" => partialJsonLength,
            _ => textLength + thinkingLength + partialJsonLength
        };
    }

    private static int ReadChatChoicesDeltaLength(ref Utf8JsonReader reader)
    {
        var startDepth = reader.CurrentDepth;
        var total = 0;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray && reader.CurrentDepth == startDepth)
                break;

            if (reader.TokenType == JsonTokenType.StartObject)
                total += ReadChatChoiceDeltaLength(ref reader);
            else
                SkipNestedValue(ref reader);
        }

        return total;
    }

    private static int ReadChatChoiceDeltaLength(ref Utf8JsonReader reader)
    {
        var startDepth = reader.CurrentDepth;
        var total = 0;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject && reader.CurrentDepth == startDepth)
                break;

            if (reader.TokenType != JsonTokenType.PropertyName || reader.CurrentDepth != startDepth + 1)
                continue;

            var isDelta = reader.ValueTextEquals("delta"u8);
            ReadValue(ref reader);
            if (isDelta && reader.TokenType == JsonTokenType.StartObject)
                total += ReadChatDeltaObjectLength(ref reader);
            else
                SkipNestedValue(ref reader);
        }

        return total;
    }

    private static int ReadChatDeltaObjectLength(ref Utf8JsonReader reader)
    {
        var startDepth = reader.CurrentDepth;
        var total = 0;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject && reader.CurrentDepth == startDepth)
                break;

            if (reader.TokenType != JsonTokenType.PropertyName || reader.CurrentDepth != startDepth + 1)
                continue;

            if (reader.ValueTextEquals("content"u8) ||
                reader.ValueTextEquals("reasoning_content"u8))
            {
                ReadValue(ref reader);
                total += ReadStringLength(ref reader);
                SkipNestedValue(ref reader);
                continue;
            }

            if (reader.ValueTextEquals("tool_calls"u8))
            {
                ReadValue(ref reader);
                if (reader.TokenType == JsonTokenType.StartArray)
                    total += ReadToolCallsArgumentsLength(ref reader);
                else
                    SkipNestedValue(ref reader);
                continue;
            }

            ReadValue(ref reader);
            SkipNestedValue(ref reader);
        }

        return total;
    }

    private static int ReadToolCallsArgumentsLength(ref Utf8JsonReader reader)
    {
        var startDepth = reader.CurrentDepth;
        var total = 0;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray && reader.CurrentDepth == startDepth)
                break;

            if (reader.TokenType == JsonTokenType.StartObject)
                total += ReadToolCallArgumentsLength(ref reader);
            else
                SkipNestedValue(ref reader);
        }

        return total;
    }

    private static int ReadToolCallArgumentsLength(ref Utf8JsonReader reader)
    {
        var startDepth = reader.CurrentDepth;
        var total = 0;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject && reader.CurrentDepth == startDepth)
                break;

            if (reader.TokenType != JsonTokenType.PropertyName || reader.CurrentDepth != startDepth + 1)
                continue;

            var isFunction = reader.ValueTextEquals("function"u8);
            ReadValue(ref reader);
            if (isFunction && reader.TokenType == JsonTokenType.StartObject)
                total += ReadFunctionArgumentsLength(ref reader);
            else
                SkipNestedValue(ref reader);
        }

        return total;
    }

    private static int ReadFunctionArgumentsLength(ref Utf8JsonReader reader)
    {
        var startDepth = reader.CurrentDepth;
        var total = 0;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject && reader.CurrentDepth == startDepth)
                break;

            if (reader.TokenType != JsonTokenType.PropertyName || reader.CurrentDepth != startDepth + 1)
                continue;

            var isArguments = reader.ValueTextEquals("arguments"u8);
            ReadValue(ref reader);
            if (isArguments)
                total += ReadStringLength(ref reader);
            SkipNestedValue(ref reader);
        }

        return total;
    }

    private static string? ReadErrorObjectMessage(ref Utf8JsonReader reader)
    {
        var startDepth = reader.CurrentDepth;
        string? message = null;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject && reader.CurrentDepth == startDepth)
                break;

            if (reader.TokenType != JsonTokenType.PropertyName || reader.CurrentDepth != startDepth + 1)
                continue;

            var isMessage = reader.ValueTextEquals("message"u8);
            ReadValue(ref reader);
            if (isMessage && reader.TokenType == JsonTokenType.String)
                message = reader.GetString();
            SkipNestedValue(ref reader);
        }

        return message;
    }

    private static bool TryGetTopLevelString(string json, ReadOnlySpan<byte> propertyName, out string? value)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            value = null;
            return false;
        }

        return TryGetTopLevelString(Encoding.UTF8.GetBytes(json), propertyName, out value);
    }

    private static bool TryGetTopLevelString(ReadOnlySpan<byte> json, ReadOnlySpan<byte> propertyName, out string? value)
    {
        value = null;

        try
        {
            var reader = new Utf8JsonReader(json, isFinalBlock: true, state: default);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
                return false;

            var startDepth = reader.CurrentDepth;
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject && reader.CurrentDepth == startDepth)
                    break;

                if (reader.TokenType != JsonTokenType.PropertyName || reader.CurrentDepth != startDepth + 1)
                    continue;

                var isMatch = reader.ValueTextEquals(propertyName);
                ReadValue(ref reader);
                if (isMatch && reader.TokenType == JsonTokenType.String)
                {
                    value = reader.GetString();
                    return true;
                }

                SkipNestedValue(ref reader);
            }
        }
        catch (JsonException)
        {
        }

        return false;
    }

    private static void ReadValue(ref Utf8JsonReader reader)
    {
        if (!reader.Read())
            throw new JsonException("Invalid JSON payload.");
    }

    private static void SkipNestedValue(ref Utf8JsonReader reader)
    {
        if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
            reader.Skip();
    }

    private static long? ReadInt64(ref Utf8JsonReader reader)
    {
        return reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out var number)
            ? number
            : null;
    }

    private static int ReadStringLength(ref Utf8JsonReader reader)
    {
        return reader.TokenType == JsonTokenType.String
            ? reader.GetString()?.Length ?? 0
            : 0;
    }

    private static bool HasUsage(UsageTokens usage)
    {
        return usage.InputTokens > 0 ||
            usage.CachedInputTokens > 0 ||
            usage.CacheCreationInputTokens > 0 ||
            usage.OutputTokens > 0 ||
            usage.ReasoningOutputTokens > 0;
    }

    private static bool IsResponsesTextDelta(string eventType)
    {
        return string.Equals(eventType, "response.output_text.delta", StringComparison.Ordinal) ||
            string.Equals(eventType, "response.reasoning_text.delta", StringComparison.Ordinal) ||
            string.Equals(eventType, "response.reasoning_summary_text.delta", StringComparison.Ordinal) ||
            string.Equals(eventType, "response.function_call_arguments.delta", StringComparison.Ordinal);
    }

    private struct ParsedResponse
    {
        public UsageTokens Usage { get; set; }

        public string? Model { get; set; }

        public bool HasUsage { get; set; }
    }

    private readonly record struct InputDetails(long? CachedTokens, long? CacheCreationInputTokens);
}
