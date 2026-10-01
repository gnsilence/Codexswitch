using System.Globalization;
using System.Text;

namespace CodexSwitch.Services;

public sealed class UsageLogMaintenanceService
{
    private readonly AppPaths _paths;
    private readonly UsageLogReader _reader;

    public UsageLogMaintenanceService(AppPaths paths, UsageLogReader reader)
    {
        _paths = paths;
        _reader = reader;
    }

    public UsageLogStorageInfo GetStorageInfo()
    {
        var files = EnumerateFiles().ToArray();
        var bytes = 0L;
        foreach (var path in files)
        {
            try
            {
                bytes += new FileInfo(path).Length;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        return new UsageLogStorageInfo(files.Length, bytes);
    }

    public int Prune(int retentionDays, DateTimeOffset? now = null)
    {
        var cutoff = (now ?? DateTimeOffset.UtcNow).AddDays(-Math.Clamp(retentionDays, 1, 3650));
        var deleted = 0;
        foreach (var path in EnumerateFiles())
        {
            var date = ParsePartitionDate(path) ?? TryGetLastWriteTime(path);
            if (date is null || date.Value > cutoff)
                continue;

            try
            {
                File.Delete(path);
                deleted++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        return deleted;
    }

    public void ExportJson(string destinationPath)
    {
        WriteAtomically(
            destinationPath,
            JsonSerializer.Serialize(
                _reader.ReadAllRecords().ToArray(),
                CodexSwitchJsonContext.Default.UsageLogRecordArray) + Environment.NewLine);
    }

    public void ExportCsv(string destinationPath)
    {
        var builder = new StringBuilder();
        builder.AppendLine(
            "timestamp,request_id,client_app,provider_id,protocol,request_model,billed_model,stream," +
            "input_tokens,cached_input_tokens,cache_creation_input_tokens,output_tokens,reasoning_output_tokens," +
            "estimated_cost,duration_ms,upstream_duration_ms,retry_count,final_provider,conversion_stage,status_code,error");

        foreach (var record in _reader.ReadAllRecords())
        {
            string[] columns =
            [
                record.Timestamp.ToString("O", CultureInfo.InvariantCulture),
                record.RequestId,
                record.ClientApp.ToString(),
                record.ProviderId,
                record.Protocol,
                record.RequestModel,
                record.BilledModel,
                record.Stream.ToString(),
                record.Usage.InputTokens.ToString(CultureInfo.InvariantCulture),
                record.Usage.CachedInputTokens.ToString(CultureInfo.InvariantCulture),
                record.Usage.CacheCreationInputTokens.ToString(CultureInfo.InvariantCulture),
                record.Usage.OutputTokens.ToString(CultureInfo.InvariantCulture),
                record.Usage.ReasoningOutputTokens.ToString(CultureInfo.InvariantCulture),
                record.EstimatedCost.ToString(CultureInfo.InvariantCulture),
                record.DurationMs.ToString(CultureInfo.InvariantCulture),
                record.UpstreamDurationMs.ToString(CultureInfo.InvariantCulture),
                record.RetryCount.ToString(CultureInfo.InvariantCulture),
                record.FinalProvider,
                record.ConversionStage,
                record.StatusCode.ToString(CultureInfo.InvariantCulture),
                record.Error ?? ""
            ];
            builder.AppendLine(string.Join(",", columns.Select(EscapeCsv)));
        }

        WriteAtomically(destinationPath, builder.ToString());
    }

    private IEnumerable<string> EnumerateFiles()
    {
        if (File.Exists(_paths.UsageLogPath))
            yield return _paths.UsageLogPath;

        if (!Directory.Exists(_paths.UsageLogDirectory))
            yield break;

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(
                _paths.UsageLogDirectory,
                UsageLogFileLayout.PartitionSearchPattern,
                SearchOption.AllDirectories);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var file in files)
            yield return file;
    }

    private static DateTimeOffset? ParsePartitionDate(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        if (!name.StartsWith("usage-", StringComparison.OrdinalIgnoreCase) ||
            !DateTime.TryParseExact(
                name["usage-".Length..],
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal,
                out var value))
        {
            return null;
        }

        return new DateTimeOffset(value);
    }

    private static DateTimeOffset? TryGetLastWriteTime(string path)
    {
        try
        {
            return File.GetLastWriteTimeUtc(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string EscapeCsv(string value)
    {
        if (value.IndexOfAny([',', '"', '\r', '\n']) < 0)
            return value;
        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private static void WriteAtomically(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory))
            throw new ArgumentException("Destination path must include a directory.", nameof(path));
        Directory.CreateDirectory(directory);
        var tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(tempPath, content, TextFileEncoding.Utf8NoBom);
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }
}

public sealed record UsageLogStorageInfo(int FileCount, long Bytes);
