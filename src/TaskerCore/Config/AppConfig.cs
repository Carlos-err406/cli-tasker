namespace TaskerCore.Config;

using System.Text.Json;
using TaskerCore.Data;

/// <summary>
/// Application configuration management.
/// </summary>
public class AppConfig
{
    /// <summary>Length of the task display prefix "(xxx) [ ] - " for formatting.</summary>
    public const int TaskPrefixLength = 12;

    private readonly StoragePaths _paths;

    public AppConfig(StoragePaths paths)
    {
        _paths = paths;
    }

    /// <summary>Gets the default list name for adding new tasks.</summary>
    public string GetDefaultList()
    {
        if (!File.Exists(_paths.ConfigPath))
        {
            return ListManager.DefaultListName;
        }

        try
        {
            var json = File.ReadAllText(_paths.ConfigPath);
            var config = JsonSerializer.Deserialize<ConfigData>(json);
            return config?.DefaultList ?? ListManager.DefaultListName;
        }
        catch
        {
            return ListManager.DefaultListName;
        }
    }

    /// <summary>Sets the default list name for adding new tasks.</summary>
    public void SetDefaultList(string name)
    {
        _paths.EnsureDirectory();
        var config = new ConfigData { DefaultList = name };
        var json = JsonSerializer.Serialize(config);
        File.WriteAllText(_paths.ConfigPath, json);
    }

    private sealed class ConfigData
    {
        public string? DefaultList { get; set; }
    }
}
