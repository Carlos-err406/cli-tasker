namespace TaskerCore.Backup;

/// <summary>
/// Configuration constants for the backup system.
/// </summary>
public static class BackupConfig
{
    /// <summary>Maximum number of version backups to keep.</summary>
    public const int MaxVersionBackups = 10;

    /// <summary>Maximum number of days to keep daily backups.</summary>
    public const int MaxDailyBackupDays = 7;

    /// <summary>File extension for backup files.</summary>
    public const string BackupExtension = ".backup.db";

    /// <summary>Prefix for daily backup files.</summary>
    public const string DailyPrefix = "daily.";

    /// <summary>Prefix for pre-restore safety backups.</summary>
    public const string PreRestorePrefix = "pre-restore.";

    /// <summary>Timestamp format for version backups (filesystem-safe).</summary>
    public const string TimestampFormat = "yyyy-MM-ddTHH-mm-ss";

    /// <summary>Date format for daily backups.</summary>
    public const string DailyDateFormat = "yyyy-MM-dd";
}
