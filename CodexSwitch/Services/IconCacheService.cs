using System.Net.Http;
using System.Text.RegularExpressions;
using Avalonia.Platform;
using Avalonia.Styling;

namespace CodexSwitch.Services;

public enum IconThemeVariant
{
    Light,
    Dark
}

public sealed class IconCacheService
{
    public const string RoutinAiIconSlug = "routinai";
    private const string LobeCdnBaseUrl = "https://unpkg.com/@lobehub/icons-static-png@latest";
    private const string LobeMirrorBaseUrl = "https://registry.npmmirror.com/@lobehub/icons-static-png/latest/files";
    private const string XiaomiSiteUrl = "https://platform.xiaomimimo.com/";
    private const string XiaomiFallbackIconUrl = "https://platform.xiaomimimo.com/static/favicon.874c9507.png";
    private static readonly HashSet<string> BundledAnyThemeIconSlugs = new(StringComparer.OrdinalIgnoreCase)
    {
        "aioss",
        "codex-color",
        "claudecode-color",
        "xiaomi"
    };
    private static readonly HashSet<string> BundledDarkIconSlugs = new(StringComparer.OrdinalIgnoreCase)
    {
        "openai",
        "claude",
        "deepseek",
        "gemini"
    };
    private static readonly Regex ShortcutIconHrefRegex = new(
        "<link[^>]*rel=\"shortcut icon\"[^>]*href=\"(?<href>[^\"]+)\"",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private readonly AppPaths _paths;
    private readonly HttpClient _httpClient;

    public IconCacheService(AppPaths paths, HttpClient httpClient)
    {
        _paths = paths;
        _httpClient = httpClient;
    }

    public string GetIconPath(string? slug)
    {
        var normalized = NormalizeSlug(slug);
        var theme = ResolveIconTheme();

        if (string.Equals(normalized, RoutinAiIconSlug, StringComparison.OrdinalIgnoreCase))
            return ResolveBundledIconPath("logo.png") ??
                ResolveBundledIconAssetUri("logo.png") ??
                GetCachedIconPath(normalized, theme);

        if (TryResolveBundledIconPath(normalized, theme, out var bundledPath))
            return bundledPath;

        return GetCachedIconPath(normalized, theme);
    }

    public string GetIconUrl(string? slug)
    {
        return GetIconUrl(slug, ResolveIconTheme());
    }

    public string GetIconUrl(string? slug, IconThemeVariant theme)
    {
        var normalized = NormalizeSlug(slug);
        if (string.Equals(normalized, "xiaomi", StringComparison.OrdinalIgnoreCase))
            return XiaomiFallbackIconUrl;

        return $"{LobeCdnBaseUrl}/{ThemeFolder(theme)}/{normalized}.png";
    }

    public bool HasIcon(string? slug)
    {
        var normalized = NormalizeSlug(slug);
        var theme = ResolveIconTheme();
        if (string.Equals(normalized, RoutinAiIconSlug, StringComparison.OrdinalIgnoreCase))
            return ResolveBundledIconPath("logo.png") is not null ||
                ResolveBundledIconAssetUri("logo.png") is not null ||
                File.Exists(GetCachedIconPath(normalized, theme));

        return TryResolveBundledIconPath(normalized, theme, out _) ||
            File.Exists(GetCachedIconPath(normalized, theme));
    }

    public Task<bool> EnsureIconAsync(string? slug, CancellationToken cancellationToken = default)
    {
        return EnsureIconAsync(slug, ResolveIconTheme(), cancellationToken);
    }

    public async Task<bool> EnsureIconAsync(
        string? slug,
        IconThemeVariant theme,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeSlug(slug);
        if (string.Equals(normalized, RoutinAiIconSlug, StringComparison.OrdinalIgnoreCase) ||
            TryResolveBundledIconPath(normalized, theme, out _))
        {
            return false;
        }

        var path = GetCachedIconPath(normalized, theme);
        if (File.Exists(path))
            return false;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? _paths.IconDirectory);

            foreach (var iconUrl in await ResolveIconUrlsAsync(normalized, theme, cancellationToken))
            {
                try
                {
                    var bytes = await _httpClient.GetByteArrayAsync(iconUrl, cancellationToken);
                    await File.WriteAllBytesAsync(path, bytes, cancellationToken);
                    return true;
                }
                catch
                {
                    // Try the next fallback URL for this provider icon.
                }
            }
        }
        catch
        {
            // Icons are decorative; network failures should not block the local proxy.
        }

        return false;
    }

    public async Task EnsureDefaultIconsAsync(CancellationToken cancellationToken = default)
    {
        var theme = ResolveIconTheme();
        await Task.WhenAll(
            EnsureIconAsync("codex-color", cancellationToken),
            EnsureIconAsync("claudecode-color", cancellationToken),
            EnsureIconAsync("openai", theme, cancellationToken),
            EnsureIconAsync("claude", theme, cancellationToken),
            EnsureIconAsync("deepseek", theme, cancellationToken),
            EnsureIconAsync("xiaomi", theme, cancellationToken),
            EnsureIconAsync("grok", theme, cancellationToken),
            EnsureIconAsync("gemini", theme, cancellationToken),
            EnsureIconAsync(RoutinAiIconSlug, cancellationToken));
    }

    public static string ResolveModelIconSlug(string modelId, string? configuredSlug = null)
    {
        if (!string.IsNullOrWhiteSpace(configuredSlug))
            return NormalizeSlug(configuredSlug);

        var normalized = modelId.Trim().ToLowerInvariant();
        if (normalized.StartsWith("claude", StringComparison.Ordinal))
            return "claude";
        if (normalized.StartsWith("deepseek", StringComparison.Ordinal))
            return "deepseek";
        if (normalized.StartsWith("gemini", StringComparison.Ordinal))
            return "gemini";
        if (normalized.StartsWith("mimo", StringComparison.Ordinal))
            return "xiaomi";
        if (normalized.StartsWith("grok", StringComparison.Ordinal))
            return "grok";
        if (normalized.StartsWith("gpt", StringComparison.Ordinal) ||
            normalized.StartsWith("o1", StringComparison.Ordinal) ||
            normalized.StartsWith("o3", StringComparison.Ordinal) ||
            normalized.StartsWith("o4", StringComparison.Ordinal))
            return "openai";

        return "openai";
    }

    private static string NormalizeSlug(string? slug)
    {
        return string.IsNullOrWhiteSpace(slug)
            ? "openai"
            : slug.Trim().ToLowerInvariant().Replace(' ', '-');
    }

    private string GetCachedIconPath(string normalized, IconThemeVariant theme)
    {
        return Path.Combine(_paths.IconDirectory, ThemeFolder(theme), normalized + ".png");
    }

    private static IconThemeVariant ResolveIconTheme()
    {
        var app = Application.Current;
        return app?.ActualThemeVariant == ThemeVariant.Light
            ? IconThemeVariant.Light
            : IconThemeVariant.Dark;
    }

    private static string ThemeFolder(IconThemeVariant theme)
    {
        return theme == IconThemeVariant.Light ? "light" : "dark";
    }

    private static bool TryResolveBundledIconPath(
        string normalized,
        IconThemeVariant theme,
        out string path)
    {
        foreach (var candidate in GetBundledIconCandidates(normalized, theme))
        {
            path = ResolveBundledIconPath(candidate) ??
                ResolveBundledIconAssetUri(candidate) ??
                "";
            if (!string.IsNullOrWhiteSpace(path))
                return true;
        }

        path = "";
        return false;
    }

    private static IEnumerable<string> GetBundledIconCandidates(string normalized, IconThemeVariant theme)
    {
        var fileName = normalized + ".png";
        var themeFolder = ThemeFolder(theme);

        yield return Path.Combine(themeFolder, fileName);
        yield return normalized + "-" + themeFolder + ".png";

        if (BundledAnyThemeIconSlugs.Contains(normalized) ||
            (theme == IconThemeVariant.Dark && BundledDarkIconSlugs.Contains(normalized)))
        {
            yield return fileName;
        }
    }

    private static string? ResolveBundledIconPath(string fileName)
    {
        var relativePaths = new[]
        {
            Path.Combine("Assets", "icons", fileName),
            Path.Combine("CodexSwitch", "Assets", "icons", fileName)
        };

        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            foreach (var relativePath in relativePaths)
            {
                var candidate = Path.Combine(directory.FullName, relativePath);
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return null;
    }

    private static string? ResolveBundledIconAssetUri(string relativeIconPath)
    {
        var assetPath = relativeIconPath.Replace('\\', '/');
        var uriText = $"avares://CodexSwitch/Assets/icons/{assetPath}";

        try
        {
            var uri = new Uri(uriText);
            return AssetLoader.Exists(uri) ? uriText : null;
        }
        catch
        {
            return null;
        }
    }

    private async Task<IReadOnlyList<string>> ResolveIconUrlsAsync(
        string normalized,
        IconThemeVariant theme,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(normalized, "xiaomi", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                GetIconUrl(normalized, theme),
                $"{LobeMirrorBaseUrl}/{ThemeFolder(theme)}/{normalized}.png"
            ];
        }

        var urls = new List<string>();
        var discovered = await TryResolveShortcutIconUrlAsync(XiaomiSiteUrl, cancellationToken);
        if (!string.IsNullOrWhiteSpace(discovered))
            urls.Add(discovered);

        urls.Add(XiaomiFallbackIconUrl);
        return urls.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private async Task<string?> TryResolveShortcutIconUrlAsync(string pageUrl, CancellationToken cancellationToken)
    {
        try
        {
            var html = await _httpClient.GetStringAsync(pageUrl, cancellationToken);
            var match = ShortcutIconHrefRegex.Match(html);
            if (!match.Success)
                return null;

            var href = match.Groups["href"].Value.Trim();
            if (string.IsNullOrWhiteSpace(href))
                return null;

            return Uri.TryCreate(href, UriKind.Absolute, out var absolute)
                ? absolute.ToString()
                : new Uri(new Uri(pageUrl), href).ToString();
        }
        catch
        {
            return null;
        }
    }
}
