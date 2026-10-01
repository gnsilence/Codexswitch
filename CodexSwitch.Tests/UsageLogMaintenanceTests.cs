using System.Globalization;
using System.Text;
using CodexSwitch.Models;
using CodexSwitch.Proxy;
using CodexSwitch.Services;

namespace CodexSwitch.Tests;

public sealed class UsageLogMaintenanceTests
{
    [Fact]
    public void ExportCsvAndJson_IncludeStructuredLogFields()
    {
        var root = CreateTempDirectory();
        try
        {
            var paths = new AppPaths(root, Path.Combine(root, ".codex"));
            var writer = new UsageLogWriter(paths);
            writer.Append(new UsageLogRecord
            {
                Timestamp = DateTimeOffset.UtcNow,
                RequestId = "cs-request",
                ProviderId = "provider-a",
                RequestModel = "gpt-6.1-sol",
                BilledModel = "gpt-6.1-sol",
                Usage = new UsageTokens(10, 2, 1, 5, 3),
                EstimatedCost = 0.12m,
                DurationMs = 42,
                RetryCount = 1,
                StatusCode = 200,
                Error = null
            });

            var maintenance = new UsageLogMaintenanceService(paths, new UsageLogReader(paths));
            var jsonPath = Path.Combine(root, "export.json");
            var csvPath = Path.Combine(root, "export.csv");
            maintenance.ExportJson(jsonPath);
            maintenance.ExportCsv(csvPath);

            var json = File.ReadAllText(jsonPath);
            var csv = File.ReadAllText(csvPath);
            Assert.Contains("cs-request", json, StringComparison.Ordinal);
            Assert.Contains("gpt-6.1-sol", csv, StringComparison.Ordinal);
            Assert.Contains("retry_count", csv, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void Prune_RemovesOnlyFilesOutsideRetentionWindow()
    {
        var root = CreateTempDirectory();
        try
        {
            var paths = new AppPaths(root, Path.Combine(root, ".codex"));
            var oldPath = Path.Combine(
                paths.UsageLogDirectory,
                "2020",
                "01",
                "usage-2020-01-01.jsonl");
            Directory.CreateDirectory(Path.GetDirectoryName(oldPath)!);
            File.WriteAllText(oldPath, "{}" + Environment.NewLine, Encoding.UTF8);

            var currentPath = UsageLogPath(paths, DateTimeOffset.UtcNow);
            Directory.CreateDirectory(Path.GetDirectoryName(currentPath)!);
            File.WriteAllText(currentPath, "{}" + Environment.NewLine, Encoding.UTF8);

            var maintenance = new UsageLogMaintenanceService(paths, new UsageLogReader(paths));
            var deleted = maintenance.Prune(30, new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));

            Assert.Equal(1, deleted);
            Assert.False(File.Exists(oldPath));
            Assert.True(File.Exists(currentPath));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void ManagedFileBackup_CreatesTimestampedHistoryAndDetectsTampering()
    {
        var root = CreateTempDirectory();
        try
        {
            var path = Path.Combine(root, "managed.json");
            File.WriteAllText(path, "original");

            ManagedFileBackup.EnsureBackedUp(path);

            Assert.True(File.Exists(ManagedFileBackup.GetBackupPath(path)));
            Assert.True(File.Exists(ManagedFileBackup.GetChecksumPath(ManagedFileBackup.GetBackupPath(path))));
            Assert.NotEmpty(ManagedFileBackup.ListHistory(path));

            File.WriteAllText(ManagedFileBackup.GetBackupPath(path), "tampered");
            Assert.Throws<InvalidDataException>(() => ManagedFileBackup.RestoreOriginal(path));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static string UsageLogPath(AppPaths paths, DateTimeOffset timestamp)
    {
        var local = timestamp.ToLocalTime();
        return Path.Combine(
            paths.UsageLogDirectory,
            local.ToString("yyyy", CultureInfo.InvariantCulture),
            local.ToString("MM", CultureInfo.InvariantCulture),
            $"usage-{local:yyyy-MM-dd}.jsonl");
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "CodexSwitchTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }
}
