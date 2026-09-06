using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace CodexSwitch.Services;

public sealed class CodexOAuthJsonImportService
{
    private readonly CodexOAuthHelper _codexOAuthHelper;

    public CodexOAuthJsonImportService(CodexOAuthHelper codexOAuthHelper)
    {
        _codexOAuthHelper = codexOAuthHelper;
    }

    public CodexOAuthJsonImportResult Import(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidOperationException("Codex auth JSON is empty.");

        using var document = JsonDocument.Parse(json);
        var records = EnumerateRecords(document.RootElement).ToArray();
        if (records.Length == 0)
            throw new InvalidOperationException("Codex auth JSON did not contain any account records.");

        var accounts = new List<OAuthAccountConfig>();
        var skipped = new List<CodexOAuthJsonImportSkippedRecord>();

        foreach (var record in records)
        {
            try
            {
                accounts.Add(ParseRecord(record.Element));
            }
            catch (Exception ex) when (ex is InvalidOperationException or JsonException)
            {
                skipped.Add(new CodexOAuthJsonImportSkippedRecord(record.Index, ex.Message));
            }
        }

        return new CodexOAuthJsonImportResult(accounts, skipped);
    }

    private OAuthAccountConfig ParseRecord(JsonElement record)
    {
        if (record.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Account record must be a JSON object.");

        var tokenContainer = GetObject(record, "tokens") ?? record;
        var accessToken = ReadFirstString(tokenContainer, "access_token", "accessToken", "authorization_token",
                "authorizationToken", "auth_token", "authToken", "token", "OPENAI_API_KEY", "openai_api_key") ??
            ReadFirstString(record, "access_token", "accessToken", "authorization_token", "authorizationToken",
                "auth_token", "authToken", "token", "OPENAI_API_KEY", "openai_api_key");
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new InvalidOperationException("missing_access_token");

        var refreshToken = ReadFirstString(tokenContainer, "refresh_token", "refreshToken") ??
            ReadFirstString(record, "refresh_token", "refreshToken");
        var idToken = ReadFirstString(tokenContainer, "id_token", "idToken") ??
            ReadFirstString(record, "id_token", "idToken");
        var email = ReadFirstString(record, "email", "mail", "account_email", "accountEmail");
        var displayName = ReadFirstString(record, "display_name", "displayName", "label", "name", "nickname") ?? email;
        var planType = ReadFirstString(record, "chatgpt_plan_type", "chatgptPlanType", "plan_type", "planType", "plan");
        var chatgptAccountId = ReadFirstString(record, "chatgpt_account_id", "chatgptAccountId", "account_id",
            "accountId", "chatgpt_user_id", "chatgptUserId", "user_id", "userId");

        var account = new OAuthAccountConfig
        {
            AccessToken = accessToken.Trim(),
            RefreshToken = refreshToken?.Trim() ?? "",
            IdToken = idToken?.Trim(),
            Email = email?.Trim(),
            DisplayName = displayName?.Trim() ?? "",
            ExpiresAt = ReadExpiresAt(record, tokenContainer),
            IsEnabled = true
        };

        _codexOAuthHelper.EnrichAccountFromIdToken(account);

        if (string.IsNullOrWhiteSpace(account.Email) && !string.IsNullOrWhiteSpace(email))
            account.Email = email.Trim();
        if (!string.IsNullOrWhiteSpace(chatgptAccountId))
            account.ChatgptAccountId = chatgptAccountId.Trim();
        if (string.IsNullOrWhiteSpace(account.PlanType) && !string.IsNullOrWhiteSpace(planType))
            account.PlanType = planType.Trim();
        if (string.IsNullOrWhiteSpace(account.DisplayName))
            account.DisplayName = ProviderAuthService.ResolveAccountDisplayName(account);

        var idSource = FirstNonBlank(account.ChatgptAccountId, account.Email, account.RefreshToken, account.AccessToken);
        account.Id = CreateStableAccountId(idSource);
        return account;
    }

    private static IEnumerable<(int Index, JsonElement Element)> EnumerateRecords(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in root.EnumerateArray())
                yield return (index++, item);
            yield break;
        }

        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Codex auth JSON must be an object or an array.");

        if (root.TryGetProperty("accounts", out var accounts) && accounts.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in accounts.EnumerateArray())
                yield return (index++, item);
            yield break;
        }

        yield return (0, root);
    }

    private static JsonElement? GetObject(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out var value) &&
            value.ValueKind == JsonValueKind.Object
                ? value
                : null;
    }

    private static string? ReadFirstString(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            var value = ReadString(element, propertyName);
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    private static DateTimeOffset? ReadExpiresAt(JsonElement record, JsonElement tokenContainer)
    {
        var direct = ReadFirstString(tokenContainer, "expires_at", "expiresAt", "expired", "expire_at", "expireAt") ??
            ReadFirstString(record, "expires_at", "expiresAt", "expired", "expire_at", "expireAt");
        var parsedDirect = ParseTimestamp(direct);
        if (parsedDirect is not null)
            return parsedDirect;

        var expiresIn = ReadFirstString(tokenContainer, "expires_in", "expiresIn") ??
            ReadFirstString(record, "expires_in", "expiresIn");
        if (!double.TryParse(expiresIn, NumberStyles.Any, CultureInfo.InvariantCulture, out var seconds) ||
            seconds <= 0)
        {
            return null;
        }

        var lastRefresh = ParseTimestamp(ReadFirstString(record, "last_refresh", "lastRefresh") ??
            ReadFirstString(tokenContainer, "last_refresh", "lastRefresh"));
        return (lastRefresh ?? DateTimeOffset.UtcNow).AddSeconds(seconds);
    }

    private static DateTimeOffset? ParseTimestamp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        if (double.TryParse(trimmed, NumberStyles.Any, CultureInfo.InvariantCulture, out var number))
        {
            try
            {
                return number > 10_000_000_000
                    ? DateTimeOffset.FromUnixTimeMilliseconds((long)number)
                    : DateTimeOffset.FromUnixTimeSeconds((long)number);
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }

        return DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.ToUniversalTime()
            : null;
    }

    private static string FirstNonBlank(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? Guid.NewGuid().ToString("N");
    }

    private static string CreateStableAccountId(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim().ToLowerInvariant()));
        return "codex-" + Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }
}

public sealed record CodexOAuthJsonImportResult(
    IReadOnlyList<OAuthAccountConfig> Accounts,
    IReadOnlyList<CodexOAuthJsonImportSkippedRecord> Skipped);

public sealed record CodexOAuthJsonImportSkippedRecord(int Index, string Reason);
