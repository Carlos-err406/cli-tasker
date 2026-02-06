namespace TaskerCore.Config;

using TaskerCore.Data;

/// <summary>
/// Application configuration management backed by SQLite config table.
/// </summary>
public class AppConfig
{
    /// <summary>Length of the task display prefix "(xxx) [ ] - " for formatting.</summary>
    public const int TaskPrefixLength = 12;

    private readonly TaskerDb _db;

    public AppConfig(TaskerDb db)
    {
        _db = db;
    }

    /// <summary>Gets the default list name for adding new tasks.</summary>
    public string GetDefaultList()
    {
        return _db.ExecuteScalar<string>(
            "SELECT value FROM config WHERE key = 'default_list'")
            ?? ListManager.DefaultListName;
    }

    /// <summary>Sets the default list name for adding new tasks.</summary>
    public void SetDefaultList(string name)
    {
        _db.Execute(
            "INSERT INTO config (key, value) VALUES ('default_list', @value) ON CONFLICT(key) DO UPDATE SET value = @value",
            ("@value", name));
    }
}
