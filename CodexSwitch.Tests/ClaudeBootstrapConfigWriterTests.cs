using System.Text.Json;
using CodexSwitch.Services;

namespace CodexSwitch.Tests;

public sealed class ClaudeBootstrapConfigWriterTests : IDisposable
{
    private readonly string _homeDirectory = Path.Combine(
        Path.GetTempPath(),
        "CodexSwitchTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Apply_CreatesClaudeBootstrapFiles_WhenMissing()
    {
        var writer = new ClaudeBootstrapConfigWriter(_homeDirectory);

        writer.Apply();

        using var claudeJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(_homeDirectory, ".claude.json")));
        Assert.True(claudeJson.RootElement.GetProperty("hasCompletedOnboarding").GetBoolean());

        using var claudeConfig = JsonDocument.Parse(File.ReadAllText(Path.Combine(_homeDirectory, ".claude", "config.json")));
        Assert.Equal("Routin", claudeConfig.RootElement.GetProperty("primaryApiKey").GetString());
    }

    [Fact]
    public void Apply_MergesClaudeBootstrapFiles_WithoutDroppingExistingProperties()
    {
        Directory.CreateDirectory(Path.Combine(_homeDirectory, ".claude"));
        File.WriteAllText(
            Path.Combine(_homeDirectory, ".claude.json"),
            """
            {
              "theme": "dark",
              "hasCompletedOnboarding": false,
              "nested": {
                "keep": true
              }
            }
            """);
        File.WriteAllText(
            Path.Combine(_homeDirectory, ".claude", "config.json"),
            """
            {
              "existing": "keep",
              "primaryApiKey": "old"
            }
            """);
        var writer = new ClaudeBootstrapConfigWriter(_homeDirectory);

        writer.Apply();

        using (var claudeJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(_homeDirectory, ".claude.json"))))
        {
            var root = claudeJson.RootElement;
            Assert.Equal("dark", root.GetProperty("theme").GetString());
            Assert.True(root.GetProperty("nested").GetProperty("keep").GetBoolean());
            Assert.True(root.GetProperty("hasCompletedOnboarding").GetBoolean());
        }

        using (var claudeConfig = JsonDocument.Parse(File.ReadAllText(Path.Combine(_homeDirectory, ".claude", "config.json"))))
        {
            var root = claudeConfig.RootElement;
            Assert.Equal("keep", root.GetProperty("existing").GetString());
            Assert.Equal("Routin", root.GetProperty("primaryApiKey").GetString());
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_homeDirectory))
            Directory.Delete(_homeDirectory, recursive: true);
    }
}
