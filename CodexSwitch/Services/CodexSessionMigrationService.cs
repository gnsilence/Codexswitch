using System.Text;
using Microsoft.Data.Sqlite;

namespace CodexSwitch.Services;

public sealed class CodexSessionMigrationService
{
    private const int SqliteTimeoutSeconds = 5;
    private const string OriginalModelProviderPropertyName = "codexswitch_original_model_provider";
    private const string ThreadProviderBackupTableName = "codexswitch_thread_provider_backup";
    private const string LegacyThreadProviderBackupTableName = "codexswitch_thread_provider_backup_legacy";
    private static readonly string[] SessionDirectoryNames = ["sessions", "archived_sessions"];
    private readonly AppPaths _paths;
    private readonly Action<string, string> _replaceFile;

    public CodexSessionMigrationService(AppPaths paths)
        : this(paths, ReplaceFile)
    {
    }

    internal CodexSessionMigrationService(AppPaths paths, Action<string, string> replaceFile)
    {
        _paths = paths;
        _replaceFile = replaceFile;
    }

    public CodexSessionInspection Inspect()
    {
        var files = LoadSessionFileRecords();
        var fileCounts = files
            .GroupBy(file => file.ModelProvider, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var indexCounts = TryLoadThreadIndexCounts(out var indexStatus)
            .GroupBy(summary => summary.ModelProvider.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.First().ModelProvider.Trim(),
                group => group.Sum(summary => summary.ThreadIndexCount),
                StringComparer.OrdinalIgnoreCase);
        var restorableThreadIndexCount = 0;
        var repairableSessionCount = 0;
        if (string.IsNullOrWhiteSpace(indexStatus))
        {
            repairableSessionCount = TryLoadRepairableSessionCount(files, out var repairIndexStatus);
            indexStatus = repairIndexStatus;
        }
        if (string.IsNullOrWhiteSpace(indexStatus))
        {
            restorableThreadIndexCount = TryLoadThreadBackupCount(out var backupIndexStatus);
            indexStatus = backupIndexStatus;
        }

        var providerIds = fileCounts.Keys
            .Concat(indexCounts.Keys)
            .Where(provider => !string.IsNullOrWhiteSpace(provider))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(provider => string.Equals(provider, CodexConfigWriter.ManagedProviderId, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(provider => provider, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var summaries = providerIds
            .Select(provider => new CodexSessionProviderSummary(
                provider,
                fileCounts.GetValueOrDefault(provider),
                indexCounts.GetValueOrDefault(provider),
                string.Equals(provider, CodexConfigWriter.ManagedProviderId, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        return new CodexSessionInspection(
            _paths.CodexDirectory,
            StateDatabasePath,
            CodexConfigWriter.ManagedProviderId,
            summaries,
            indexStatus,
            files.Count(CanRestoreSessionFile),
            restorableThreadIndexCount,
            repairableSessionCount);
    }

    public CodexSessionMigrationResult MigrateToManagedProvider()
    {
        var files = LoadSessionFileRecords();
        if (!File.Exists(StateDatabasePath))
        {
            return files.All(file =>
                    string.Equals(file.ModelProvider, CodexConfigWriter.ManagedProviderId, StringComparison.OrdinalIgnoreCase))
                ? new CodexSessionMigrationResult(true, 0, 0, null, [])
                : new CodexSessionMigrationResult(
                    false,
                    0,
                    0,
                    "state-db-missing",
                    files
                        .Where(file => !string.Equals(
                            file.ModelProvider,
                            CodexConfigWriter.ManagedProviderId,
                            StringComparison.OrdinalIgnoreCase))
                        .Select(file => file.Path)
                        .ToArray());
        }

        return ExecuteMigration(
            files,
            BuildManagedProviderPlans,
            UpdateThreadIndex);
    }

    public CodexSessionRestoreResult RestoreOriginalProviders()
    {
        var files = LoadSessionFileRecords();
        if (!File.Exists(StateDatabasePath))
        {
            return files.All(file => !CanRestoreSessionFile(file))
                ? new CodexSessionRestoreResult(true, 0, 0, null, [])
                : new CodexSessionRestoreResult(
                    false,
                    0,
                    0,
                    "state-db-missing",
                    files.Where(CanRestoreSessionFile).Select(file => file.Path).ToArray());
        }

        var result = ExecuteMigration(
            files,
            BuildRestorePlans,
            RestoreThreadIndex);
        return new CodexSessionRestoreResult(
            result.Succeeded,
            result.UpdatedSessionFiles,
            result.UpdatedThreadIndexEntries,
            result.StateIndexStatus,
            result.FailedFiles);
    }

    private string StateDatabasePath => Path.Combine(_paths.CodexDirectory, "state_5.sqlite");

    private CodexSessionMigrationResult ExecuteMigration(
        IReadOnlyList<CodexSessionFileRecord> files,
        Func<
            SqliteConnection,
            SqliteTransaction,
            IReadOnlyList<CodexSessionFileRecord>,
            IReadOnlyList<SessionRewritePlan>> buildPlans,
        Func<SqliteConnection, SqliteTransaction, int> updateIndex)
    {
        var preparedFiles = new List<PreparedSessionFile>(files.Count);
        var replacedFiles = new List<PreparedSessionFile>(files.Count);
        try
        {
            using var connection = OpenStateDatabase(readOnly: false);
            using var transaction = connection.BeginTransaction();
            EnsureBackupTable(connection, transaction);
            var plans = buildPlans(connection, transaction, files);
            foreach (var plan in plans)
            {
                preparedFiles.Add(PrepareSessionFile(
                    plan.File.Path,
                    plan.TargetProvider,
                    plan.OriginalProvider,
                    plan.RemoveOriginalProvider));
            }

            var updatedThreadEntries = updateIndex(connection, transaction);

            foreach (var preparedFile in preparedFiles)
            {
                _replaceFile(preparedFile.UpdatedPath, preparedFile.Path);
                replacedFiles.Add(preparedFile);
            }

            transaction.Commit();
            CleanupPreparedFiles(preparedFiles);
            return new CodexSessionMigrationResult(
                true,
                replacedFiles.Count,
                updatedThreadEntries,
                null,
                []);
        }
        catch (Exception ex) when (IsMigrationException(ex))
        {
            var rollbackFailedFiles = RollBackSessionFiles(replacedFiles);
            CleanupPreparedFiles(preparedFiles, rollbackFailedFiles);
            var failedFiles = rollbackFailedFiles.Count > 0
                ? rollbackFailedFiles
                : preparedFiles
                    .Where(file => !replacedFiles.Contains(file))
                    .Take(1)
                    .Select(file => file.Path)
                    .ToArray();
            return new CodexSessionMigrationResult(
                false,
                0,
                0,
                rollbackFailedFiles.Count > 0 ? "rollback-failed" : GetMigrationFailureStatus(ex),
                failedFiles);
        }
    }

    private static IReadOnlyList<SessionRewritePlan> BuildManagedProviderPlans(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<CodexSessionFileRecord> files)
    {
        var threadProviders = LoadThreadProviders(connection, transaction);
        var backupProviders = LoadThreadBackupProviders(connection, transaction);
        var plans = new List<SessionRewritePlan>();
        foreach (var file in files)
        {
            var fileIsManaged = string.Equals(
                file.ModelProvider,
                CodexConfigWriter.ManagedProviderId,
                StringComparison.OrdinalIgnoreCase);
            var indexHasOriginalProvider =
                threadProviders.TryGetValue(file.SessionId, out var indexProvider) &&
                !string.Equals(
                    indexProvider,
                    CodexConfigWriter.ManagedProviderId,
                    StringComparison.OrdinalIgnoreCase);
            if (!fileIsManaged &&
                threadProviders.TryGetValue(file.SessionId, out indexProvider) &&
                !string.Equals(
                    indexProvider,
                    CodexConfigWriter.ManagedProviderId,
                    StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(
                    file.ModelProvider,
                    indexProvider,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new SessionIndexMismatchException([file.SessionId]);
            }

            if (indexHasOriginalProvider &&
                !string.IsNullOrWhiteSpace(file.OriginalModelProvider) &&
                !string.Equals(
                    file.OriginalModelProvider,
                    indexProvider,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new SessionIndexMismatchException([file.SessionId]);
            }

            backupProviders.TryGetValue(file.SessionId, out var backupProvider);
            var originalProvider = file.OriginalModelProvider ??
                (indexHasOriginalProvider ? indexProvider : null) ??
                backupProvider ??
                (fileIsManaged ? null : file.ModelProvider);
            if (!string.IsNullOrWhiteSpace(backupProvider) &&
                !string.IsNullOrWhiteSpace(originalProvider) &&
                !string.Equals(
                    backupProvider,
                    originalProvider,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new SessionIndexMismatchException([file.SessionId]);
            }

            if (fileIsManaged &&
                (string.IsNullOrWhiteSpace(originalProvider) ||
                    !string.IsNullOrWhiteSpace(file.OriginalModelProvider)))
            {
                if (!string.IsNullOrWhiteSpace(originalProvider))
                {
                    InsertThreadBackup(
                        connection,
                        transaction,
                        file.SessionId,
                        originalProvider);
                }
                continue;
            }

            if (!string.IsNullOrWhiteSpace(originalProvider))
            {
                InsertThreadBackup(
                    connection,
                    transaction,
                    file.SessionId,
                    originalProvider);
            }

            plans.Add(new SessionRewritePlan(
                file,
                CodexConfigWriter.ManagedProviderId,
                originalProvider,
                RemoveOriginalProvider: false));
        }

        return plans;
    }

    private static IReadOnlyList<SessionRewritePlan> BuildRestorePlans(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<CodexSessionFileRecord> files)
    {
        var threadProviders = LoadThreadProviders(connection, transaction);
        var backupProviders = LoadThreadBackupProviders(connection, transaction);

        var plans = new List<SessionRewritePlan>();
        foreach (var file in files)
        {
            if (!string.Equals(
                    file.ModelProvider,
                    CodexConfigWriter.ManagedProviderId,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var originalProvider = file.OriginalModelProvider;
            backupProviders.TryGetValue(file.SessionId, out var backupProvider);
            if (!string.IsNullOrWhiteSpace(originalProvider) &&
                !string.IsNullOrWhiteSpace(backupProvider) &&
                !string.Equals(
                    originalProvider,
                    backupProvider,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new SessionIndexMismatchException([file.SessionId]);
            }

            if (string.IsNullOrWhiteSpace(originalProvider))
                originalProvider = backupProvider;
            if (string.IsNullOrWhiteSpace(originalProvider) ||
                string.Equals(
                    originalProvider,
                    CodexConfigWriter.ManagedProviderId,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            plans.Add(new SessionRewritePlan(
                file,
                originalProvider,
                OriginalProvider: null,
                RemoveOriginalProvider: true));

            if (threadProviders.TryGetValue(file.SessionId, out var indexProvider) &&
                string.Equals(
                    indexProvider,
                    CodexConfigWriter.ManagedProviderId,
                    StringComparison.OrdinalIgnoreCase))
            {
                InsertThreadBackup(
                    connection,
                    transaction,
                    file.SessionId,
                    originalProvider);
            }
        }

        return plans;
    }

    private IReadOnlyList<CodexSessionFileRecord> LoadSessionFileRecords()
    {
        if (!Directory.Exists(_paths.CodexDirectory))
            return [];

        var records = new List<CodexSessionFileRecord>();
        foreach (var directoryName in SessionDirectoryNames)
        {
            var directory = Path.Combine(_paths.CodexDirectory, directoryName);
            if (!Directory.Exists(directory))
                continue;

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(directory, "rollout-*.jsonl", SearchOption.AllDirectories);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files)
            {
                if (TryReadSessionFileProvider(
                    file,
                    out var sessionId,
                    out var provider,
                    out var originalProvider))
                {
                    records.Add(new CodexSessionFileRecord(
                        file,
                        sessionId,
                        provider,
                        originalProvider));
                }
            }
        }

        return records;
    }

    private static bool TryReadSessionFileProvider(
        string path,
        out string sessionId,
        out string provider,
        out string? originalProvider)
    {
        sessionId = "";
        provider = "";
        originalProvider = null;
        try
        {
            using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var firstLine = reader.ReadLine();
            if (string.IsNullOrWhiteSpace(firstLine))
                return false;

            using var document = JsonDocument.Parse(firstLine);
            var root = document.RootElement;
            if (!IsSessionMeta(root) ||
                !root.TryGetProperty("payload", out var payload) ||
                payload.ValueKind != JsonValueKind.Object ||
                !payload.TryGetProperty("id", out var id) ||
                id.ValueKind != JsonValueKind.String ||
                !payload.TryGetProperty("model_provider", out var modelProvider) ||
                modelProvider.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            sessionId = id.GetString()?.Trim() ?? "";
            provider = modelProvider.GetString()?.Trim() ?? "";
            if (payload.TryGetProperty(OriginalModelProviderPropertyName, out var originalModelProvider) &&
                originalModelProvider.ValueKind == JsonValueKind.String)
            {
                originalProvider = originalModelProvider.GetString()?.Trim();
            }

            return !string.IsNullOrWhiteSpace(sessionId) && !string.IsNullOrWhiteSpace(provider);
        }
        catch (Exception ex) when (IsSessionFileException(ex))
        {
            return false;
        }
    }

    private static PreparedSessionFile PrepareSessionFile(
        string path,
        string provider,
        string? originalProvider,
        bool removeOriginalProvider)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var updatedPath = path + "." + suffix + ".codexswitch.tmp";
        var backupPath = path + "." + suffix + ".codexswitch.bak";
        try
        {
            File.Copy(path, backupPath);
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var output = new FileStream(updatedPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            var firstLineBytes = ReadFirstLine(input);
            var (contentOffset, contentLength, newlineOffset) = GetFirstLineLayout(firstLineBytes);
            var firstLine = Encoding.UTF8.GetString(firstLineBytes, contentOffset, contentLength);
            using var document = JsonDocument.Parse(firstLine);
            if (!IsSessionMeta(document.RootElement))
                throw new JsonException("The first line is not session_meta.");

            if (contentOffset > 0)
                output.Write(firstLineBytes, 0, contentOffset);
            var rewritten = Encoding.UTF8.GetBytes(RewriteSessionMetaProvider(
                document.RootElement,
                provider,
                originalProvider,
                removeOriginalProvider));
            output.Write(rewritten);
            if (newlineOffset < firstLineBytes.Length)
                output.Write(firstLineBytes, newlineOffset, firstLineBytes.Length - newlineOffset);
            input.CopyTo(output);
            output.Flush(flushToDisk: true);
            return new PreparedSessionFile(path, updatedPath, backupPath);
        }
        catch
        {
            DeleteIfExists(updatedPath);
            DeleteIfExists(backupPath);
            throw;
        }
    }

    private static byte[] ReadFirstLine(Stream input)
    {
        using var buffer = new MemoryStream();
        while (true)
        {
            var value = input.ReadByte();
            if (value < 0)
                break;

            buffer.WriteByte((byte)value);
            if (value == '\n')
                break;
        }

        if (buffer.Length == 0)
            throw new JsonException("The session file is empty.");

        return buffer.ToArray();
    }

    private static (int ContentOffset, int ContentLength, int NewlineOffset) GetFirstLineLayout(byte[] bytes)
    {
        var contentOffset = bytes.Length >= 3 &&
            bytes[0] == 0xEF &&
            bytes[1] == 0xBB &&
            bytes[2] == 0xBF
                ? 3
                : 0;
        var newlineOffset = bytes.Length;
        if (bytes[^1] == '\n')
        {
            newlineOffset = bytes.Length - 1;
            if (newlineOffset > contentOffset && bytes[newlineOffset - 1] == '\r')
                newlineOffset--;
        }

        return (contentOffset, newlineOffset - contentOffset, newlineOffset);
    }

    private static string RewriteSessionMetaProvider(
        JsonElement root,
        string provider,
        string? originalProvider,
        bool removeOriginalProvider)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var property in root.EnumerateObject())
            {
                if (property.NameEquals("payload") && property.Value.ValueKind == JsonValueKind.Object)
                {
                    writer.WritePropertyName(property.Name);
                    WritePayloadWithProvider(writer, property.Value, provider, originalProvider, removeOriginalProvider);
                    continue;
                }

                property.WriteTo(writer);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WritePayloadWithProvider(
        Utf8JsonWriter writer,
        JsonElement payload,
        string provider,
        string? originalProvider,
        bool removeOriginalProvider)
    {
        var wroteProvider = false;
        var wroteOriginalProvider = false;
        writer.WriteStartObject();
        foreach (var property in payload.EnumerateObject())
        {
            if (property.NameEquals("model_provider"))
            {
                writer.WriteString(property.Name, provider);
                wroteProvider = true;
                continue;
            }

            if (property.NameEquals(OriginalModelProviderPropertyName))
            {
                if (!removeOriginalProvider && !string.IsNullOrWhiteSpace(originalProvider))
                {
                    writer.WriteString(property.Name, originalProvider);
                    wroteOriginalProvider = true;
                }

                continue;
            }

            property.WriteTo(writer);
        }

        if (!wroteProvider)
            writer.WriteString("model_provider", provider);
        if (!removeOriginalProvider &&
            !wroteOriginalProvider &&
            !string.IsNullOrWhiteSpace(originalProvider))
        {
            writer.WriteString(OriginalModelProviderPropertyName, originalProvider);
        }

        writer.WriteEndObject();
    }

    private static bool IsSessionMeta(JsonElement root)
    {
        return root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("type", out var type) &&
            type.ValueKind == JsonValueKind.String &&
            string.Equals(type.GetString(), "session_meta", StringComparison.Ordinal);
    }

    private static bool CanRestoreSessionFile(CodexSessionFileRecord file)
    {
        return string.Equals(file.ModelProvider, CodexConfigWriter.ManagedProviderId, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(file.OriginalModelProvider) &&
            !string.Equals(file.OriginalModelProvider, CodexConfigWriter.ManagedProviderId, StringComparison.OrdinalIgnoreCase);
    }

    private IReadOnlyList<CodexSessionProviderSummary> TryLoadThreadIndexCounts(out string? status)
    {
        status = null;
        if (!File.Exists(StateDatabasePath))
        {
            status = "state-db-missing";
            return [];
        }

        try
        {
            using var connection = OpenStateDatabase(readOnly: true);
            using var command = CreateCommand(
                connection,
                null,
                "select model_provider, count(*) from threads group by model_provider;");
            using var reader = command.ExecuteReader();
            var summaries = new List<CodexSessionProviderSummary>();
            while (reader.Read())
            {
                var provider = reader.GetString(0).Trim();
                if (provider.Length == 0)
                    continue;

                summaries.Add(new CodexSessionProviderSummary(
                    provider,
                    SessionFileCount: 0,
                    ThreadIndexCount: reader.GetInt32(1),
                    IsManagedProvider: string.Equals(provider, CodexConfigWriter.ManagedProviderId, StringComparison.OrdinalIgnoreCase)));
            }

            return summaries;
        }
        catch (Exception ex) when (IsMigrationException(ex))
        {
            status = GetMigrationFailureStatus(ex);
            return [];
        }
    }

    private int TryLoadThreadBackupCount(out string? status)
    {
        status = null;
        if (!File.Exists(StateDatabasePath))
        {
            status = "state-db-missing";
            return 0;
        }

        try
        {
            using var connection = OpenStateDatabase(readOnly: true);
            if (!TableExists(connection, null, ThreadProviderBackupTableName))
                return 0;

            using var command = CreateCommand(
                connection,
                null,
                $"select count(*) from {ThreadProviderBackupTableName};");
            return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (IsMigrationException(ex))
        {
            status = GetMigrationFailureStatus(ex);
            return 0;
        }
    }

    private int TryLoadRepairableSessionCount(
        IReadOnlyList<CodexSessionFileRecord> files,
        out string? status)
    {
        status = null;
        if (!File.Exists(StateDatabasePath))
        {
            status = "state-db-missing";
            return 0;
        }

        try
        {
            using var connection = OpenStateDatabase(readOnly: true);
            var threadProviders = LoadThreadProviders(connection, null);
            var backupProviders = TableExists(connection, null, ThreadProviderBackupTableName)
                ? LoadThreadBackupProviders(connection, null)
                : new Dictionary<string, string>(StringComparer.Ordinal);
            var repairableSessionIds = new HashSet<string>(StringComparer.Ordinal);
            var sessionFileIds = files
                .Select(file => file.SessionId)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var file in files)
            {
                if (!threadProviders.TryGetValue(file.SessionId, out var indexProvider))
                    continue;

                var fileIsManaged = string.Equals(
                    file.ModelProvider,
                    CodexConfigWriter.ManagedProviderId,
                    StringComparison.OrdinalIgnoreCase);
                var indexIsManaged = string.Equals(
                    indexProvider,
                    CodexConfigWriter.ManagedProviderId,
                    StringComparison.OrdinalIgnoreCase);
                backupProviders.TryGetValue(file.SessionId, out var backupProvider);
                var hasFileOriginalProvider = !string.IsNullOrWhiteSpace(file.OriginalModelProvider);
                var hasBackupProvider = !string.IsNullOrWhiteSpace(backupProvider);

                if (fileIsManaged != indexIsManaged ||
                    (fileIsManaged &&
                     indexIsManaged &&
                     (hasFileOriginalProvider != hasBackupProvider ||
                      hasFileOriginalProvider &&
                      !string.Equals(
                          file.OriginalModelProvider,
                          backupProvider,
                          StringComparison.OrdinalIgnoreCase))) ||
                    (!fileIsManaged && !indexIsManaged && hasBackupProvider))
                {
                    repairableSessionIds.Add(file.SessionId);
                }
            }

            foreach (var thread in threadProviders)
            {
                if (!sessionFileIds.Contains(thread.Key) &&
                    !string.Equals(
                        thread.Value,
                        CodexConfigWriter.ManagedProviderId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    repairableSessionIds.Add(thread.Key);
                }
            }

            return repairableSessionIds.Count;
        }
        catch (Exception ex) when (IsMigrationException(ex))
        {
            status = GetMigrationFailureStatus(ex);
            return 0;
        }
    }

    private SqliteConnection OpenStateDatabase(bool readOnly)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = StateDatabasePath,
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWrite,
            Cache = SqliteCacheMode.Private,
            DefaultTimeout = SqliteTimeoutSeconds,
            Pooling = false
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"pragma busy_timeout = {SqliteTimeoutSeconds * 1000};";
        command.ExecuteNonQuery();
        return connection;
    }

    private static void EnsureBackupTable(SqliteConnection connection, SqliteTransaction transaction)
    {
        if (!TableExists(connection, transaction, ThreadProviderBackupTableName))
        {
            CreateBackupTable(connection, transaction);
            return;
        }

        var columns = LoadTableColumns(connection, transaction, ThreadProviderBackupTableName);
        if (columns.Contains("thread_id") && columns.Contains("model_provider"))
            return;
        if (!columns.Contains("thread_rowid") || !columns.Contains("model_provider"))
            throw new InvalidDataException("Unsupported CodexSwitch provider backup table schema.");
        if (TableExists(connection, transaction, LegacyThreadProviderBackupTableName))
            throw new InvalidDataException("An incomplete provider backup table migration was found.");

        ExecuteNonQuery(
            connection,
            transaction,
            $"alter table {ThreadProviderBackupTableName} rename to {LegacyThreadProviderBackupTableName};");
        CreateBackupTable(connection, transaction);
        ExecuteNonQuery(
            connection,
            transaction,
            $"""
             insert or ignore into {ThreadProviderBackupTableName}(thread_id, model_provider)
             select threads.id, legacy.model_provider
             from {LegacyThreadProviderBackupTableName} legacy
             join threads on threads.rowid = legacy.thread_rowid;
             """);
        ExecuteNonQuery(connection, transaction, $"drop table {LegacyThreadProviderBackupTableName};");
    }

    private static void CreateBackupTable(SqliteConnection connection, SqliteTransaction transaction)
    {
        ExecuteNonQuery(
            connection,
            transaction,
            $"""
             -- Stores the original provider for each Codex thread migrated by CodexSwitch.
             create table {ThreadProviderBackupTableName} (
                 -- Stable Codex thread identifier from threads.id.
                 thread_id text primary key,
                 -- Provider identifier used before migration to meteor-ai.
                 model_provider text not null
             );
             """);
    }

    private static int UpdateThreadIndex(SqliteConnection connection, SqliteTransaction transaction)
    {
        using (var backup = CreateCommand(
                   connection,
                   transaction,
                   $"""
                    insert or ignore into {ThreadProviderBackupTableName}(thread_id, model_provider)
                    select id, model_provider
                    from threads
                    where model_provider <> $managed collate nocase;
                    """))
        {
            backup.Parameters.AddWithValue("$managed", CodexConfigWriter.ManagedProviderId);
            backup.ExecuteNonQuery();
        }

        using var update = CreateCommand(
            connection,
            transaction,
            "update threads set model_provider = $managed where model_provider <> $managed collate nocase;");
        update.Parameters.AddWithValue("$managed", CodexConfigWriter.ManagedProviderId);
        return update.ExecuteNonQuery();
    }

    private static Dictionary<string, string> LoadThreadProviders(
        SqliteConnection connection,
        SqliteTransaction? transaction)
    {
        using var command = CreateCommand(
            connection,
            transaction,
            "select id, model_provider from threads;");
        using var reader = command.ExecuteReader();
        var providers = new Dictionary<string, string>(StringComparer.Ordinal);
        while (reader.Read())
            providers[reader.GetString(0)] = reader.GetString(1);
        return providers;
    }

    private static Dictionary<string, string> LoadThreadBackupProviders(
        SqliteConnection connection,
        SqliteTransaction? transaction)
    {
        using var command = CreateCommand(
            connection,
            transaction,
            $"select thread_id, model_provider from {ThreadProviderBackupTableName};");
        using var reader = command.ExecuteReader();
        var providers = new Dictionary<string, string>(StringComparer.Ordinal);
        while (reader.Read())
            providers[reader.GetString(0)] = reader.GetString(1);
        return providers;
    }

    private static void InsertThreadBackup(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string threadId,
        string modelProvider)
    {
        using var command = CreateCommand(
            connection,
            transaction,
            $"""
             insert or ignore into {ThreadProviderBackupTableName}(thread_id, model_provider)
             values ($threadId, $modelProvider);
             """);
        command.Parameters.AddWithValue("$threadId", threadId);
        command.Parameters.AddWithValue("$modelProvider", modelProvider);
        command.ExecuteNonQuery();
    }

    private static int RestoreThreadIndex(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var update = CreateCommand(
            connection,
            transaction,
            $"""
             update threads
             set model_provider = (
                 select backup.model_provider
                 from {ThreadProviderBackupTableName} backup
                 where backup.thread_id = threads.id
             )
             where model_provider = $managed collate nocase
                 and id in (select thread_id from {ThreadProviderBackupTableName});
             """);
        update.Parameters.AddWithValue("$managed", CodexConfigWriter.ManagedProviderId);
        var changed = update.ExecuteNonQuery();
        ExecuteNonQuery(
            connection,
            transaction,
            $"""
             delete from {ThreadProviderBackupTableName}
             where thread_id not in (select id from threads)
                 or thread_id in (
                     select id
                     from threads
                     where model_provider <> $managed collate nocase
                 );
             """,
            ("$managed", CodexConfigWriter.ManagedProviderId));
        return changed;
    }

    private static bool TableExists(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string tableName)
    {
        using var command = CreateCommand(
            connection,
            transaction,
            "select count(*) from sqlite_master where type = 'table' and name = $name;");
        command.Parameters.AddWithValue("$name", tableName);
        return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) > 0;
    }

    private static HashSet<string> LoadTableColumns(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string tableName)
    {
        using var command = CreateCommand(connection, transaction, $"pragma table_info({tableName});");
        using var reader = command.ExecuteReader();
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
            columns.Add(reader.GetString(1));
        return columns;
    }

    private static SqliteCommand CreateCommand(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string commandText)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = SqliteTimeoutSeconds;
        command.CommandText = commandText;
        return command;
    }

    private static void ExecuteNonQuery(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string commandText,
        params (string Name, object Value)[] parameters)
    {
        using var command = CreateCommand(connection, transaction, commandText);
        foreach (var parameter in parameters)
            command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        command.ExecuteNonQuery();
    }

    private static IReadOnlyList<string> RollBackSessionFiles(IReadOnlyList<PreparedSessionFile> replacedFiles)
    {
        var failedFiles = new List<string>();
        for (var index = replacedFiles.Count - 1; index >= 0; index--)
        {
            var file = replacedFiles[index];
            try
            {
                ReplaceFile(file.BackupPath, file.Path);
            }
            catch (Exception ex) when (IsSessionFileException(ex))
            {
                failedFiles.Add(file.Path);
            }
        }

        return failedFiles;
    }

    private static void CleanupPreparedFiles(
        IEnumerable<PreparedSessionFile> preparedFiles,
        IReadOnlyCollection<string>? preserveBackupForPaths = null)
    {
        foreach (var file in preparedFiles)
        {
            DeleteIfExists(file.UpdatedPath);
            if (preserveBackupForPaths is null ||
                !preserveBackupForPaths.Contains(file.Path, StringComparer.Ordinal))
            {
                DeleteIfExists(file.BackupPath);
            }
        }
    }

    private static void ReplaceFile(string sourcePath, string destinationPath)
    {
        File.Move(sourcePath, destinationPath, overwrite: true);
    }

    private static void DeleteIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (IsSessionFileException(ex))
        {
        }
    }

    private static bool IsSessionFileException(Exception ex)
    {
        return ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException;
    }

    private static bool IsMigrationException(Exception ex)
    {
        return IsSessionFileException(ex) ||
            ex is SqliteException or InvalidOperationException or SessionIndexMismatchException;
    }

    private static string GetMigrationFailureStatus(Exception ex)
    {
        if (ex is SqliteException { SqliteErrorCode: 5 or 6 })
            return "state-db-locked";
        if (ex is SqliteException)
            return "state-db-failed";
        if (ex is SessionIndexMismatchException)
            return "session-index-mismatch";
        if (ex is IOException or UnauthorizedAccessException or JsonException)
            return "session-file-failed";
        return "migration-failed";
    }

    private sealed record CodexSessionFileRecord(
        string Path,
        string SessionId,
        string ModelProvider,
        string? OriginalModelProvider);

    private sealed record SessionRewritePlan(
        CodexSessionFileRecord File,
        string TargetProvider,
        string? OriginalProvider,
        bool RemoveOriginalProvider);

    private sealed record PreparedSessionFile(string Path, string UpdatedPath, string BackupPath);

    private sealed class SessionIndexMismatchException : Exception
    {
        public SessionIndexMismatchException(IReadOnlyList<string> sessionIds)
            : base("Session files were not found for indexed threads: " + string.Join(", ", sessionIds))
        {
        }
    }
}

public sealed record CodexSessionInspection(
    string CodexDirectory,
    string StateDatabasePath,
    string ManagedModelProvider,
    IReadOnlyList<CodexSessionProviderSummary> Providers,
    string? StateIndexStatus,
    int RestorableSessionFileCount,
    int RestorableThreadIndexCount,
    int RepairableSessionCount)
{
    public int TotalSessionFileCount => Providers.Sum(provider => provider.SessionFileCount);

    public int ManagedSessionFileCount => Providers
        .Where(provider => provider.IsManagedProvider)
        .Sum(provider => provider.SessionFileCount);

    public int MigratableSessionFileCount => Providers
        .Where(provider => !provider.IsManagedProvider)
        .Sum(provider => provider.SessionFileCount);
}

public sealed record CodexSessionProviderSummary(
    string ModelProvider,
    int SessionFileCount,
    int ThreadIndexCount,
    bool IsManagedProvider);

public sealed record CodexSessionMigrationResult(
    bool Succeeded,
    int UpdatedSessionFiles,
    int UpdatedThreadIndexEntries,
    string? StateIndexStatus,
    IReadOnlyList<string> FailedFiles);

public sealed record CodexSessionRestoreResult(
    bool Succeeded,
    int RestoredSessionFiles,
    int RestoredThreadIndexEntries,
    string? StateIndexStatus,
    IReadOnlyList<string> FailedFiles);
