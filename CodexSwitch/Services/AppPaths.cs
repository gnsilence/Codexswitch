namespace CodexSwitch.Services;

public sealed class AppPaths
{
    public AppPaths(string? rootDirectory = null, string? codexDirectory = null, string? claudeDirectory = null)
    {
        var root = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CodexSwitch");

        Directory.CreateDirectory(root);
        RootDirectory = root;
        ConfigPath = Path.Combine(root, "config.json");
        SecretsPath = Path.Combine(root, "secrets.v1.json");
        SecretKeyPath = Path.Combine(root, "secrets.key");
        SecretKeyProtectedPath = Path.Combine(root, "secrets.key.dpapi");
        ConfigBackupDirectory = Path.Combine(root, "config-backups");
        PricingPath = Path.Combine(root, "model-pricing.json");
        UsageLogPath = Path.Combine(root, "usage-log.jsonl");
        UsageLogDirectory = Path.Combine(root, "usage-logs");
        IconDirectory = Path.Combine(root, "icons");
        UpdateDirectory = Path.Combine(root, "updates");
        Directory.CreateDirectory(UsageLogDirectory);
        Directory.CreateDirectory(IconDirectory);
        Directory.CreateDirectory(UpdateDirectory);
        Directory.CreateDirectory(ConfigBackupDirectory);

        var codexRoot = codexDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".codex");
        CodexDirectory = codexRoot;
        CodexConfigPath = Path.Combine(codexRoot, "config.toml");
        CodexAuthPath = Path.Combine(codexRoot, "auth.json");
        CodexModelCatalogPath = Path.Combine(codexRoot, CodexConfigWriter.ManagedModelCatalogFileName);

        var claudeRoot = claudeDirectory ??
            (codexDirectory is not null
                ? Path.Combine(Path.GetDirectoryName(codexRoot) ?? root, ".claude")
                : Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".claude"));
        ClaudeDirectory = claudeRoot;
        ClaudeSettingsPath = Path.Combine(claudeRoot, "settings.json");
    }

    public string RootDirectory { get; }

    public string ConfigPath { get; }

    public string SecretsPath { get; }

    public string SecretKeyPath { get; }

    public string SecretKeyProtectedPath { get; }

    public string ConfigBackupDirectory { get; }

    public string PricingPath { get; }

    public string UsageLogPath { get; }

    public string UsageLogDirectory { get; }

    public string IconDirectory { get; }

    public string UpdateDirectory { get; }

    public string CodexDirectory { get; }

    public string CodexConfigPath { get; }

    public string CodexAuthPath { get; }

    public string CodexModelCatalogPath { get; }

    public string ClaudeDirectory { get; }

    public string ClaudeSettingsPath { get; }
}
