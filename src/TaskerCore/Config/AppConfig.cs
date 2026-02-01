namespace TaskerCore.Config;

using System.Text.Json;
using TaskerCore.Data;

/// <summary>
/// Application configuration management.
/// </summary>
public static class AppConfig
{
    /// <summary>Length of the task display prefix "(xxx) [ ] - " for formatting.</summary>
    public const int TaskPrefixLength = 12;

    /// <summary>Gets the default list name for adding new tasks.</summary>
    public static string GetDefaultList()
    {
        if (!File.Exists(StoragePaths.ConfigPath))
        {
            return ListManager.DefaultListName;
        }

        try
        {
            var json = File.ReadAllText(StoragePaths.ConfigPath);
            var config = JsonSerializer.Deserialize<ConfigData>(json);
            return config?.DefaultList ?? ListManager.DefaultListName;
        }
        catch
        {
            return ListManager.DefaultListName;
        }
    }

    /// <summary>Sets the default list name for adding new tasks.</summary>
    public static void SetDefaultList(string name)
    {
        StoragePaths.EnsureDirectory();
        var config = new ConfigData { DefaultList = name };
        var json = JsonSerializer.Serialize(config);
        File.WriteAllText(StoragePaths.ConfigPath, json);
    }

    private sealed class ConfigData
    {
        public string? DefaultList { get; set; }
    }
}
