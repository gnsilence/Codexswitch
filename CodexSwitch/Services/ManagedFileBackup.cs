using System.Globalization;
using System.Security.Cryptography;

namespace CodexSwitch.Services;

internal static class ManagedFileBackup
{
    public static string GetBackupPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return path + ".bak";
    }

    // Marker recorded when CodexSwitch starts managing a path that had no original file.
    // It lets us remember "the user had nothing here" without creating a real backup of our
    // own generated config (requirement: never back up a file this program produced).
    private static string GetAbsentMarkerPath(string path)
    {
        return path + ".absent";
    }

    public static string GetChecksumPath(string path)
    {
        return path + ".sha256";
    }

    public static string GetHistoryDirectory(string path)
    {
        return path + ".history";
    }

    public static bool HasBackup(string path)
    {
        return File.Exists(GetBackupPath(path)) || File.Exists(GetAbsentMarkerPath(path));
    }

    public static IReadOnlyList<ManagedFileBackupEntry> ListHistory(string path)
    {
        var directory = GetHistoryDirectory(path);
        if (!Directory.Exists(directory))
            return [];

        try
        {
            return Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
                .Where(file => !file.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase))
                .Select(file => new ManagedFileBackupEntry(
                    file,
                    File.Exists(file + ".sha256")
                        ? File.ReadAllText(file + ".sha256").Trim()
                        : null))
                .OrderByDescending(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    ///     Captures the user's original file before CodexSwitch overwrites it. If an original exists it
    ///     is moved to a <c>.bak</c> file. If nothing exists, an empty <c>.absent</c> marker is written so
    ///     a later restore knows the config is CodexSwitch's own and should be removed rather than kept.
    ///     Idempotent: once a path is tracked, the current (managed) file is never captured as a backup.
    /// </summary>
    public static void EnsureBackedUp(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var backupPath = GetBackupPath(path);
        var absentPath = GetAbsentMarkerPath(path);

        // Already tracking this path's original state; do not back up our own managed config over it.
        if (File.Exists(backupPath) || File.Exists(absentPath))
            return;

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path))
        {
            CaptureHistory(path);
            File.Move(path, backupPath);
            WriteChecksum(backupPath);
        }
        else
        {
            File.WriteAllText(absentPath, "");
            CaptureAbsentHistory(path);
        }
    }

    /// <summary>
    ///     Reverses <see cref="EnsureBackedUp" />: restores the user's original from <c>.bak</c>, or removes
    ///     a CodexSwitch-generated file recorded by an <c>.absent</c> marker. A path we never managed
    ///     (no backup, no marker) is left untouched so we never delete a user's own file.
    /// </summary>
    public static void RestoreOriginal(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var backupPath = GetBackupPath(path);
        var absentPath = GetAbsentMarkerPath(path);

        if (File.Exists(backupPath))
        {
            VerifyChecksum(backupPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.Move(backupPath, path, overwrite: true);
            DeleteIfExists(GetChecksumPath(backupPath));
            if (File.Exists(absentPath))
                File.Delete(absentPath);
            return;
        }

        if (File.Exists(absentPath))
        {
            if (File.Exists(path))
                File.Delete(path);
            File.Delete(absentPath);
            return;
        }

        // Not managed by CodexSwitch: leave the user's file exactly as it is.
    }

    /// <summary>
    ///     Reinstates the captured original without consuming the backup, so managed config can be
    ///     re-applied afterwards. Mirrors <see cref="RestoreOriginal" /> for the absent-marker case
    ///     (removes a generated file) and leaves un-managed files untouched.
    /// </summary>
    public static void CopyBackupToOriginal(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var backupPath = GetBackupPath(path);
        if (File.Exists(backupPath))
        {
            VerifyChecksum(backupPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.Copy(backupPath, path, overwrite: true);
            return;
        }

        if (File.Exists(GetAbsentMarkerPath(path)) && File.Exists(path))
            File.Delete(path);
    }

    private static void CaptureHistory(string path)
    {
        var historyDirectory = GetHistoryDirectory(path);
        Directory.CreateDirectory(historyDirectory);
        var fileName = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) +
            "-" + Guid.NewGuid().ToString("N") + "-" + Path.GetFileName(path);
        var destination = Path.Combine(historyDirectory, fileName);
        File.Copy(path, destination, overwrite: false);
        WriteChecksum(destination);
    }

    private static void CaptureAbsentHistory(string path)
    {
        var historyDirectory = GetHistoryDirectory(path);
        Directory.CreateDirectory(historyDirectory);
        var fileName = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) +
            "-" + Guid.NewGuid().ToString("N") + "-" + Path.GetFileName(path) + ".absent";
        File.WriteAllText(Path.Combine(historyDirectory, fileName), "");
    }

    private static void WriteChecksum(string path)
    {
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        File.WriteAllText(GetChecksumPath(path), hash + Environment.NewLine);
    }

    private static void VerifyChecksum(string path)
    {
        var checksumPath = GetChecksumPath(path);
        if (!File.Exists(checksumPath))
            return;

        var expected = File.ReadAllText(checksumPath).Trim();
        var actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The managed backup checksum does not match its contents.");
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }
}

internal sealed record ManagedFileBackupEntry(string Path, string? Sha256);
