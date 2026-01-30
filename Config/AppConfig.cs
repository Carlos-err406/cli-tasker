namespace cli_tasker;

using System.Text.Json;

static class AppConfig
{
    public const int TaskPrefixLength = 12; // Length of "(xxx) [ ] - "

    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "cli-tasker",
        "config.json");

    public static string GetDefaultList()
    {
        if (!File.Exists(ConfigPath))
        {
            return ListManager.DefaultListName;
        }

        try
        {
            var json = File.ReadAllText(ConfigPath);
            var config = JsonSerializer.Deserialize<ConfigData>(json);
            return config?.DefaultList ?? ListManager.DefaultListName;
        }
        catch
        {
            return ListManager.DefaultListName;
        }
    }

    public static void SetDefaultList(string name)
    {
        var directory = Path.GetDirectoryName(ConfigPath);
        if (directory != null && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
        var config = new ConfigData { DefaultList = name };
        var json = JsonSerializer.Serialize(config);
        File.WriteAllText(ConfigPath, json);
    }

    private class ConfigData
    {
        public string? DefaultList { get; set; }
    }
}
