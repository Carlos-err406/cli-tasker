namespace TaskerCore.Backup;

/// <summary>
/// Information about a backup file.
/// </summary>
public record BackupInfo(
    /// <summary>Full path to the backup file.</summary>
    string FilePath,

    /// <summary>When the backup was created.</summary>
    DateTime Timestamp,

    /// <summary>Whether this is a daily backup (vs version backup).</summary>
    bool IsDaily,

    /// <summary>Size of the backup file in bytes.</summary>
    long FileSize
);
