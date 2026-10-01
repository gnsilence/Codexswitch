using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace CodexSwitch.Services;

public sealed class ConfigurationTransferService
{
    private const int FormatVersion = 1;
    private const int KeyIterations = 600_000;
    private readonly AppPaths _paths;

    public ConfigurationTransferService(AppPaths paths)
    {
        _paths = paths;
    }

    public void Export(
        AppConfig config,
        ModelPricingCatalog pricing,
        string password,
        string destinationPath)
    {
        ValidatePassword(password);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(pricing);

        var payload = new ConfigurationTransferPayload
        {
            CreatedAt = DateTimeOffset.UtcNow,
            Config = config,
            Pricing = pricing
        };
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(
            payload,
            CodexSwitchJsonContext.Default.ConfigurationTransferPayload);
        var encrypted = Encrypt(plaintext, password);
        WriteAtomically(destinationPath, encrypted);
    }

    public ConfigurationTransferPayload Import(string sourcePath, string password)
    {
        ValidatePassword(password);
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("Configuration backup was not found.", sourcePath);

        var encrypted = JsonSerializer.Deserialize(
            File.ReadAllBytes(sourcePath),
            CodexSwitchJsonContext.Default.EncryptedTransferFile);
        if (encrypted is null || encrypted.Version != FormatVersion)
            throw new InvalidDataException("Unsupported CodexSwitch backup format.");

        try
        {
            var plaintext = Decrypt(encrypted, password);
            return JsonSerializer.Deserialize(
                       plaintext,
                       CodexSwitchJsonContext.Default.ConfigurationTransferPayload) ??
                throw new InvalidDataException("The configuration backup is empty.");
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException or JsonException)
        {
            throw new InvalidDataException("The backup password is incorrect or the backup is corrupt.", ex);
        }
    }

    public void ExportDiagnostics(
        AppConfig config,
        ModelPricingCatalog pricing,
        ProxyRuntimeState proxyState,
        string destinationPath)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(pricing);
        ArgumentNullException.ThrowIfNull(proxyState);

        var stagingDirectory = Path.Combine(
            _paths.UpdateDirectory,
            "diagnostics-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingDirectory);
        try
        {
            var diagnostics = new DiagnosticSnapshot
            {
                GeneratedAt = DateTimeOffset.UtcNow,
                Version = AppReleaseInfo.CurrentVersionTag,
                OperatingSystem = Environment.OSVersion.VersionString,
                Architecture = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString(),
                Proxy = proxyState,
                Config = CreateRedactedConfig(config),
                Pricing = pricing
            };
            File.WriteAllText(
                Path.Combine(stagingDirectory, "diagnostics.json"),
                JsonSerializer.Serialize(
                    diagnostics,
                    CodexSwitchJsonContext.Default.DiagnosticSnapshot),
                TextFileEncoding.Utf8NoBom);

            var logDirectory = Path.Combine(stagingDirectory, "logs");
            Directory.CreateDirectory(logDirectory);
            var logPaths = new List<string>();
            if (File.Exists(_paths.UsageLogPath))
                logPaths.Add(_paths.UsageLogPath);
            if (Directory.Exists(_paths.UsageLogDirectory))
            {
                logPaths.AddRange(Directory.EnumerateFiles(
                    _paths.UsageLogDirectory,
                    "*.jsonl",
                    SearchOption.AllDirectories));
            }

            foreach (var path in logPaths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var relativePath = Path.GetRelativePath(_paths.UsageLogDirectory, path);
                if (string.Equals(path, _paths.UsageLogPath, StringComparison.OrdinalIgnoreCase))
                    relativePath = Path.GetFileName(path);
                var destination = Path.Combine(logDirectory, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                RedactUsageLogFile(path, destination);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            if (File.Exists(destinationPath))
                File.Delete(destinationPath);
            ZipFile.CreateFromDirectory(stagingDirectory, destinationPath, CompressionLevel.Fastest, includeBaseDirectory: false);
        }
        finally
        {
            try
            {
                if (Directory.Exists(stagingDirectory))
                    Directory.Delete(stagingDirectory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static AppConfig CreateRedactedConfig(AppConfig config)
    {
        var json = JsonSerializer.Serialize(config, CodexSwitchJsonContext.Default.AppConfig);
        var node = JsonNode.Parse(json)?.AsObject() ??
            throw new InvalidDataException("Unable to create diagnostic configuration.");

        RedactNode(node);

        return JsonSerializer.Deserialize(
                   node.ToJsonString(),
                   CodexSwitchJsonContext.Default.AppConfig) ??
            throw new InvalidDataException("Unable to create diagnostic configuration.");
    }

    private static void RedactUsageLogFile(string sourcePath, string destinationPath)
    {
        using var input = new StreamReader(sourcePath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        using var output = new StreamWriter(destinationPath, append: false, TextFileEncoding.Utf8NoBom);
        while (input.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            try
            {
                var record = JsonSerializer.Deserialize(
                    line,
                    CodexSwitchJsonContext.Default.UsageLogRecord);
                if (record is not null)
                {
                    record.Error = string.IsNullOrWhiteSpace(record.Error)
                        ? null
                        : "[redacted]";
                    output.WriteLine(JsonSerializer.Serialize(
                        record,
                        CodexSwitchJsonContext.Default.UsageLogRecord));
                    continue;
                }
            }
            catch (JsonException)
            {
            }

            output.WriteLine("{\"error\":\"[redacted]\"}");
        }
    }

    private static void RedactNode(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            foreach (var property in obj.ToArray())
            {
                var name = property.Key;
                if (IsSecretProperty(name))
                {
                    obj[name] = "[redacted]";
                    continue;
                }

                if (property.Value is JsonValue value &&
                    value.TryGetValue<string>(out var text) &&
                    IsUrlProperty(name))
                {
                    obj[name] = RedactUrl(text);
                    continue;
                }

                if (property.Value is not null)
                    RedactNode(property.Value);
            }

            return;
        }

        if (node is JsonArray array)
        {
            foreach (var child in array)
            {
                if (child is not null)
                    RedactNode(child);
            }
        }
    }

    private static bool IsSecretProperty(string name)
    {
        var normalized = name.Replace("_", "", StringComparison.Ordinal)
            .Replace("-", "", StringComparison.Ordinal)
            .ToLowerInvariant();
        return normalized.Contains("apikey", StringComparison.Ordinal) ||
            normalized.Contains("token", StringComparison.Ordinal) ||
            normalized.Contains("authorization", StringComparison.Ordinal) ||
            normalized.Contains("password", StringComparison.Ordinal) ||
            normalized.Contains("secret", StringComparison.Ordinal);
    }

    private static bool IsUrlProperty(string name)
    {
        return name.Contains("url", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("endpoint", StringComparison.OrdinalIgnoreCase);
    }

    private static string RedactUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return url;

        var builder = new UriBuilder(uri);
        builder.UserName = "";
        builder.Password = "";
        if (!string.IsNullOrWhiteSpace(uri.Query))
        {
            builder.Query = string.Join(
                "&",
                uri.Query.TrimStart('?')
                    .Split('&', StringSplitOptions.RemoveEmptyEntries)
                    .Select(part => part.Split('=', 2)[0] + "=[redacted]"));
        }
        return builder.Uri.ToString();
    }

    private static EncryptedTransferFile Encrypt(byte[] plaintext, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var ciphertext = new byte[plaintext.Length];
        var key = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            KeyIterations,
            HashAlgorithmName.SHA256,
            32);
        using var aes = new AesGcm(key, tag.Length);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);
        return new EncryptedTransferFile
        {
            Version = FormatVersion,
            Salt = Convert.ToBase64String(salt),
            Nonce = Convert.ToBase64String(nonce),
            Ciphertext = Convert.ToBase64String(ciphertext),
            Tag = Convert.ToBase64String(tag)
        };
    }

    private static byte[] Decrypt(EncryptedTransferFile encrypted, string password)
    {
        var salt = Convert.FromBase64String(encrypted.Salt);
        var nonce = Convert.FromBase64String(encrypted.Nonce);
        var ciphertext = Convert.FromBase64String(encrypted.Ciphertext);
        var tag = Convert.FromBase64String(encrypted.Tag);
        var key = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            KeyIterations,
            HashAlgorithmName.SHA256,
            32);
        using var aes = new AesGcm(key, tag.Length);
        var plaintext = new byte[ciphertext.Length];
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return plaintext;
    }

    private static void ValidatePassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
            throw new ArgumentException("The backup password must contain at least 8 characters.", nameof(password));
    }

    private static void WriteAtomically(string path, EncryptedTransferFile value)
    {
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory))
            throw new ArgumentException("Destination path must include a directory.", nameof(path));
        Directory.CreateDirectory(directory);
        var tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = File.Create(tempPath))
            {
                JsonSerializer.Serialize(stream, value, CodexSwitchJsonContext.Default.EncryptedTransferFile);
            }

            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }
}

public sealed class ConfigurationTransferPayload
{
    public DateTimeOffset CreatedAt { get; set; }

    public AppConfig Config { get; set; } = new();

    public ModelPricingCatalog Pricing { get; set; } = new();
}

public sealed class EncryptedTransferFile
{
    public int Version { get; set; }

    public string Salt { get; set; } = "";

    public string Nonce { get; set; } = "";

    public string Ciphertext { get; set; } = "";

    public string Tag { get; set; } = "";
}

public sealed class DiagnosticSnapshot
{
    public DateTimeOffset GeneratedAt { get; set; }

    public string Version { get; set; } = "";

    public string OperatingSystem { get; set; } = "";

    public string Architecture { get; set; } = "";

    public ProxyRuntimeState Proxy { get; set; } = new();

    public AppConfig Config { get; set; } = new();

    public ModelPricingCatalog Pricing { get; set; } = new();
}
