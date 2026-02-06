namespace TaskerCore.Backup;

using System.Globalization;
using System.Text.RegularExpressions;
using TaskerCore.Undo;

/// <summary>
/// Manages automatic backup creation, rotation, and restoration.
/// </summary>
public class BackupManager
{
    private readonly StoragePaths _paths;
    private readonly object _backupLock = new();

    public BackupManager(StoragePaths paths)
    {
        _paths = paths;
    }

    /// <summary>
    /// Creates a backup of current task files. Called before each save.
    /// Silently fails if backup cannot be created (should not block saves).
    /// </summary>
    public void CreateBackup()
    {
        lock (_backupLock)
        {
            _paths.EnsureBackupDirectory();
            var timestamp = DateTime.Now;

            // Only backup if files exist
            if (!File.Exists(_paths.AllTasksPath))
                return;

            // Create version backup
            var tasksBackupPath = GetVersionBackupPath(timestamp, "all-tasks");
            File.Copy(_paths.AllTasksPath, tasksBackupPath, overwrite: true);

            if (File.Exists(_paths.AllTrashPath))
            {
                var trashBackupPath = GetVersionBackupPath(timestamp, "all-tasks.trash");
                File.Copy(_paths.AllTrashPath, trashBackupPath, overwrite: true);
            }

            // Create/update daily backup if needed
            CreateDailyBackupIfNeeded(timestamp);

            // Cleanup old backups
            RotateBackups();
        }
    }

    /// <summary>
    /// Lists available backups, newest first.
    /// Returns only task file backups (not trash backups separately).
    /// </summary>
    public IReadOnlyList<BackupInfo> ListBackups()
    {
        var backupDir = _paths.BackupDirectory;
        if (!Directory.Exists(backupDir))
            return Array.Empty<BackupInfo>();

        var backups = new List<BackupInfo>();

        // Find all task backup files (not trash)
        var files = Directory.GetFiles(backupDir, "all-tasks.*" + BackupConfig.BackupExtension)
            .Where(f => !f.Contains(".trash."))
            .ToList();

        foreach (var file in files)
        {
            var info = ParseBackupFile(file);
            if (info != null)
                backups.Add(info);
        }

        // Sort by timestamp descending (newest first)
        return backups.OrderByDescending(b => b.Timestamp).ToList();
    }

    /// <summary>
    /// Restores from a specific backup timestamp.
    /// Creates a pre-restore safety backup first.
    /// </summary>
    /// <param name="timestamp">The timestamp of the backup to restore.</param>
    /// <param name="undoManager">Optional undo manager to clear history after restore.</param>
    public void RestoreBackup(DateTime timestamp, UndoManager? undoManager = null)
    {
        lock (_backupLock)
        {
            var tasksBackupPath = FindBackupByTimestamp(timestamp, "all-tasks");
            if (tasksBackupPath == null)
                throw new Exceptions.BackupNotFoundException(
                    $"Backup from {timestamp:yyyy-MM-dd HH:mm:ss} not found.");

            // Create safety backup before restore
            CreatePreRestoreBackup();

            // Restore tasks file
            File.Copy(tasksBackupPath, _paths.AllTasksPath, overwrite: true);

            // Restore trash file if it exists in backup
            var trashBackupPath = FindBackupByTimestamp(timestamp, "all-tasks.trash");
            if (trashBackupPath != null)
            {
                File.Copy(trashBackupPath, _paths.AllTrashPath, overwrite: true);
            }

            // Clear undo history - checksums won't match after restore
            undoManager?.ClearHistory();
        }
    }

    private void CreateDailyBackupIfNeeded(DateTime timestamp)
    {
        var dailyTasksPath = GetDailyBackupPath(timestamp, "all-tasks");

        // Skip if today's daily backup already exists
        if (File.Exists(dailyTasksPath))
            return;

        File.Copy(_paths.AllTasksPath, dailyTasksPath, overwrite: true);

        if (File.Exists(_paths.AllTrashPath))
        {
            var dailyTrashPath = GetDailyBackupPath(timestamp, "all-tasks.trash");
            File.Copy(_paths.AllTrashPath, dailyTrashPath, overwrite: true);
        }
    }

    private void CreatePreRestoreBackup()
    {
        if (!File.Exists(_paths.AllTasksPath))
            return;

        var timestamp = DateTime.Now;
        var preRestorePath = GetPreRestoreBackupPath(timestamp, "all-tasks");
        File.Copy(_paths.AllTasksPath, preRestorePath, overwrite: true);

        if (File.Exists(_paths.AllTrashPath))
        {
            var preRestoreTrashPath = GetPreRestoreBackupPath(timestamp, "all-tasks.trash");
            File.Copy(_paths.AllTrashPath, preRestoreTrashPath, overwrite: true);
        }
    }

    private void RotateBackups()
    {
        var backupDir = _paths.BackupDirectory;
        if (!Directory.Exists(backupDir))
            return;

        // Rotate version backups (keep MaxVersionBackups)
        RotateVersionBackups(backupDir);

        // Rotate daily backups (keep MaxDailyBackupDays)
        RotateDailyBackups(backupDir);
    }

    private void RotateVersionBackups(string backupDir)
    {
        // Get all version backup files for tasks (not daily, not pre-restore)
        var versionBackups = Directory.GetFiles(backupDir, "all-tasks.*" + BackupConfig.BackupExtension)
            .Where(f => !f.Contains(BackupConfig.DailyPrefix) &&
                       !f.Contains(BackupConfig.PreRestorePrefix) &&
                       !f.Contains(".trash."))
            .Select(f => new { Path = f, Info = ParseBackupFile(f) })
            .Where(x => x.Info != null)
            .OrderByDescending(x => x.Info!.Timestamp)
            .ToList();

        // Delete excess version backups (and their corresponding trash backups)
        foreach (var backup in versionBackups.Skip(BackupConfig.MaxVersionBackups))
        {
            TryDeleteBackupPair(backup.Path);
        }
    }

    private void RotateDailyBackups(string backupDir)
    {
        var cutoff = DateTime.Now.AddDays(-BackupConfig.MaxDailyBackupDays);

        // Get all daily backup files
        var dailyBackups = Directory.GetFiles(backupDir, $"all-tasks.{BackupConfig.DailyPrefix}*" + BackupConfig.BackupExtension)
            .Where(f => !f.Contains(".trash."))
            .Select(f => new { Path = f, Info = ParseBackupFile(f) })
            .Where(x => x.Info != null && x.Info.Timestamp < cutoff)
            .ToList();

        foreach (var backup in dailyBackups)
        {
            TryDeleteBackupPair(backup.Path);
        }
    }

    private static void TryDeleteBackupPair(string tasksBackupPath)
    {
        try
        {
            File.Delete(tasksBackupPath);

            // Also delete corresponding trash backup
            var trashBackupPath = tasksBackupPath.Replace("all-tasks.", "all-tasks.trash.");
            if (File.Exists(trashBackupPath))
                File.Delete(trashBackupPath);
        }
        catch
        {
            // Ignore deletion failures
        }
    }

    private string? FindBackupByTimestamp(DateTime timestamp, string baseName)
    {
        var backupDir = _paths.BackupDirectory;
        if (!Directory.Exists(backupDir))
            return null;

        // Try version backup first
        var versionPath = GetVersionBackupPath(timestamp, baseName);
        if (File.Exists(versionPath))
            return versionPath;

        // Try daily backup
        var dailyPath = GetDailyBackupPath(timestamp, baseName);
        if (File.Exists(dailyPath))
            return dailyPath;

        return null;
    }

    private static BackupInfo? ParseBackupFile(string filePath)
    {
        var fileName = Path.GetFileName(filePath);

        // Try to parse version backup: all-tasks.2026-02-05T14-30-45.backup.json
        var versionMatch = Regex.Match(fileName,
            @"all-tasks\.(\d{4}-\d{2}-\d{2}T\d{2}-\d{2}-\d{2})\.backup\.json$");
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

        // Try to parse daily backup: all-tasks.daily.2026-02-05.backup.json
        var dailyMatch = Regex.Match(fileName,
            @"all-tasks\.daily\.(\d{4}-\d{2}-\d{2})\.backup\.json$");
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

    private string GetVersionBackupPath(DateTime timestamp, string baseName)
    {
        var fileName = $"{baseName}.{timestamp.ToString(BackupConfig.TimestampFormat)}{BackupConfig.BackupExtension}";
        return Path.Combine(_paths.BackupDirectory, fileName);
    }

    private string GetDailyBackupPath(DateTime timestamp, string baseName)
    {
        var fileName = $"{baseName}.{BackupConfig.DailyPrefix}{timestamp.ToString(BackupConfig.DailyDateFormat)}{BackupConfig.BackupExtension}";
        return Path.Combine(_paths.BackupDirectory, fileName);
    }

    private string GetPreRestoreBackupPath(DateTime timestamp, string baseName)
    {
        var fileName = $"{baseName}.{BackupConfig.PreRestorePrefix}{timestamp.ToString(BackupConfig.TimestampFormat)}{BackupConfig.BackupExtension}";
        return Path.Combine(_paths.BackupDirectory, fileName);
    }
}
