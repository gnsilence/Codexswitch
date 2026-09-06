using CodexSwitch.Models;
using CodexSwitch.Services;

namespace CodexSwitch.Proxy;

public static class ResponsesPayloadBuilder
{
    public static byte[] Build(
        ResponsesRequestSnapshot snapshot,
        ProviderConfig provider,
        ModelRouteConfig? model,
        ProviderCostSettings costSettings)
    {
        return Build(snapshot, provider, model, costSettings, []);
    }

    public static byte[] Build(
        ResponsesRequestSnapshot snapshot,
        ProviderConfig provider,
        ModelRouteConfig? model,
        ProviderCostSettings costSettings,
        IEnumerable<string> extraOmitKeys)
    {
        var overrides = ShouldApplyOverrides(provider) ? provider.RequestOverrides : null;
        var extraKeys = extraOmitKeys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .ToArray();

        if (snapshot.IsObject && CanUseRawPayload(provider, model, costSettings, overrides, extraKeys))
            return snapshot.BodyBytes;

        var omitKeys = overrides?.OmitBodyKeys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        foreach (var key in extraKeys)
            omitKeys.Add(key);

        var upstreamModel = ResolveUpstreamModel(provider, model);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            var reader = new Utf8JsonReader(snapshot.Body.Span, isFinalBlock: true, state: default);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
                throw new JsonException("Responses request body must be a JSON object.");

            writer.WriteStartObject();
            var wroteModel = false;
            var wroteServiceTier = false;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject && reader.CurrentDepth == 0)
                    break;

                if (reader.TokenType != JsonTokenType.PropertyName || reader.CurrentDepth != 1)
                    continue;

                var propertyName = reader.GetString() ?? "";
                var isModel = reader.ValueTextEquals("model"u8);
                var isServiceTier = reader.ValueTextEquals("service_tier"u8);
                var isStore = reader.ValueTextEquals("store"u8);
                var isInstructions = reader.ValueTextEquals("instructions"u8);

                ReadValue(ref reader);
                var valueStart = checked((int)reader.TokenStartIndex);

                if (omitKeys.Contains(propertyName) ||
                    (isStore && overrides?.ForceStoreFalse == true) ||
                    (isInstructions && overrides?.Instructions is not null))
                {
                    SkipNestedValue(ref reader);
                    continue;
                }

                if (isModel)
                {
                    wroteModel = true;
                    SkipNestedValue(ref reader);
                    if (!string.IsNullOrWhiteSpace(upstreamModel))
                        writer.WriteString(propertyName, upstreamModel);
                    else
                        WriteRawPropertyValue(writer, snapshot, propertyName, valueStart, reader);
                    continue;
                }

                if (isServiceTier)
                {
                    wroteServiceTier = true;
                    if (ShouldForceFastTier(costSettings))
                    {
                        SkipNestedValue(ref reader);
                        writer.WriteString(propertyName, ResolveFastTier(provider, model));
                    }
                    else if (!string.IsNullOrWhiteSpace(model?.ServiceTier))
                    {
                        SkipNestedValue(ref reader);
                        writer.WriteString(propertyName, model.ServiceTier);
                    }
                    else if (!string.IsNullOrWhiteSpace(provider.ServiceTier))
                    {
                        SkipNestedValue(ref reader);
                        writer.WriteString(propertyName, provider.ServiceTier);
                    }
                    else
                    {
                        SkipNestedValue(ref reader);
                        WriteRawPropertyValue(writer, snapshot, propertyName, valueStart, reader);
                    }

                    continue;
                }

                SkipNestedValue(ref reader);
                WriteRawPropertyValue(writer, snapshot, propertyName, valueStart, reader);
            }

            if (!wroteModel && !string.IsNullOrWhiteSpace(upstreamModel))
                writer.WriteString("model", upstreamModel);

            if (!wroteServiceTier && ShouldForceFastTier(costSettings))
                writer.WriteString("service_tier", ResolveFastTier(provider, model));

            if (overrides?.ForceStoreFalse == true)
                writer.WriteBoolean("store", false);

            if (overrides?.Instructions is not null)
                writer.WriteString("instructions", overrides.Instructions);

            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    public static byte[] Build(
        JsonElement root,
        ProviderConfig provider,
        ModelRouteConfig? model,
        ProviderCostSettings costSettings)
    {
        return Build(root, provider, model, costSettings, []);
    }

    public static byte[] Build(
        JsonElement root,
        ProviderConfig provider,
        ModelRouteConfig? model,
        ProviderCostSettings costSettings,
        IEnumerable<string> extraOmitKeys)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();

            var wroteModel = false;
            var wroteServiceTier = false;
            var overrides = ShouldApplyOverrides(provider) ? provider.RequestOverrides : null;
            var omitKeys = overrides?.OmitBodyKeys
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
            foreach (var key in extraOmitKeys)
            {
                if (!string.IsNullOrWhiteSpace(key))
                    omitKeys.Add(key);
            }

            foreach (var property in root.EnumerateObject())
            {
                if (omitKeys.Contains(property.Name))
                    continue;

                if (property.NameEquals("model"))
                {
                    wroteModel = true;
                    var upstreamModel = ResolveUpstreamModel(provider, model);
                    if (!string.IsNullOrWhiteSpace(upstreamModel))
                        writer.WriteString(property.Name, upstreamModel);
                    else
                        property.WriteTo(writer);
                    continue;
                }

                if (property.NameEquals("service_tier"))
                {
                    wroteServiceTier = true;
                    WriteServiceTier(writer, property.Name, provider, model, costSettings, property.Value);
                    continue;
                }

                if (property.NameEquals("store"))
                {
                    if (overrides?.ForceStoreFalse == true)
                        continue;
                }

                if (property.NameEquals("instructions"))
                {
                    if (overrides?.Instructions is not null)
                        continue;
                }

                property.WriteTo(writer);
            }

            var fallbackModel = ResolveUpstreamModel(provider, model);
            if (!wroteModel && !string.IsNullOrWhiteSpace(fallbackModel))
                writer.WriteString("model", fallbackModel);

            if (!wroteServiceTier && ShouldForceFastTier(costSettings))
                writer.WriteString("service_tier", ResolveFastTier(provider, model));

            if (overrides?.ForceStoreFalse == true)
                writer.WriteBoolean("store", false);

            if (overrides?.Instructions is not null)
                writer.WriteString("instructions", overrides.Instructions);

            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    public static string? ExtractRequestModel(JsonElement root)
    {
        return root.TryGetProperty("model", out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    public static bool ExtractStream(JsonElement root)
    {
        return root.TryGetProperty("stream", out var value) && value.ValueKind == JsonValueKind.True;
    }

    private static void WriteServiceTier(
        Utf8JsonWriter writer,
        string propertyName,
        ProviderConfig provider,
        ModelRouteConfig? model,
        ProviderCostSettings costSettings,
        JsonElement originalValue)
    {
        if (ShouldForceFastTier(costSettings))
        {
            writer.WriteString(propertyName, ResolveFastTier(provider, model));
            return;
        }

        if (!string.IsNullOrWhiteSpace(model?.ServiceTier))
        {
            writer.WriteString(propertyName, model.ServiceTier);
            return;
        }

        if (!string.IsNullOrWhiteSpace(provider.ServiceTier))
        {
            writer.WriteString(propertyName, provider.ServiceTier);
            return;
        }

        originalValue.WriteTo(writer);
    }

    private static bool ShouldForceFastTier(ProviderCostSettings costSettings)
    {
        return costSettings.FastMode;
    }

    private static bool CanUseRawPayload(
        ProviderConfig provider,
        ModelRouteConfig? model,
        ProviderCostSettings costSettings,
        ProviderRequestOverrides? overrides,
        IReadOnlyCollection<string> extraOmitKeys)
    {
        if (!string.IsNullOrWhiteSpace(ResolveUpstreamModel(provider, model)) ||
            ShouldForceFastTier(costSettings) ||
            !string.IsNullOrWhiteSpace(model?.ServiceTier) ||
            !string.IsNullOrWhiteSpace(provider.ServiceTier) ||
            extraOmitKeys.Count > 0)
        {
            return false;
        }

        return overrides is null ||
            (!overrides.ForceStoreFalse &&
             overrides.Instructions is null &&
             overrides.OmitBodyKeys.All(string.IsNullOrWhiteSpace));
    }

    private static void WriteRawPropertyValue(
        Utf8JsonWriter writer,
        ResponsesRequestSnapshot snapshot,
        string propertyName,
        int valueStart,
        Utf8JsonReader reader)
    {
        var valueLength = checked((int)reader.BytesConsumed - valueStart);
        writer.WritePropertyName(propertyName);
        writer.WriteRawValue(snapshot.Body.Span.Slice(valueStart, valueLength), skipInputValidation: true);
    }

    private static void ReadValue(ref Utf8JsonReader reader)
    {
        if (!reader.Read())
            throw new JsonException("Invalid Responses request body.");
    }

    private static void SkipNestedValue(ref Utf8JsonReader reader)
    {
        if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
            reader.Skip();
    }

    private static string ResolveFastTier(ProviderConfig provider, ModelRouteConfig? model)
    {
        if (!string.IsNullOrWhiteSpace(model?.ServiceTier))
            return model.ServiceTier;

        return string.IsNullOrWhiteSpace(provider.ServiceTier)
            ? "priority"
            : provider.ServiceTier;
    }

    private static string ResolveUpstreamModel(ProviderConfig provider, ModelRouteConfig? model)
    {
        if (!string.IsNullOrWhiteSpace(model?.UpstreamModel))
            return model.UpstreamModel;

        if (provider.OverrideRequestModel && !string.IsNullOrWhiteSpace(provider.DefaultModel))
            return provider.DefaultModel;

        return "";
    }

    private static bool ShouldApplyOverrides(ProviderConfig provider)
    {
        if (provider.RequestOverrides is null)
            return false;

        if (string.Equals(provider.BuiltinId, ProviderTemplateCatalog.CodexOAuthBuiltinId, StringComparison.OrdinalIgnoreCase))
            return ProviderTemplateCatalog.IsChatGptCodexBackend(provider.BaseUrl);

        return true;
    }
}
