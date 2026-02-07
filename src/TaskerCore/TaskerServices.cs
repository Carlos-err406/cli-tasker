namespace TaskerCore;

using TaskerCore.Backup;
using TaskerCore.Config;
using TaskerCore.Data;
using TaskerCore.Undo;

/// <summary>
/// Container for all TaskerCore services with dependency injection support.
/// Create one instance for production (default paths) or per-test (isolated paths).
/// </summary>
public class TaskerServices : IDisposable
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
    public TaskerDb Db { get; }
    public UndoManager Undo { get; }
    public AppConfig Config { get; }
    public BackupManager Backup { get; }

    /// <summary>
    /// Creates a new services instance with the given storage paths.
    /// Opens the SQLite database and ensures schema exists.
    /// </summary>
    /// <param name="paths">Storage paths. If null, uses default production paths.</param>
    public TaskerServices(StoragePaths? paths = null)
    {
        Paths = paths ?? new StoragePaths();
        Paths.EnsureDirectory();
        Db = new TaskerDb(Paths.DatabasePath);
        Db.EnsureCreated();
        JsonMigrator.MigrateIfNeeded(Paths, Db);
        InverseMarkerMigrator.MigrateIfNeeded(Db);
        Config = new AppConfig(Db);
        Backup = new BackupManager(Paths, Db);
        Undo = new UndoManager(Db);
    }

    /// <summary>
    /// Creates a new services instance with a custom base directory.
    /// Useful for tests that need file-system paths (e.g., backup tests).
    /// </summary>
    public TaskerServices(string baseDirectory) : this(new StoragePaths(baseDirectory))
    {
    }

    /// <summary>
    /// Creates a services instance with an in-memory SQLite database.
    /// Each call returns a fully isolated instance — ideal for tests.
    /// </summary>
    public static TaskerServices CreateInMemory()
    {
        var db = TaskerDb.CreateInMemory();
        return new TaskerServices(db);
    }

    /// <summary>
    /// Creates a services instance from an existing TaskerDb (for in-memory testing).
    /// </summary>
    private TaskerServices(TaskerDb db)
    {
        Paths = new StoragePaths(Path.Combine(Path.GetTempPath(), $"tasker-inmemory-{Guid.NewGuid()}"));
        Db = db;
        Config = new AppConfig(Db);
        Backup = new BackupManager(Paths, Db);
        Undo = new UndoManager(Db);
    }

    public void Dispose()
    {
        Db.Dispose();
        GC.SuppressFinalize(this);
    }
}
