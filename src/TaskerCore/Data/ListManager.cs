namespace TaskerCore.Data;

using System.Text.RegularExpressions;
using TaskerCore.Config;
using TaskerCore.Exceptions;
using TaskerCore.Results;

public static partial class ListManager
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

    // CRUD - These return TaskResult instead of using Output directly

    /// <summary>
    /// Creates a new list. Lists are created implicitly when tasks are added.
    /// </summary>
    /// <returns>TaskResult indicating success or error.</returns>
    public static TaskResult CreateList(string name)
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
        return new TaskResult.Success($"List '{name}' will be created when you add tasks to it with: tasker add \"task\" -l {name}");
    }

    /// <summary>
    /// Deletes a list and all its tasks.
    /// </summary>
    /// <returns>TaskResult indicating success or if default list was reset.</returns>
    public static TaskResult DeleteList(string name)
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
            return new TaskResult.Success($"Deleted list '{name}'. Note: It was the default list. Default reset to '{DefaultListName}'.");
        }

        return new TaskResult.Success($"Deleted list '{name}'");
    }

    /// <summary>
    /// Renames a list.
    /// </summary>
    /// <returns>TaskResult indicating success or if default list was updated.</returns>
    public static TaskResult RenameList(string oldName, string newName)
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
            return new TaskResult.Success($"Renamed '{oldName}' to '{newName}'. Note: It was the default list. Default updated to '{newName}'.");
        }

        return new TaskResult.Success($"Renamed list '{oldName}' to '{newName}'");
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
