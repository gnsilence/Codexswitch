namespace CodexSwitch.Services;

public static class ModelReasoningEffortCatalog
{
    public static IReadOnlyList<string> All { get; } =
        ["none", "minimal", "low", "medium", "high", "xhigh", "max", "ultra", "persistent"];

    public static bool IsSupported(string? effort)
    {
        return All.Contains(effort, StringComparer.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<string> ResolveLegacy(string? modelId)
    {
        if (BuiltInModelCatalog.SupportsDeepSeekMaxReasoning(modelId))
            return ["none", "low", "high", "max", "ultra"];

        if (!string.IsNullOrWhiteSpace(modelId) &&
            (modelId.StartsWith("gpt-6-astra", StringComparison.OrdinalIgnoreCase) ||
             modelId.StartsWith("gpt-6.1-sol", StringComparison.OrdinalIgnoreCase) ||
             modelId.StartsWith("gpt-6-sol", StringComparison.OrdinalIgnoreCase) ||
             modelId.StartsWith("gpt-6-luna", StringComparison.OrdinalIgnoreCase) ||
             modelId.StartsWith("gpt-5.6-sol", StringComparison.OrdinalIgnoreCase) ||
             modelId.StartsWith("gpt-5.6-terra", StringComparison.OrdinalIgnoreCase)))
        {
            return ["low", "medium", "high", "xhigh", "max", "ultra"];
        }

        if (!string.IsNullOrWhiteSpace(modelId) &&
            modelId.StartsWith("gpt-5.6-luna", StringComparison.OrdinalIgnoreCase))
        {
            return ["low", "medium", "high", "xhigh", "max"];
        }

        if (!string.IsNullOrWhiteSpace(modelId) &&
            (modelId.StartsWith("gpt-5.5", StringComparison.OrdinalIgnoreCase) ||
             modelId.StartsWith("gpt-5.4", StringComparison.OrdinalIgnoreCase)))
        {
            return ["low", "medium", "high", "xhigh"];
        }

        return ["none", "low", "medium", "high", "xhigh", "max"];
    }
}
