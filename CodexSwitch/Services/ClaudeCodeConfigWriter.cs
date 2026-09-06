using System.Text.Json.Nodes;

namespace CodexSwitch.Services;

public sealed class ClaudeCodeConfigWriter
{
    private const string DefaultInboundApiKey = "sk-codex";

    private readonly AppPaths _paths;

    public ClaudeCodeConfigWriter(AppPaths paths)
    {
        _paths = paths;
    }

    public void Apply(AppConfig config)
    {
        // Claude Code integration is opt-in. When it is disabled we never write ~/.claude/settings.json,
        // and we only restore an original the user had if CodexSwitch previously managed the file.
        if (!config.Proxy.ManageClaudeCodeConfig)
        {
            RestoreOriginal();
            return;
        }

        var provider = ResolveClaudeCodeProvider(config);
        if (provider is null)
        {
            RestoreOriginal();
            return;
        }

        Directory.CreateDirectory(_paths.ClaudeDirectory);

        var existing = File.Exists(_paths.ClaudeSettingsPath)
            ? File.ReadAllText(_paths.ClaudeSettingsPath)
            : "";
        var settings = ParseObject(existing);
        var env = settings["env"] as JsonObject ?? new JsonObject();
        settings["env"] = env;

        env["ANTHROPIC_BASE_URL"] = BuildClientEndpoint(config.Proxy);
        env["ANTHROPIC_AUTH_TOKEN"] = string.IsNullOrWhiteSpace(config.Proxy.InboundApiKey)
            ? DefaultInboundApiKey
            : config.Proxy.InboundApiKey.Trim();

        var model = ResolveSettingsModel(provider);
        settings["model"] = model;
        settings["availableModels"] = CreateAvailableModels(provider);
        settings["alwaysThinkingEnabled"] = provider.ClaudeCode.AlwaysThinkingEnabled;
        settings["skipDangerousModePermissionPrompt"] = provider.ClaudeCode.SkipDangerousModePermissionPrompt;

        WriteTextIfChanged(_paths.ClaudeSettingsPath, settings.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true
        }) + Environment.NewLine, existing);
    }

    public void RestoreOriginal()
    {
        // Restores the user's captured original, or removes a settings file CodexSwitch generated.
        // A file we never managed is left untouched (see ManagedFileBackup.RestoreOriginal).
        ManagedFileBackup.RestoreOriginal(_paths.ClaudeSettingsPath);
    }

    private ProviderConfig? ResolveClaudeCodeProvider(AppConfig config)
    {
        var providerId = config.ActiveClaudeCodeProviderId;
        return config.Providers.FirstOrDefault(provider =>
            provider.SupportsClaudeCode &&
            string.Equals(provider.Id, providerId, StringComparison.OrdinalIgnoreCase));
    }

    private static JsonObject ParseObject(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new JsonObject();

        try
        {
            return JsonNode.Parse(text) as JsonObject ?? new JsonObject();
        }
        catch (JsonException)
        {
            return new JsonObject();
        }
    }

    private static string ResolveSettingsModel(ProviderConfig provider)
    {
        var model = string.IsNullOrWhiteSpace(provider.ClaudeCode.Model)
            ? provider.DefaultModel
            : provider.ClaudeCode.Model.Trim();
        if (string.IsNullOrWhiteSpace(model))
            model = provider.Models.FirstOrDefault()?.Id ?? "claude-sonnet-4-5";

        model = StripOneMillionSuffix(model);
        return provider.ClaudeCode.EnableOneMillionContext && IsOneMillionContextModel(model)
            ? model + "[1m]"
            : model;
    }

    private static JsonArray CreateAvailableModels(ProviderConfig provider)
    {
        var models = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var model in provider.Models)
        {
            if (string.IsNullOrWhiteSpace(model.Id))
                continue;

            var id = StripOneMillionSuffix(model.Id.Trim());
            models.Add(id);
            if (IsOneMillionContextModel(id))
                models.Add(id + "[1m]");
        }

        if (!string.IsNullOrWhiteSpace(provider.DefaultModel))
            models.Add(StripOneMillionSuffix(provider.DefaultModel.Trim()));
        if (!string.IsNullOrWhiteSpace(provider.ClaudeCode.Model))
            models.Add(ResolveSettingsModel(provider));

        var array = new JsonArray();
        foreach (var model in models)
            array.Add((JsonNode?)JsonValue.Create(model));
        return array;
    }

    public static bool IsOneMillionContextModel(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
            return false;

        var normalized = StripOneMillionSuffix(model).ToLowerInvariant();
        return string.Equals(normalized, "sonnet", StringComparison.Ordinal) ||
            normalized.StartsWith("claude-sonnet-4", StringComparison.Ordinal);
    }

    public static string StripOneMillionSuffix(string model)
    {
        return model.EndsWith("[1m]", StringComparison.OrdinalIgnoreCase)
            ? model[..^4]
            : model;
    }

    private static string BuildClientEndpoint(ProxySettings proxy)
    {
        var host = string.IsNullOrWhiteSpace(proxy.Host) ? "127.0.0.1" : proxy.Host.Trim();
        if (string.Equals(host, "0.0.0.0", StringComparison.Ordinal) ||
            string.Equals(host, "::", StringComparison.Ordinal))
        {
            host = "127.0.0.1";
        }

        var port = proxy.Port <= 0 ? 12785 : proxy.Port;
        return $"http://{host}:{port}";
    }

    private void WriteTextIfChanged(string path, string content, string? existing = null)
    {
        var fileExisted = File.Exists(path);
        existing ??= fileExisted ? File.ReadAllText(path) : null;
        if (fileExisted &&
            string.Equals(existing, content, StringComparison.Ordinal) &&
            !TextFileEncoding.HasUtf8Bom(path) &&
            ManagedFileBackup.HasBackup(path))
            return;

        ManagedFileBackup.EnsureBackedUp(path);
        WriteTextAtomically(path, content);
    }

    private static void WriteTextAtomically(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, content, TextFileEncoding.Utf8NoBom);
        File.Move(tempPath, path, overwrite: true);
    }
}
