namespace cli_tasker;

using System.Text.RegularExpressions;

static partial class ListManager
{
    public const string DefaultListName = "tasks";

    private static readonly string Directory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "cli-tasker");

    [GeneratedRegex(@"^[a-zA-Z0-9_-]+$")]
    private static partial Regex ValidNameRegex();

    // Validation

    public static bool IsValidListName(string name)
    {
        return !string.IsNullOrWhiteSpace(name) && ValidNameRegex().IsMatch(name);
    }

    public static bool ListExists(string name)
    {
        return File.Exists(GetFilePath(name));
    }

    // Discovery

    public static string GetFilePath(string name)
    {
        return Path.Combine(Directory, $"{name}.json");
    }

    public static string[] GetAllListNames()
    {
        EnsureDirectory();
        return System.IO.Directory.GetFiles(Directory, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => name != null && name != "config")
            .Cast<string>()
            .OrderBy(name => name != DefaultListName)
            .ThenBy(name => name)
            .ToArray();
    }

    // CRUD

    public static void CreateList(string name)
    {
        if (!IsValidListName(name))
        {
            throw new InvalidListNameException(name);
        }

        if (ListExists(name))
        {
            throw new ListAlreadyExistsException(name);
        }

        EnsureDirectory();
        File.WriteAllText(GetFilePath(name), "[]");
    }

    public static void DeleteList(string name)
    {
        if (name == DefaultListName)
        {
            throw new CannotModifyDefaultListException("delete");
        }

        if (!ListExists(name))
        {
            throw new ListNotFoundException(name);
        }

        File.Delete(GetFilePath(name));

        // Reset selection if deleting the selected list
        if (AppConfig.GetSelectedList() == name)
        {
            AppConfig.SetSelectedList(DefaultListName);
            Console.WriteLine($"Note: '{name}' was the selected list. Selection reset to '{DefaultListName}'.");
        }
    }

    public static void RenameList(string oldName, string newName)
    {
        if (oldName == DefaultListName)
        {
            throw new CannotModifyDefaultListException("rename");
        }

        if (!IsValidListName(newName))
        {
            throw new InvalidListNameException(newName);
        }

        if (!ListExists(oldName))
        {
            throw new ListNotFoundException(oldName);
        }

        if (ListExists(newName))
        {
            throw new ListAlreadyExistsException(newName);
        }

        File.Move(GetFilePath(oldName), GetFilePath(newName));
    }

    // Setup

    public static void EnsureDirectory()
    {
        if (!System.IO.Directory.Exists(Directory))
        {
            System.IO.Directory.CreateDirectory(Directory);
        }
    }

    // Factory

    public static TodoTaskList GetTaskList(string? listName)
    {
        // Priority: -l flag > selected list > default ("tasks")
        var name = listName ?? AppConfig.GetSelectedList();

        // Validate list exists (unless it's the default, which auto-creates)
        if (name != DefaultListName && !ListExists(name))
        {
            throw new ListNotFoundException(name);
        }

        return new TodoTaskList(name);
    }
}
