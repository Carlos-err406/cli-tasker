namespace TaskerCore;

/// <summary>
/// Centralized storage path management for all TaskerCore data files.
/// Uses an instance-based design with a static Current property for convenience.
/// </summary>
public class StoragePaths
{
    private static StoragePaths? _current;
    private static readonly object _lock = new();

    /// <summary>
    /// The current storage paths instance. Defaults to production paths.
    /// </summary>
    public static StoragePaths Current
    {
        get
        {
            if (_current == null)
            {
                lock (_lock)
                {
                    _current ??= new StoragePaths(GetDefaultDirectory());
                }
            }
            return _current;
        }
    }

    /// <summary>
    /// Sets a custom StoragePaths instance for testing.
    /// </summary>
    internal static void SetCurrent(StoragePaths paths) => _current = paths;

    private static string GetDefaultDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "cli-tasker");

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
    /// <param name="baseDirectory">The base directory for all storage files.</param>
    public StoragePaths(string baseDirectory)
    {
        Directory = baseDirectory ?? throw new ArgumentNullException(nameof(baseDirectory));
    }

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
        if (!System.IO.Directory.Exists(BackupDirectory))
        {
            System.IO.Directory.CreateDirectory(BackupDirectory);
        }
    }

    // Static convenience methods that delegate to Current instance
    // These maintain backwards compatibility with existing code

    /// <summary>Sets a test directory. Creates a new StoragePaths instance.</summary>
    internal static void SetDirectory(string? path)
    {
        if (path != null)
        {
            _current = new StoragePaths(path);
        }
        else
        {
            // Reset to default - but this should rarely be needed
            _current = null;
        }
    }
}
