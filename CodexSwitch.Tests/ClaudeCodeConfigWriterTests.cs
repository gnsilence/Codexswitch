using CodexSwitch.Models;
using CodexSwitch.Services;

namespace CodexSwitch.Tests;

public sealed class ClaudeCodeConfigWriterTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        "CodexSwitchTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Apply_LeavesUserSettingsUntouched_WhenManageDisabled()
    {
        var paths = CreatePaths("disabled");
        Directory.CreateDirectory(paths.ClaudeDirectory);
        const string original = "{\n  \"env\": { \"ANTHROPIC_BASE_URL\": \"https://api.anthropic.com\" }\n}\n";
        File.WriteAllText(paths.ClaudeSettingsPath, original);

        var writer = new ClaudeCodeConfigWriter(paths);
        writer.Apply(CreateConfig(manageClaudeCode: false));

        Assert.Equal(original, File.ReadAllText(paths.ClaudeSettingsPath));
        Assert.False(File.Exists(BackupPath(paths.ClaudeSettingsPath)));
    }

    [Fact]
    public void Apply_WritesManagedSettings_WhenManageEnabled()
    {
        var paths = CreatePaths("enabled");
        Directory.CreateDirectory(paths.ClaudeDirectory);
        const string original = "{\n  \"env\": { \"ANTHROPIC_BASE_URL\": \"https://api.anthropic.com\" }\n}\n";
        File.WriteAllText(paths.ClaudeSettingsPath, original);

        var writer = new ClaudeCodeConfigWriter(paths);
        writer.Apply(CreateConfig(manageClaudeCode: true));

        var settings = File.ReadAllText(paths.ClaudeSettingsPath);
        Assert.Contains("\"ANTHROPIC_BASE_URL\": \"http://127.0.0.1:12785\"", settings, StringComparison.Ordinal);
        Assert.Contains("\"ANTHROPIC_AUTH_TOKEN\": \"sk-codex\"", settings, StringComparison.Ordinal);
        Assert.True(File.Exists(BackupPath(paths.ClaudeSettingsPath)));
        Assert.Equal(original, File.ReadAllText(BackupPath(paths.ClaudeSettingsPath)));
    }

    [Fact]
    public void RestoreOriginal_DoesNotDeleteUserSettings_WhenNeverManaged()
    {
        var paths = CreatePaths("restore-safe");
        Directory.CreateDirectory(paths.ClaudeDirectory);
        const string original = "{\n  \"model\": \"claude-sonnet-4-5\"\n}\n";
        File.WriteAllText(paths.ClaudeSettingsPath, original);

        var writer = new ClaudeCodeConfigWriter(paths);
        writer.RestoreOriginal();

        Assert.True(File.Exists(paths.ClaudeSettingsPath));
        Assert.Equal(original, File.ReadAllText(paths.ClaudeSettingsPath));
    }

    [Fact]
    public void Apply_RestoresOriginal_WhenManageToggledOff()
    {
        var paths = CreatePaths("toggle-off");
        Directory.CreateDirectory(paths.ClaudeDirectory);
        const string original = "{\n  \"env\": { \"ANTHROPIC_BASE_URL\": \"https://api.anthropic.com\" }\n}\n";
        File.WriteAllText(paths.ClaudeSettingsPath, original);

        var writer = new ClaudeCodeConfigWriter(paths);
        writer.Apply(CreateConfig(manageClaudeCode: true));
        Assert.NotEqual(original, File.ReadAllText(paths.ClaudeSettingsPath));

        writer.Apply(CreateConfig(manageClaudeCode: false));

        Assert.Equal(original, File.ReadAllText(paths.ClaudeSettingsPath));
        Assert.False(File.Exists(BackupPath(paths.ClaudeSettingsPath)));
    }

    private AppPaths CreatePaths(string name)
    {
        var appRoot = Path.Combine(_tempDirectory, name, "appdata");
        var codexRoot = Path.Combine(_tempDirectory, name, "codex");
        var claudeRoot = Path.Combine(_tempDirectory, name, "claude");
        return new AppPaths(appRoot, codexRoot, claudeRoot);
    }

    private static AppConfig CreateConfig(bool manageClaudeCode)
    {
        return new AppConfig
        {
            ActiveClaudeCodeProviderId = "anthropic",
            Proxy = new ProxySettings
            {
                Host = "127.0.0.1",
                Port = 12785,
                InboundApiKey = "sk-codex",
                ManageClaudeCodeConfig = manageClaudeCode
            },
            Providers =
            {
                new ProviderConfig
                {
                    Id = "anthropic",
                    Protocol = ProviderProtocol.AnthropicMessages,
                    SupportsClaudeCode = true,
                    DefaultModel = "claude-sonnet-4-5",
                    Models =
                    {
                        new ModelRouteConfig { Id = "claude-sonnet-4-5" }
                    }
                }
            }
        };
    }

    private static string BackupPath(string path)
    {
        return path + ".bak";
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
            Directory.Delete(_tempDirectory, recursive: true);
    }
}
