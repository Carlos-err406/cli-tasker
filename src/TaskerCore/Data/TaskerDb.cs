namespace TaskerCore.Data;

using Microsoft.Data.Sqlite;

/// <summary>
/// SQLite database connection manager for all TaskerCore data.
/// Wraps a single SqliteConnection with schema creation and helper methods.
/// </summary>
public sealed class TaskerDb : IDisposable
{
    private readonly SqliteConnection _connection;

    public SqliteConnection Connection => _connection;

    /// <summary>
    /// Creates a TaskerDb from a file path. Opens the connection and enables WAL + foreign keys.
    /// </summary>
    public TaskerDb(string dbPath)
    {
        _connection = new SqliteConnection($"Data Source={dbPath}");
        _connection.Open();
        EnablePragmas();
    }

    /// <summary>
    /// Creates a TaskerDb from an existing open connection (for in-memory databases).
    /// </summary>
    private TaskerDb(SqliteConnection connection)
    {
        _connection = connection;
        // Pragmas already set by CreateInMemory
    }

    /// <summary>
    /// Creates an in-memory SQLite database for testing. Each call returns an isolated database.
    /// </summary>
    public static TaskerDb CreateInMemory()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var db = new TaskerDb(connection);
        db.EnablePragmas();
        db.EnsureCreated();

        return db;
    }

    private void EnablePragmas()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode = WAL;
            PRAGMA foreign_keys = ON;
            """;
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Creates all tables and indexes if they don't exist.
    /// </summary>
    public void EnsureCreated()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS lists (
                name TEXT PRIMARY KEY,
                is_collapsed INTEGER DEFAULT 0,
                sort_order INTEGER DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS tasks (
                id TEXT PRIMARY KEY,
                description TEXT NOT NULL,
                status INTEGER DEFAULT 0,
                created_at TEXT NOT NULL,
                list_name TEXT NOT NULL REFERENCES lists(name) ON UPDATE CASCADE ON DELETE CASCADE,
                due_date TEXT,
                priority INTEGER,
                tags TEXT,
                is_trashed INTEGER DEFAULT 0,
                sort_order INTEGER DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS config (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS undo_history (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                stack_type TEXT NOT NULL CHECK(stack_type IN ('undo', 'redo')),
                command_json TEXT NOT NULL,
                created_at TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_tasks_list_name ON tasks(list_name);
            CREATE INDEX IF NOT EXISTS idx_tasks_is_trashed ON tasks(is_trashed);
            CREATE INDEX IF NOT EXISTS idx_tasks_sort ON tasks(status, priority, due_date, sort_order);
            CREATE INDEX IF NOT EXISTS idx_undo_stack_type ON undo_history(stack_type);
            """;
        cmd.ExecuteNonQuery();

        // Migrate from is_checked → status if upgrading from older schema
        MigrateIsCheckedToStatus();

        // Ensure default list exists
        EnsureDefaultList();
    }

    /// <summary>
    /// Migrates old is_checked column to status column for existing databases.
    /// is_checked 0 (unchecked) → status 0 (Pending), is_checked 1 (checked) → status 2 (Done).
    /// </summary>
    private void MigrateIsCheckedToStatus()
    {
        // Check if tasks table has is_checked column (old schema)
        var hasIsChecked = Query(
            "PRAGMA table_info(tasks)",
            reader => reader.GetString(1), // column name
            []).Any(col => col == "is_checked");

        if (!hasIsChecked) return;

        using var tx = BeginTransaction();
        try
        {
            // Recreate tasks table with status column
            Execute("""
                CREATE TABLE tasks_new (
                    id TEXT PRIMARY KEY,
                    description TEXT NOT NULL,
                    status INTEGER DEFAULT 0,
                    created_at TEXT NOT NULL,
                    list_name TEXT NOT NULL REFERENCES lists(name) ON UPDATE CASCADE ON DELETE CASCADE,
                    due_date TEXT,
                    priority INTEGER,
                    tags TEXT,
                    is_trashed INTEGER DEFAULT 0,
                    sort_order INTEGER DEFAULT 0
                )
                """);
            Execute("""
                INSERT INTO tasks_new (id, description, status, created_at, list_name,
                    due_date, priority, tags, is_trashed, sort_order)
                SELECT id, description, CASE WHEN is_checked = 1 THEN 2 ELSE 0 END,
                    created_at, list_name, due_date, priority, tags, is_trashed, sort_order
                FROM tasks
                """);
            Execute("DROP TABLE tasks");
            Execute("ALTER TABLE tasks_new RENAME TO tasks");

            // Recreate indexes on new table
            Execute("CREATE INDEX IF NOT EXISTS idx_tasks_list_name ON tasks(list_name)");
            Execute("CREATE INDEX IF NOT EXISTS idx_tasks_is_trashed ON tasks(is_trashed)");
            Execute("CREATE INDEX IF NOT EXISTS idx_tasks_sort ON tasks(status, priority, due_date, sort_order)");

            // Clear undo history — old check/uncheck commands won't deserialize
            Execute("DELETE FROM undo_history");

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    private void EnsureDefaultList()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO lists (name, sort_order) VALUES (@name, 0)";
        cmd.Parameters.AddWithValue("@name", ListManager.DefaultListName);
        cmd.ExecuteNonQuery();
    }

    // --- Helper methods for common operations ---

    public SqliteCommand CreateCommand(string sql)
    {
        var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        return cmd;
    }

    public SqliteTransaction BeginTransaction() => _connection.BeginTransaction();

    public int Execute(string sql, params (string name, object? value)[] parameters)
    {
        using var cmd = CreateCommand(sql);
        foreach (var (name, value) in parameters)
        {
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }
        return cmd.ExecuteNonQuery();
    }

    public T? ExecuteScalar<T>(string sql, params (string name, object? value)[] parameters)
    {
        using var cmd = CreateCommand(sql);
        foreach (var (name, value) in parameters)
        {
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }
        var result = cmd.ExecuteScalar();
        if (result is null or DBNull) return default;

        // Handle nullable types — Convert.ChangeType doesn't support Nullable<T> directly
        var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        return (T)Convert.ChangeType(result, targetType);
    }

    public List<T> Query<T>(string sql, Func<SqliteDataReader, T> mapper, params (string name, object? value)[] parameters)
    {
        using var cmd = CreateCommand(sql);
        foreach (var (name, value) in parameters)
        {
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        using var reader = cmd.ExecuteReader();
        var results = new List<T>();
        while (reader.Read())
        {
            results.Add(mapper(reader));
        }
        return results;
    }

    public T? QuerySingle<T>(string sql, Func<SqliteDataReader, T> mapper, params (string name, object? value)[] parameters)
    {
        using var cmd = CreateCommand(sql);
        foreach (var (name, value) in parameters)
        {
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        using var reader = cmd.ExecuteReader();
        return reader.Read() ? mapper(reader) : default;
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}
