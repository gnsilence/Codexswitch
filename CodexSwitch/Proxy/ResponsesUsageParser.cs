using System.Text;
using CodexSwitch.Models;

namespace CodexSwitch.Proxy;

public static class ResponsesUsageParser
{
    public static bool TryParseResponseUsage(string json, out UsageTokens usage, out string? model)
    {
        return ResponsesUsageScanner.TryParseResponseUsage(json, out usage, out model);
    }

    public static bool TryParseResponseUsage(JsonElement root, out UsageTokens usage, out string? model)
    {
        usage = default;
        model = null;

        var response = root;
        if (root.TryGetProperty("response", out var nestedResponse) &&
            nestedResponse.ValueKind == JsonValueKind.Object)
        {
            response = nestedResponse;
        }

        model = TryGetString(response, "model");

        if (!response.TryGetProperty("usage", out var usageElement) ||
            usageElement.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var input = TryGetInt64(usageElement, "input_tokens") ?? TryGetInt64(usageElement, "prompt_tokens") ?? 0;
        var output = TryGetInt64(usageElement, "output_tokens") ?? TryGetInt64(usageElement, "completion_tokens") ?? 0;
        var cached = TryGetInt64(usageElement, "cache_read_input_tokens") ?? 0;
        var cacheCreation = TryGetInt64(usageElement, "cache_creation_input_tokens") ?? 0;
        var reasoning = 0L;

        if (usageElement.TryGetProperty("input_tokens_details", out var inputDetails) &&
            inputDetails.ValueKind == JsonValueKind.Object)
        {
            cached = TryGetInt64(inputDetails, "cached_tokens") ??
                TryGetInt64(inputDetails, "cache_read_input_tokens") ??
                0;
            cacheCreation = TryGetInt64(inputDetails, "cache_creation_input_tokens") ?? cacheCreation;
        }

        if (usageElement.TryGetProperty("output_tokens_details", out var outputDetails) &&
            outputDetails.ValueKind == JsonValueKind.Object)
        {
            reasoning = TryGetInt64(outputDetails, "reasoning_tokens") ?? 0;
        }

        if (HasOpenAiInputDetails(usageElement))
            input = Math.Max(0, input - cached - cacheCreation);

        usage = new UsageTokens(input, cached, cacheCreation, output, reasoning);
        return input > 0 || cached > 0 || cacheCreation > 0 || output > 0 || reasoning > 0;
    }

    public static bool TryParseCompletedSse(
        string? eventName,
        StringBuilder dataBuilder,
        out UsageTokens usage,
        out string? model)
    {
        usage = default;
        model = null;

        return ResponsesUsageScanner.TryParseCompletedSse(eventName, dataBuilder, out usage, out model);
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static long? TryGetInt64(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
            return null;

        return value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)
            ? number
            : null;
    }

    private static bool HasOpenAiInputDetails(JsonElement usageElement)
    {
        return usageElement.TryGetProperty("input_tokens_details", out _) ||
            usageElement.TryGetProperty("prompt_tokens_details", out _);
    }
}
