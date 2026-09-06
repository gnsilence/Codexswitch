namespace CodexSwitch.Services;

public sealed class PriceCalculator
{
    private readonly ModelPricingCatalog _catalog;

    public PriceCalculator(ModelPricingCatalog catalog)
    {
        _catalog = catalog;
    }

    public CostBreakdown Calculate(string model, UsageTokens usage, ProviderCostSettings settings)
    {
        var rule = FindRule(model);
        if (rule is null)
            return new CostBreakdown(0m, 0m, 0m, settings.Multiplier);

        var multiplier = settings.Multiplier;
        var cacheCreationInput1HourTokens = Math.Min(
            usage.CacheCreationInputTokens,
            Math.Max(0, usage.CacheCreationInput1HourTokens));
        var cacheCreationInput5MinuteTokens = Math.Max(
            0,
            usage.CacheCreationInputTokens - cacheCreationInput1HourTokens);
        var cacheCreationInput1HourTable = rule.CacheCreationInput1Hour.Tiers.Count > 0
            ? rule.CacheCreationInput1Hour
            : rule.CacheCreationInput;

        if (rule.ContextPricingThresholdTokens is long threshold)
        {
            var totalInput = usage.InputTokens + usage.CachedInputTokens + usage.CacheCreationInputTokens;
            var tierIndex = totalInput > threshold ? 1 : 0;
            return new CostBreakdown(
                CalculateContextCost(usage.InputTokens, rule.Input, tierIndex),
                CalculateContextCost(usage.CachedInputTokens, rule.CachedInput, tierIndex),
                CalculateContextCost(cacheCreationInput5MinuteTokens, rule.CacheCreationInput, tierIndex) +
                    CalculateContextCost(cacheCreationInput1HourTokens, cacheCreationInput1HourTable, tierIndex),
                CalculateContextCost(usage.OutputTokens, rule.Output, tierIndex),
                multiplier);
        }

        return new CostBreakdown(
            CalculateTieredCost(usage.InputTokens, rule.Input),
            CalculateTieredCost(usage.CachedInputTokens, rule.CachedInput),
            CalculateTieredCost(cacheCreationInput5MinuteTokens, rule.CacheCreationInput) +
                CalculateTieredCost(cacheCreationInput1HourTokens, cacheCreationInput1HourTable),
            CalculateTieredCost(usage.OutputTokens, rule.Output),
            multiplier);
    }

    private ModelPricingRule? FindRule(string model)
    {
        ModelPricingRule? bestMatch = null;
        var bestSpecificity = -1;
        foreach (var rule in _catalog.Models)
        {
            SelectBestMatch(rule, rule.Id, model, ref bestMatch, ref bestSpecificity);

            foreach (var alias in rule.Aliases)
                SelectBestMatch(rule, alias, model, ref bestMatch, ref bestSpecificity);
        }

        return bestMatch;
    }

    private static void SelectBestMatch(
        ModelPricingRule rule,
        string pattern,
        string model,
        ref ModelPricingRule? bestMatch,
        ref int bestSpecificity)
    {
        if (!Matches(pattern, model))
            return;

        var specificity = pattern.EndsWith("*", StringComparison.Ordinal)
            ? pattern.Length - 1
            : int.MaxValue;
        if (specificity <= bestSpecificity)
            return;

        bestMatch = rule;
        bestSpecificity = specificity;
    }

    private decimal CalculateTieredCost(long tokens, TokenPriceTable table)
    {
        if (tokens <= 0 || table.Tiers.Count == 0)
            return 0m;

        var remaining = tokens;
        var consumedUpperBound = 0L;
        var cost = 0m;

        foreach (var tier in table.Tiers)
        {
            var tierLimit = tier.UpToTokens;
            var tierCapacity = tierLimit is null
                ? remaining
                : Math.Max(0, tierLimit.Value - consumedUpperBound);
            var tierTokens = Math.Min(remaining, tierCapacity);

            if (tierTokens > 0)
            {
                cost += tierTokens / (decimal)_catalog.BillingUnitTokens * tier.PricePerUnit;
                remaining -= tierTokens;
            }

            if (tierLimit is not null)
                consumedUpperBound = tierLimit.Value;

            if (remaining <= 0)
                break;
        }

        return cost;
    }

    private decimal CalculateContextCost(long tokens, TokenPriceTable table, int tierIndex)
    {
        if (tokens <= 0 || table.Tiers.Count == 0)
            return 0m;

        var resolvedTier = table.Tiers[Math.Min(tierIndex, table.Tiers.Count - 1)];
        return tokens / (decimal)_catalog.BillingUnitTokens * resolvedTier.PricePerUnit;
    }

    private static bool Matches(string pattern, string model)
    {
        return ModelPatternMatcher.Matches(pattern, model);
    }
}
