namespace cli_tasker;

using System.Text.Json;

static class AppConfig
{
    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "cli-tasker",
        "config.json");

    public static string GetSelectedList()
    {
        if (!File.Exists(ConfigPath))
        {
            return ListManager.DefaultListName;
        }

        try
        {
            var json = File.ReadAllText(ConfigPath);
            var config = JsonSerializer.Deserialize<ConfigData>(json);
            return config?.SelectedList ?? ListManager.DefaultListName;
        }
        catch
        {
            return ListManager.DefaultListName;
        }
    }

    public static void SetSelectedList(string name)
    {
        ListManager.EnsureDirectory();
        var config = new ConfigData { SelectedList = name };
        var json = JsonSerializer.Serialize(config);
        File.WriteAllText(ConfigPath, json);
    }

    private class ConfigData
    {
        public string? SelectedList { get; set; }
    }
}
