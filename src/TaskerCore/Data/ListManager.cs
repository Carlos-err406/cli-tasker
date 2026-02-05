namespace TaskerCore.Data;

using System.Text.RegularExpressions;
using TaskerCore.Config;
using TaskerCore.Exceptions;
using TaskerCore.Results;
using TaskerCore.Undo;
using TaskerCore.Undo.Commands;

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
        // Check if list exists in storage (with or without tasks)
        return TodoTaskList.ListExists(name);
    }

    // Discovery

    public static string[] GetAllListNames()
    {
        return TodoTaskList.GetAllListNames();
    }

    // CRUD - These return TaskResult instead of using Output directly

    /// <summary>
    /// Creates a new empty list.
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

        TodoTaskList.CreateList(name);
        return new TaskResult.Success($"Created list '{name}'");
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
    /// <param name="oldName">Current name of the list.</param>
    /// <param name="newName">New name for the list.</param>
    /// <param name="recordUndo">Whether to record this action for undo. Set to false when called from undo/redo.</param>
    /// <returns>TaskResult indicating success or if default list was updated.</returns>
    public static TaskResult RenameList(string oldName, string newName, bool recordUndo = true)
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

        // Record undo command before making changes
        var wasDefault = AppConfig.GetDefaultList() == oldName;
        if (recordUndo)
        {
            var cmd = new RenameListCommand
            {
                OldName = oldName,
                NewName = newName,
                WasDefaultList = wasDefault
            };
            UndoManager.Instance.RecordCommand(cmd);
        }

        TodoTaskList.RenameList(oldName, newName);

        // Update default if renaming the default list
        if (wasDefault)
        {
            AppConfig.SetDefaultList(newName);

            if (recordUndo)
            {
                UndoManager.Instance.SaveHistory();
            }

            return new TaskResult.Success($"Renamed '{oldName}' to '{newName}'. Note: It was the default list. Default updated to '{newName}'.");
        }

        if (recordUndo)
        {
            UndoManager.Instance.SaveHistory();
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
