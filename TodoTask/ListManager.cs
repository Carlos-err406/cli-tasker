namespace cli_tasker;

using System.Text.RegularExpressions;

static partial class ListManager
{
    public const string DefaultListName = "tasks";

    [GeneratedRegex(@"^[a-zA-Z0-9_-]+$")]
    private static partial Regex ValidNameRegex();

    // Validation

    public static bool IsValidListName(string name)
    {
        return !string.IsNullOrWhiteSpace(name) && ValidNameRegex().IsMatch(name);
    }

    public static bool ListExists(string name)
    {
        // Default list always exists
        if (name == DefaultListName) return true;
        // Other lists exist if they have tasks
        return TodoTaskList.ListHasTasks(name);
    }

    // Discovery

    public static string[] GetAllListNames()
    {
        return TodoTaskList.GetAllListNames();
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

        // Lists are created implicitly when tasks are added
        // This is now a no-op since we don't have separate list files
        Output.Info($"List '{name}' will be created when you add tasks to it with: tasker add \"task\" -l {name}");
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

        TodoTaskList.DeleteList(name);

        // Reset default if deleting the default list
        if (AppConfig.GetDefaultList() == name)
        {
            AppConfig.SetDefaultList(DefaultListName);
            Output.Warning($"Note: '{name}' was the default list. Default reset to '{DefaultListName}'.");
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

        TodoTaskList.RenameList(oldName, newName);

        // Update default if renaming the default list
        if (AppConfig.GetDefaultList() == oldName)
        {
            AppConfig.SetDefaultList(newName);
            Output.Warning($"Note: '{oldName}' was the default list. Default updated to '{newName}'.");
        }
    }

    // Factory

    public static TodoTaskList GetTaskList(string? listName)
    {
        // If no list specified, return unfiltered (all tasks)
        if (listName == null)
        {
            return new TodoTaskList();
        }

        // Otherwise return filtered to specific list
        return new TodoTaskList(listName);
    }

    public static TodoTaskList GetTaskListForAdding(string? listName)
    {
        // For adding: use specified list or default list
        var name = listName ?? AppConfig.GetDefaultList();
        return new TodoTaskList(name);
    }
}
