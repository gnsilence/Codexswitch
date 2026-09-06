using CodexSwitch.Models;
using CodexSwitch.Services;

namespace CodexSwitch.Tests;

public sealed class PriceCalculatorTests
{
    [Fact]
    public void Calculate_AppliesProgressiveTiersAndProviderMultiplier()
    {
        var catalog = new ModelPricingCatalog
        {
            BillingUnitTokens = 1_000,
            FastMode =
            {
                DefaultMultiplier = 2m,
                ModelOverrides =
                {
                    ["gpt-5.5*"] = 2.5m
                }
            },
            Models =
            {
                new ModelPricingRule
                {
                    Id = "gpt-5.5",
                    Aliases = { "gpt-5.5*" },
                    Input =
                    {
                        Tiers =
                        {
                            new PricingTier { UpToTokens = 1_000, PricePerUnit = 1m },
                            new PricingTier { UpToTokens = null, PricePerUnit = 2m }
                        }
                    },
                    CachedInput =
                    {
                        Tiers =
                        {
                            new PricingTier { UpToTokens = null, PricePerUnit = 0.1m }
                        }
                    },
                    CacheCreationInput =
                    {
                        Tiers =
                        {
                            new PricingTier { UpToTokens = null, PricePerUnit = 0.25m }
                        }
                    },
                    Output =
                    {
                        Tiers =
                        {
                            new PricingTier { UpToTokens = null, PricePerUnit = 10m }
                        }
                    }
                }
            }
        };

        var calculator = new PriceCalculator(catalog);
        var cost = calculator.Calculate(
            "gpt-5.5-latest",
            new UsageTokens(1_500, 500, 250, 100, 0),
            new ProviderCostSettings { FastMode = true, Multiplier = 1.5m });

        Assert.Equal(1.5m, cost.Multiplier);
        Assert.Equal(2m, cost.InputCost);
        Assert.Equal(0.05m, cost.CachedInputCost);
        Assert.Equal(0.0625m, cost.CacheCreationInputCost);
        Assert.Equal(1m, cost.OutputCost);
        Assert.Equal(4.66875m, cost.Total);
    }

    [Fact]
    public void Calculate_Gpt56LongContext_UsesLongRatesForWholeRequest()
    {
        var rule = Assert.Single(
            BuiltInModelCatalog.CreatePricingRules(),
            item => item.Id == "gpt-5.6-terra");
        var catalog = new ModelPricingCatalog
        {
            BillingUnitTokens = 1_000_000,
            Models = { rule }
        };
        var calculator = new PriceCalculator(catalog);

        var cost = calculator.Calculate(
            "gpt-5.6-terra-20260906",
            new UsageTokens(150_000, 100_000, 50_000, 1_000, 0),
            new ProviderCostSettings());

        Assert.Equal(0.60m, cost.InputCost);
        Assert.Equal(0.04m, cost.CachedInputCost);
        Assert.Equal(0.25m, cost.CacheCreationInputCost);
        Assert.Equal(0.018m, cost.OutputCost);
        Assert.Equal(0.908m, cost.Total);
    }

    [Fact]
    public void Calculate_AppliesCurrentProviderBillingMultiplierToOfficialRate()
    {
        var rule = Assert.Single(
            BuiltInModelCatalog.CreatePricingRules(),
            item => item.Id == "gpt-5.4-mini");
        var calculator = new PriceCalculator(new ModelPricingCatalog
        {
            BillingUnitTokens = 1_000_000,
            Models = { rule }
        });

        var cost = calculator.Calculate(
            "gpt-5.4-mini",
            new UsageTokens(1_000_000, 0, 0, 0, 0),
            new ProviderCostSettings { Multiplier = 0.13m });

        Assert.Equal(0.13m, cost.Multiplier);
        Assert.Equal(0.75m, cost.InputCost);
        Assert.Equal(0.0975m, cost.Total);
    }

    [Fact]
    public void Calculate_UsesSeparateClaudeCacheCreationDurations()
    {
        var rule = Assert.Single(
            BuiltInModelCatalog.CreatePricingRules(),
            item => item.Id == "claude-sonnet-5");
        var calculator = new PriceCalculator(new ModelPricingCatalog
        {
            BillingUnitTokens = 1_000_000,
            Models = { rule }
        });
        var usage = new UsageTokens(0, 0, 1_000_000, 0, 0)
        {
            CacheCreationInput1HourTokens = 400_000
        };

        var cost = calculator.Calculate("claude-sonnet-5", usage, new ProviderCostSettings());

        Assert.Equal(3.10m, cost.CacheCreationInputCost);
        Assert.Equal(3.10m, cost.Total);
    }
}
