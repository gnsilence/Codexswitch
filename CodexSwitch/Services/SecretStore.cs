using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace CodexSwitch.Services;

/// <summary>
/// Stores provider and local proxy credentials outside config.json.
/// Native user protection is preferred; the encrypted file fallback is explicitly surfaced
/// through <see cref="IsUsingFallbackStorage"/>.
/// </summary>
internal interface ISecretStore
{
    bool IsUsingFallbackStorage { get; }

    bool Hydrate(AppConfig config);

    void Persist(AppConfig config);

    bool TryGet(string name, out string value);

}

internal sealed class SecretStore : ISecretStore
{
    private const int CurrentVersion = 1;
    private const string MacService = "CodexSwitch";
    private const string MacAccount = "CodexSwitch.Secrets";
    private const string LinuxService = "CodexSwitch";
    private const string LinuxKey = "vault";
    private readonly AppPaths _paths;
    private readonly object _sync = new();
    private Dictionary<string, string> _values = new(StringComparer.Ordinal);
    private bool _loaded;
    private byte[]? _encryptionKey;

    public SecretStore(AppPaths paths)
    {
        _paths = paths;
    }

    public bool IsUsingFallbackStorage { get; private set; }

    public bool Hydrate(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        lock (_sync)
        {
            EnsureLoaded();
            var migrated = false;
            var inboundKey = config.Proxy.InboundApiKey;
            if (!string.IsNullOrWhiteSpace(inboundKey))
                migrated |= SetOrUpdate("proxy.inbound-api-key", inboundKey);
            else if (TryGet("proxy.inbound-api-key", out var storedInboundKey))
                config.Proxy.InboundApiKey = storedInboundKey;

            var customProxyUrl = config.Network.CustomProxyUrl;
            if (!string.IsNullOrWhiteSpace(customProxyUrl))
                migrated |= SetOrUpdate("network.proxy-url", customProxyUrl);
            else if (TryGet("network.proxy-url", out var storedProxyUrl))
                config.Network.CustomProxyUrl = storedProxyUrl;

            foreach (var provider in config.Providers)
            {
                var providerKey = ProviderKey(provider.Id);
                if (!string.IsNullOrWhiteSpace(provider.ApiKey))
                    migrated |= SetOrUpdate(providerKey + ".api-key", provider.ApiKey);
                else if (TryGet(providerKey + ".api-key", out var storedApiKey))
                    provider.ApiKey = storedApiKey;

                foreach (var account in provider.OAuthAccounts)
                {
                    var accountKey = providerKey + ".oauth." + account.Id;
                    migrated |= MigrateOrHydrate(accountKey + ".access-token", account.AccessToken, value => account.AccessToken = value);
                    migrated |= MigrateOrHydrate(accountKey + ".refresh-token", account.RefreshToken, value => account.RefreshToken = value);
                    migrated |= MigrateOrHydrate(accountKey + ".id-token", account.IdToken, value => account.IdToken = value);
                }
            }

            if (migrated)
                Save();
            return migrated;
        }
    }

    public void Persist(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        EnsureLoaded();
        SetOrRemove("proxy.inbound-api-key", config.Proxy.InboundApiKey);
        SetOrRemove("network.proxy-url", config.Network.CustomProxyUrl);
        foreach (var provider in config.Providers)
        {
            var providerKey = ProviderKey(provider.Id);
            SetOrRemove(providerKey + ".api-key", provider.ApiKey);
            foreach (var account in provider.OAuthAccounts)
            {
                var accountKey = providerKey + ".oauth." + account.Id;
                SetOrRemove(accountKey + ".access-token", account.AccessToken);
                SetOrRemove(accountKey + ".refresh-token", account.RefreshToken);
                SetOrRemove(accountKey + ".id-token", account.IdToken);
            }
        }
        Save();
    }

    public bool TryGet(string name, out string value)
    {
        EnsureLoaded();
        return _values.TryGetValue(name, out value!);
    }

    private sealed record SecretBinding(string Key, string Value, Action<string> Assign);

    private static IEnumerable<SecretBinding> EnumerateSecrets(AppConfig config)
    {
        yield return new("proxy.inbound-api-key", config.Proxy.InboundApiKey, value => config.Proxy.InboundApiKey = value);
        yield return new("network.proxy-url", config.Network.CustomProxyUrl, value => config.Network.CustomProxyUrl = value);
        foreach (var provider in config.Providers)
        {
            var prefix = ProviderKey(provider.Id);
            yield return new(prefix + ".api-key", provider.ApiKey, value => provider.ApiKey = value);
            foreach (var account in provider.OAuthAccounts)
            {
                var accountPrefix = prefix + ".oauth." + account.Id;
                yield return new(accountPrefix + ".access-token", account.AccessToken, value => account.AccessToken = value);
                yield return new(accountPrefix + ".refresh-token", account.RefreshToken, value => account.RefreshToken = value);
                yield return new(accountPrefix + ".id-token", account.IdToken ?? "", value => account.IdToken = value);
            }
        }
    }

    private bool MigrateOrHydrate(string key, string? legacyValue, Action<string> assign)
    {
        if (!string.IsNullOrWhiteSpace(legacyValue))
            return SetOrUpdate(key, legacyValue);

        if (!TryGet(key, out var storedValue))
            return false;

        assign(storedValue);
        return false;
    }

    private bool SetOrUpdate(string key, string value)
    {
        if (_values.TryGetValue(key, out var current) &&
            string.Equals(current, value, StringComparison.Ordinal))
        {
            return false;
        }

        _values[key] = value;
        return true;
    }

    private static string ProviderKey(string providerId) => "provider." + providerId;

    private void SetOrRemove(string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            _values.Remove(key);
        else
            _values[key] = value;
    }

    private void EnsureLoaded()
    {
        lock (_sync)
        {
            if (_loaded)
                return;

            _encryptionKey = LoadOrCreateEncryptionKey();
            _values = LoadValues(_encryptionKey);
            _loaded = true;
        }
    }

    private void Save()
    {
        lock (_sync)
        {
            EnsureLoaded();
            var plaintext = JsonSerializer.SerializeToUtf8Bytes(
                _values,
                CodexSwitchJsonContext.Default.DictionaryStringString);
            var tempPath = _paths.SecretsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(tempPath, Protect(plaintext));
                RestrictFileAccess(tempPath);
                File.Move(tempPath, _paths.SecretsPath, overwrite: true);
                CryptographicOperations.ZeroMemory(plaintext);
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }
    }

    private Dictionary<string, string> LoadValues(byte[] key)
    {
        if (!File.Exists(_paths.SecretsPath))
            return new Dictionary<string, string>(StringComparer.Ordinal);

        var plaintext = UnprotectCore(File.ReadAllBytes(_paths.SecretsPath), key);
        try
        {
            return JsonSerializer.Deserialize(plaintext, CodexSwitchJsonContext.Default.DictionaryStringString) ??
                new Dictionary<string, string>(StringComparer.Ordinal);
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    public byte[] Protect(byte[] plaintext)
    {
        lock (_sync)
        {
            EnsureLoaded();
            var nonce = RandomNumberGenerator.GetBytes(12);
            var tag = new byte[16];
            var ciphertext = new byte[plaintext.Length];
            using var aes = new AesGcm(_encryptionKey!, tag.Length);
            aes.Encrypt(nonce, plaintext, ciphertext, tag);
            return JsonSerializer.SerializeToUtf8Bytes(new SecretVaultFile
            {
                Version = CurrentVersion,
                Nonce = Convert.ToBase64String(nonce),
                Ciphertext = Convert.ToBase64String(ciphertext),
                Tag = Convert.ToBase64String(tag)
            }, CodexSwitchJsonContext.Default.SecretVaultFile);
        }
    }

    public byte[] Unprotect(byte[] encrypted)
    {
        lock (_sync)
        {
            EnsureLoaded();
            return UnprotectCore(encrypted, _encryptionKey!);
        }
    }

    private static byte[] UnprotectCore(byte[] encrypted, byte[] key)
    {
        try
        {
            var file = JsonSerializer.Deserialize(encrypted, CodexSwitchJsonContext.Default.SecretVaultFile);
            if (file is null || file.Version != CurrentVersion)
                throw new InvalidDataException("Unsupported CodexSwitch secret vault version.");
            var nonce = Convert.FromBase64String(file.Nonce);
            var ciphertext = Convert.FromBase64String(file.Ciphertext);
            var tag = Convert.FromBase64String(file.Tag);
            if (nonce.Length != 12 || tag.Length != 16)
                throw new InvalidDataException("Invalid secret vault parameters.");
            var plaintext = new byte[ciphertext.Length];
            using var aes = new AesGcm(key, tag.Length);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
            return plaintext;
        }
        catch (FormatException ex)
        {
            throw new InvalidDataException("CodexSwitch secret vault is corrupt.", ex);
        }
        catch (CryptographicException ex)
        {
            throw new InvalidDataException("CodexSwitch secret vault could not be decrypted.", ex);
        }
    }

    private byte[] LoadOrCreateEncryptionKey()
    {
        if (File.Exists(_paths.SecretKeyPath))
        {
            IsUsingFallbackStorage = true;
            var existing = File.ReadAllBytes(_paths.SecretKeyPath);
            if (existing.Length != 32)
                throw new InvalidDataException("The fallback secret key is corrupt.");
            return existing;
        }
        if (OperatingSystem.IsWindows() &&
            TryLoadWindowsProtectedKey(out var windowsKey))
        {
            return windowsKey;
        }

        if (OperatingSystem.IsMacOS() &&
            TryLoadMacKey(out var macKey))
        {
            return macKey;
        }

        if (OperatingSystem.IsLinux() &&
            TryLoadLinuxKey(out var linuxKey))
        {
            return linuxKey;
        }

        IsUsingFallbackStorage = true;
        if (File.Exists(_paths.SecretsPath) || File.Exists(_paths.SecretKeyProtectedPath))
            throw new InvalidDataException("The existing native secret key is unavailable. Restore it before saving configuration.");

        var key = RandomNumberGenerator.GetBytes(32);
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write
        };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        using (var file = new FileStream(_paths.SecretKeyPath, options))
            file.Write(key);
        return key;
    }

    private static void RestrictFileAccess(string path)
    {
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private bool TryLoadMacKey(out byte[] key)
    {
        key = [];
        var result = RunSecretCommand(
            "security",
            ["find-generic-password", "-a", MacAccount, "-s", MacService, "-w"]);
        if (result.ExitCode == 0 &&
            TryDecodeKey(result.Output, out key))
        {
            return true;
        }

        if (File.Exists(_paths.SecretsPath))
            return false;
        key = RandomNumberGenerator.GetBytes(32);
        var encoded = Convert.ToBase64String(key);
        result = RunSecretCommand(
            "security",
            [
                "add-generic-password", "-U",
                "-a", MacAccount,
                "-s", MacService,
                "-w", encoded
            ]);
        return result.ExitCode == 0;
    }

    private bool TryLoadLinuxKey(out byte[] key)
    {
        key = [];
        var result = RunSecretCommand(
            "secret-tool",
            ["lookup", "service", LinuxService, "key", LinuxKey]);
        if (result.ExitCode == 0 &&
            TryDecodeKey(result.Output, out key))
        {
            return true;
        }

        if (File.Exists(_paths.SecretsPath))
            return false;
        key = RandomNumberGenerator.GetBytes(32);
        var encoded = Convert.ToBase64String(key);
        result = RunSecretCommand(
            "secret-tool",
            ["store", "--label=CodexSwitch secrets", "service", LinuxService, "key", LinuxKey],
            encoded);
        return result.ExitCode == 0;
    }

    private static bool TryDecodeKey(string value, out byte[] key)
    {
        try
        {
            key = Convert.FromBase64String(value.Trim());
            return key.Length == 32;
        }
        catch (FormatException)
        {
            key = [];
            return false;
        }
    }

    private static (int ExitCode, string Output) RunSecretCommand(
        string fileName,
        IReadOnlyList<string> arguments,
        string? standardInput = null)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = standardInput is not null
                }
            };
            foreach (var argument in arguments)
                process.StartInfo.ArgumentList.Add(argument);

            if (!process.Start())
                return (-1, "");

            if (standardInput is not null)
            {
                process.StandardInput.Write(standardInput);
                process.StandardInput.Close();
            }

            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(3000))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
                return (-1, "");
            }
            error.GetAwaiter().GetResult();
            return (process.ExitCode, output.GetAwaiter().GetResult());
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return (-1, "");
        }
    }

    private bool TryLoadWindowsProtectedKey(out byte[] key)
    {
        key = [];
        var path = _paths.SecretKeyProtectedPath;
        try
        {
            if (!File.Exists(path))
            {
                if (File.Exists(_paths.SecretsPath))
                    return false;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                key = RandomNumberGenerator.GetBytes(32);
                File.WriteAllBytes(path, ProtectData(key));
                return true;
            }

            key = UnprotectData(File.ReadAllBytes(path));
            return key.Length == 32;
        }
        catch (Exception ex) when (ex is IOException or CryptographicException or UnauthorizedAccessException)
        {
            key = [];
            return false;
        }
    }

    private static byte[] ProtectData(byte[] value)
    {
        using var input = new DataBlob(value);
        var inputBlob = input.Blob;
        if (!CryptProtectData(ref inputBlob, "CodexSwitch secrets", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out var output))
            throw new CryptographicException(Marshal.GetLastWin32Error());

        try
        {
            return output.ToArray();
        }
        finally
        {
            LocalFree(output.Data);
        }
    }

    private static byte[] UnprotectData(byte[] value)
    {
        using var input = new DataBlob(value);
        var inputBlob = input.Blob;
        if (!CryptUnprotectData(ref inputBlob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out var output))
            throw new CryptographicException(Marshal.GetLastWin32Error());

        try
        {
            return output.ToArray();
        }
        finally
        {
            LocalFree(output.Data);
        }
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(
        ref NativeDataBlob dataIn,
        string description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        int flags,
        out NativeDataBlob dataOut);

    [DllImport("crypt32.dll", SetLastError = true)]
    private static extern bool CryptUnprotectData(
        ref NativeDataBlob dataIn,
        IntPtr description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        int flags,
        out NativeDataBlob dataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeDataBlob
    {
        public int Size;
        public IntPtr Data;

        public byte[] ToArray()
        {
            var result = new byte[Size];
            Marshal.Copy(Data, result, 0, Size);
            return result;
        }
    }

    private sealed class DataBlob : IDisposable
    {
        private readonly GCHandle _handle;

        public DataBlob(byte[] value)
        {
            _handle = GCHandle.Alloc(value, GCHandleType.Pinned);
            Blob = new NativeDataBlob
            {
                Size = value.Length,
                Data = _handle.AddrOfPinnedObject()
            };
        }

        public NativeDataBlob Blob { get; }

        public void Dispose()
        {
            if (_handle.IsAllocated)
                _handle.Free();
        }
    }
}

internal sealed class SecretVaultFile
{
    public int Version { get; set; }

    public string Nonce { get; set; } = "";

    public string Ciphertext { get; set; } = "";

    public string Tag { get; set; } = "";
}
