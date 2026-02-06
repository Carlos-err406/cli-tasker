namespace TaskerCore;

/// <summary>
/// Centralized storage path management for all TaskerCore data files.
/// Pure instance-based class - create with a base directory, get derived paths.
/// </summary>
public class StoragePaths
{
    /// <summary>Base directory for all cli-tasker data.</summary>
    public string Directory { get; }

    /// <summary>Path to the main tasks JSON file.</summary>
    public string AllTasksPath => Path.Combine(Directory, "all-tasks.json");

    /// <summary>Path to the trash JSON file.</summary>
    public string AllTrashPath => Path.Combine(Directory, "all-tasks.trash.json");

    /// <summary>Path to the configuration JSON file.</summary>
    public string ConfigPath => Path.Combine(Directory, "config.json");

    /// <summary>Path to the undo history JSON file.</summary>
    public string UndoHistoryPath => Path.Combine(Directory, "undo-history.json");

    /// <summary>Directory for backup files.</summary>
    public string BackupDirectory => Path.Combine(Directory, "backups");

    /// <summary>
    /// Creates a new StoragePaths instance with the specified base directory.
    /// </summary>
    /// <param name="baseDirectory">The base directory for all storage files. If null, uses the default application data directory.</param>
    public StoragePaths(string? baseDirectory = null)
    {
        Directory = baseDirectory ?? GetDefaultDirectory();
    }

    private static string GetDefaultDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "cli-tasker");

    /// <summary>Ensures the storage directory exists.</summary>
    public void EnsureDirectory()
    {
        if (!System.IO.Directory.Exists(Directory))
        {
            System.IO.Directory.CreateDirectory(Directory);
        }
    }

    /// <summary>Ensures the backup directory exists.</summary>
    public void EnsureBackupDirectory()
    {
        EnsureDirectory();
        if (!System.IO.Directory.Exists(BackupDirectory))
        {
            System.IO.Directory.CreateDirectory(BackupDirectory);
        }
    }
}
