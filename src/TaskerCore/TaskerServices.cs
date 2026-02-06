namespace TaskerCore;

using TaskerCore.Backup;
using TaskerCore.Config;
using TaskerCore.Undo;

/// <summary>
/// Container for all TaskerCore services with dependency injection support.
/// Create one instance for production (default paths) or per-test (isolated paths).
/// </summary>
public class TaskerServices
{
    /// <summary>
    /// Default services instance for production use.
    /// Can be overridden for tests using SetDefault().
    /// </summary>
    public static TaskerServices Default => _current ?? _default.Value;
    private static readonly Lazy<TaskerServices> _default = new(() => new TaskerServices());
    private static TaskerServices? _current;

    /// <summary>
    /// Sets the current default services instance. Use for tests to isolate storage.
    /// Call with null to reset to the production default.
    /// </summary>
    public static void SetDefault(TaskerServices? services) => _current = services;

    public StoragePaths Paths { get; }
    public UndoManager Undo { get; }
    public AppConfig Config { get; }
    public BackupManager Backup { get; }

    /// <summary>
    /// Creates a new services instance with the given storage paths.
    /// </summary>
    /// <param name="paths">Storage paths. If null, uses default production paths.</param>
    public TaskerServices(StoragePaths? paths = null)
    {
        Paths = paths ?? new StoragePaths();
        Config = new AppConfig(Paths);
        Backup = new BackupManager(Paths);
        Undo = new UndoManager(Paths);
    }

    /// <summary>
    /// Creates a new services instance with a custom base directory.
    /// Useful for tests.
    /// </summary>
    public TaskerServices(string baseDirectory) : this(new StoragePaths(baseDirectory))
    {
    }
}
