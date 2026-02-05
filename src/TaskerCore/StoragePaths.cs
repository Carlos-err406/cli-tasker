namespace TaskerCore;

/// <summary>
/// Centralized storage path management for all TaskerCore data files.
/// </summary>
public static class StoragePaths
{
    private static string? _overrideDirectory;
    private static bool _testModeActive;

    /// <summary>Base directory for all cli-tasker data.</summary>
    public static string Directory => _overrideDirectory ?? GetDefaultDirectory();

    private static string GetDefaultDirectory()
    {
        // SAFETY: If test mode was ever activated, refuse to use production directory
        if (_testModeActive)
        {
            throw new InvalidOperationException(
                "StoragePaths: Test mode is active but no test directory is set. " +
                "This is a bug - tests must not write to production storage.");
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "cli-tasker");
    }

    /// <summary>
    /// Sets a custom directory for testing. Once called, test mode is permanently
    /// active for this process - setting null will throw instead of using production path.
    /// </summary>
    internal static void SetDirectory(string? path)
    {
        if (path != null)
        {
            _testModeActive = true;
        }
        _overrideDirectory = path;
    }

    /// <summary>Path to the main tasks JSON file.</summary>
    public static string AllTasksPath => Path.Combine(Directory, "all-tasks.json");

    /// <summary>Path to the trash JSON file.</summary>
    public static string AllTrashPath => Path.Combine(Directory, "all-tasks.trash.json");

    /// <summary>Path to the configuration JSON file.</summary>
    public static string ConfigPath => Path.Combine(Directory, "config.json");

    /// <summary>Path to the undo history JSON file.</summary>
    public static string UndoHistoryPath => Path.Combine(Directory, "undo-history.json");

    /// <summary>Directory for backup files.</summary>
    public static string BackupDirectory => Path.Combine(Directory, "backups");

    /// <summary>Ensures the storage directory exists.</summary>
    public static void EnsureDirectory()
    {
        if (!System.IO.Directory.Exists(Directory))
        {
            System.IO.Directory.CreateDirectory(Directory);
        }
    }

    /// <summary>Ensures the backup directory exists.</summary>
    public static void EnsureBackupDirectory()
    {
        if (!System.IO.Directory.Exists(BackupDirectory))
        {
            System.IO.Directory.CreateDirectory(BackupDirectory);
        }
    }
}
