using System.Text.Json.Nodes;

namespace CodexSwitch.Services;

public sealed class ClaudeBootstrapConfigWriter
{
    private const string ClaudeJsonFileName = ".claude.json";
    private const string ClaudeDirectoryName = ".claude";
    private const string ClaudeConfigFileName = "config.json";
    private const string PrimaryApiKey = "Routin";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _homeDirectory;

    public ClaudeBootstrapConfigWriter(string? homeDirectory = null)
    {
        _homeDirectory = ResolveHomeDirectory(homeDirectory);
    }

    public static bool TryApplyForCurrentUser()
    {
        try
        {
            new ClaudeBootstrapConfigWriter().Apply();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void Apply()
    {
        if (string.IsNullOrWhiteSpace(_homeDirectory))
            return;

        WriteClaudeJson();
        WriteClaudeConfig();
    }

    private void WriteClaudeJson()
    {
        var path = Path.Combine(_homeDirectory, ClaudeJsonFileName);
        UpdateJsonObjectFile(path, root =>
        {
            root["hasCompletedOnboarding"] = true;
        });
    }

    private void WriteClaudeConfig()
    {
        var path = Path.Combine(_homeDirectory, ClaudeDirectoryName, ClaudeConfigFileName);
        UpdateJsonObjectFile(path, root =>
        {
            root["primaryApiKey"] = PrimaryApiKey;
        });
    }

    private static void UpdateJsonObjectFile(string path, Action<JsonObject> update)
    {
        var existing = File.Exists(path)
            ? File.ReadAllText(path)
            : "";
        var root = ParseObject(existing);
        update(root);

        var content = root.ToJsonString(JsonOptions) + Environment.NewLine;
        if (File.Exists(path) &&
            string.Equals(existing, content, StringComparison.Ordinal) &&
            !TextFileEncoding.HasUtf8Bom(path))
        {
            return;
        }

        WriteTextAtomically(path, content);
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

    private static string ResolveHomeDirectory(string? homeDirectory)
    {
        if (!string.IsNullOrWhiteSpace(homeDirectory))
            return homeDirectory;

        var resolved = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(resolved))
            return resolved;

        resolved = Environment.GetEnvironmentVariable("HOME");
        if (!string.IsNullOrWhiteSpace(resolved))
            return resolved;

        return Environment.GetEnvironmentVariable("USERPROFILE") ?? "";
    }

    private static void WriteTextAtomically(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, content, TextFileEncoding.Utf8NoBom);
        File.Move(tempPath, path, overwrite: true);
    }
}
