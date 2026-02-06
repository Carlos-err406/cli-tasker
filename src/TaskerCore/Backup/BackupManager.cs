namespace TaskerCore.Backup;

using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using TaskerCore.Data;
using TaskerCore.Undo;

/// <summary>
/// Manages automatic backup creation, rotation, and restoration.
/// Backs up the SQLite database file.
/// </summary>
public class BackupManager
{
    private readonly StoragePaths _paths;
    private readonly TaskerDb _db;
    private readonly object _backupLock = new();

    public BackupManager(StoragePaths paths, TaskerDb db)
    {
        _paths = paths;
        _db = db;
    }

    /// <summary>
    /// Creates a backup of the current database. Called before modifications.
    /// Silently fails if backup cannot be created (should not block operations).
    /// </summary>
    public void CreateBackup()
    {
        lock (_backupLock)
        {
            _paths.EnsureBackupDirectory();
            var timestamp = DateTime.Now;

            // Create version backup using SQLite backup API
            var backupPath = GetVersionBackupPath(timestamp);
            BackupTo(backupPath);

            // Create/update daily backup if needed
            CreateDailyBackupIfNeeded(timestamp);

            // Cleanup old backups
            RotateBackups();
        }
    }

    /// <summary>
    /// Lists available backups, newest first.
    /// </summary>
    public IReadOnlyList<BackupInfo> ListBackups()
    {
        var backupDir = _paths.BackupDirectory;
        if (!Directory.Exists(backupDir))
            return Array.Empty<BackupInfo>();

        var backups = new List<BackupInfo>();

        var files = Directory.GetFiles(backupDir, $"*{BackupConfig.BackupExtension}")
            .ToList();

        foreach (var file in files)
        {
            var info = ParseBackupFile(file);
            if (info != null)
                backups.Add(info);
        }

        return backups.OrderByDescending(b => b.Timestamp).ToList();
    }

    /// <summary>
    /// Restores from a specific backup timestamp.
    /// Creates a pre-restore safety backup first.
    /// </summary>
    public void RestoreBackup(DateTime timestamp, UndoManager? undoManager = null)
    {
        lock (_backupLock)
        {
            var backupPath = FindBackupByTimestamp(timestamp);
            if (backupPath == null)
                throw new Exceptions.BackupNotFoundException(
                    $"Backup from {timestamp:yyyy-MM-dd HH:mm:ss} not found.");

            // Create safety backup before restore
            CreatePreRestoreBackup();

            // Restore by copying backup over the main database
            RestoreFrom(backupPath);

            // Clear undo history — it won't match the restored state
            undoManager?.ClearHistory();
        }
    }

    private void BackupTo(string destinationPath)
    {
        using var destination = new SqliteConnection($"Data Source={destinationPath}");
        destination.Open();
        _db.Connection.BackupDatabase(destination);
    }

    private void RestoreFrom(string sourcePath)
    {
        using var source = new SqliteConnection($"Data Source={sourcePath}");
        source.Open();
        source.BackupDatabase(_db.Connection);
    }

    private void CreateDailyBackupIfNeeded(DateTime timestamp)
    {
        var dailyPath = GetDailyBackupPath(timestamp);
        if (File.Exists(dailyPath))
            return;

        BackupTo(dailyPath);
    }

    private void CreatePreRestoreBackup()
    {
        var timestamp = DateTime.Now;
        var preRestorePath = GetPreRestoreBackupPath(timestamp);
        BackupTo(preRestorePath);
    }

    private void RotateBackups()
    {
        var backupDir = _paths.BackupDirectory;
        if (!Directory.Exists(backupDir))
            return;

        RotateVersionBackups(backupDir);
        RotateDailyBackups(backupDir);
    }

    private void RotateVersionBackups(string backupDir)
    {
        var versionBackups = Directory.GetFiles(backupDir, $"tasker.*{BackupConfig.BackupExtension}")
            .Where(f => !f.Contains(BackupConfig.DailyPrefix) &&
                       !f.Contains(BackupConfig.PreRestorePrefix))
            .Select(f => new { Path = f, Info = ParseBackupFile(f) })
            .Where(x => x.Info != null)
            .OrderByDescending(x => x.Info!.Timestamp)
            .ToList();

        foreach (var backup in versionBackups.Skip(BackupConfig.MaxVersionBackups))
        {
            TryDelete(backup.Path);
        }
    }

    private void RotateDailyBackups(string backupDir)
    {
        var cutoff = DateTime.Now.AddDays(-BackupConfig.MaxDailyBackupDays);

        var dailyBackups = Directory.GetFiles(backupDir, $"tasker.{BackupConfig.DailyPrefix}*{BackupConfig.BackupExtension}")
            .Select(f => new { Path = f, Info = ParseBackupFile(f) })
            .Where(x => x.Info != null && x.Info.Timestamp < cutoff)
            .ToList();

        foreach (var backup in dailyBackups)
        {
            TryDelete(backup.Path);
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch { /* Ignore deletion failures */ }
    }

    private string? FindBackupByTimestamp(DateTime timestamp)
    {
        var backupDir = _paths.BackupDirectory;
        if (!Directory.Exists(backupDir))
            return null;

        var versionPath = GetVersionBackupPath(timestamp);
        if (File.Exists(versionPath))
            return versionPath;

        var dailyPath = GetDailyBackupPath(timestamp);
        if (File.Exists(dailyPath))
            return dailyPath;

        return null;
    }

    private static BackupInfo? ParseBackupFile(string filePath)
    {
        var fileName = Path.GetFileName(filePath);

        // Version backup: tasker.2026-02-05T14-30-45.backup.db
        var versionMatch = Regex.Match(fileName,
            @"tasker\.(\d{4}-\d{2}-\d{2}T\d{2}-\d{2}-\d{2})\.backup\.db$");
        if (versionMatch.Success)
        {
            if (DateTime.TryParseExact(versionMatch.Groups[1].Value,
                BackupConfig.TimestampFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var timestamp))
            {
                var fileInfo = new FileInfo(filePath);
                return new BackupInfo(filePath, timestamp, IsDaily: false, fileInfo.Length);
            }
        }

        // Daily backup: tasker.daily.2026-02-05.backup.db
        var dailyMatch = Regex.Match(fileName,
            @"tasker\.daily\.(\d{4}-\d{2}-\d{2})\.backup\.db$");
        if (dailyMatch.Success)
        {
            if (DateTime.TryParseExact(dailyMatch.Groups[1].Value,
                BackupConfig.DailyDateFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date))
            {
                var fileInfo = new FileInfo(filePath);
                return new BackupInfo(filePath, date, IsDaily: true, fileInfo.Length);
            }
        }

        return null;
    }

    private string GetVersionBackupPath(DateTime timestamp)
    {
        var fileName = $"tasker.{timestamp.ToString(BackupConfig.TimestampFormat)}{BackupConfig.BackupExtension}";
        return Path.Combine(_paths.BackupDirectory, fileName);
    }

    private string GetDailyBackupPath(DateTime timestamp)
    {
        var fileName = $"tasker.{BackupConfig.DailyPrefix}{timestamp.ToString(BackupConfig.DailyDateFormat)}{BackupConfig.BackupExtension}";
        return Path.Combine(_paths.BackupDirectory, fileName);
    }

    private string GetPreRestoreBackupPath(DateTime timestamp)
    {
        var fileName = $"tasker.{BackupConfig.PreRestorePrefix}{timestamp.ToString(BackupConfig.TimestampFormat)}{BackupConfig.BackupExtension}";
        return Path.Combine(_paths.BackupDirectory, fileName);
    }
}
