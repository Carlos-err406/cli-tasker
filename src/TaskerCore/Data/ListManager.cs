namespace TaskerCore.Data;

using System.Text.RegularExpressions;
using TaskerCore.Exceptions;
using TaskerCore.Results;
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

    public static bool ListExists(TaskerServices services, string name)
    {
        // Default list always exists
        if (name == DefaultListName) return true;
        // Check if list exists in storage (with or without tasks)
        return TodoTaskList.ListExists(services, name);
    }

    public static bool ListExists(string name) => ListExists(TaskerServices.Default, name);

    // Discovery

    public static string[] GetAllListNames(TaskerServices services)
    {
        return TodoTaskList.GetAllListNames(services);
    }

    public static string[] GetAllListNames() => GetAllListNames(TaskerServices.Default);

    // CRUD - These return TaskResult instead of using Output directly

    /// <summary>
    /// Creates a new empty list.
    /// </summary>
    /// <returns>TaskResult indicating success or error.</returns>
    public static TaskResult CreateList(TaskerServices services, string name)
    {
        if (!IsValidListName(name))
        {
            throw new InvalidListNameException(name);
        }

        if (ListExists(services, name))
        {
            throw new ListAlreadyExistsException(name);
        }

        TodoTaskList.CreateList(services, name);
        return new TaskResult.Success($"Created list '{name}'");
    }

    public static TaskResult CreateList(string name) => CreateList(TaskerServices.Default, name);

    /// <summary>
    /// Deletes a list and all its tasks.
    /// </summary>
    /// <param name="services">The services container.</param>
    /// <param name="name">Name of the list to delete.</param>
    /// <param name="recordUndo">Whether to record this action for undo. Set to false when called from undo/redo.</param>
    /// <returns>TaskResult indicating success or if default list was reset.</returns>
    public static TaskResult DeleteList(TaskerServices services, string name, bool recordUndo = true)
    {
        if (name == DefaultListName)
        {
            throw new CannotModifyDefaultListException("delete");
        }

        if (!ListExists(services, name))
        {
            throw new ListNotFoundException(name);
        }

        // Capture state before deletion for undo
        if (recordUndo)
        {
            var deletedList = TodoTaskList.GetListByName(services, name);
            var trashedList = TodoTaskList.GetTrashedListByName(services, name);
            var originalIndex = TodoTaskList.GetListIndex(services, name);
            var wasDefault = services.Config.GetDefaultList() == name;

            services.Undo.RecordCommand(new DeleteListCommand
            {
                ListName = name,
                DeletedList = deletedList,
                TrashedList = trashedList,
                WasDefaultList = wasDefault,
                OriginalIndex = originalIndex
            });
        }

        TodoTaskList.DeleteList(services, name);

        // Reset default if deleting the default list
        var wasDefaultList = services.Config.GetDefaultList() == name;
        if (wasDefaultList)
        {
            services.Config.SetDefaultList(DefaultListName);
        }

        if (recordUndo)
        {
            services.Undo.SaveHistory();
        }

        if (wasDefaultList)
        {
            return new TaskResult.Success($"Deleted list '{name}'. Note: It was the default list. Default reset to '{DefaultListName}'.");
        }

        return new TaskResult.Success($"Deleted list '{name}'");
    }

    public static TaskResult DeleteList(string name, bool recordUndo = true) =>
        DeleteList(TaskerServices.Default, name, recordUndo);

    /// <summary>
    /// Renames a list.
    /// </summary>
    /// <param name="services">The services container.</param>
    /// <param name="oldName">Current name of the list.</param>
    /// <param name="newName">New name for the list.</param>
    /// <param name="recordUndo">Whether to record this action for undo. Set to false when called from undo/redo.</param>
    /// <returns>TaskResult indicating success or if default list was updated.</returns>
    public static TaskResult RenameList(TaskerServices services, string oldName, string newName, bool recordUndo = true)
    {
        if (oldName == DefaultListName)
        {
            throw new CannotModifyDefaultListException("rename");
        }

        if (!IsValidListName(newName))
        {
            throw new InvalidListNameException(newName);
        }

        if (!ListExists(services, oldName))
        {
            throw new ListNotFoundException(oldName);
        }

        if (ListExists(services, newName))
        {
            throw new ListAlreadyExistsException(newName);
        }

        // Record undo command before making changes
        var wasDefault = services.Config.GetDefaultList() == oldName;
        if (recordUndo)
        {
            var cmd = new RenameListCommand
            {
                OldName = oldName,
                NewName = newName,
                WasDefaultList = wasDefault
            };
            services.Undo.RecordCommand(cmd);
        }

        TodoTaskList.RenameList(services, oldName, newName);

        // Update default if renaming the default list
        if (wasDefault)
        {
            services.Config.SetDefaultList(newName);

            if (recordUndo)
            {
                services.Undo.SaveHistory();
            }

            return new TaskResult.Success($"Renamed '{oldName}' to '{newName}'. Note: It was the default list. Default updated to '{newName}'.");
        }

        if (recordUndo)
        {
            services.Undo.SaveHistory();
        }

        return new TaskResult.Success($"Renamed list '{oldName}' to '{newName}'");
    }

    public static TaskResult RenameList(string oldName, string newName, bool recordUndo = true) =>
        RenameList(TaskerServices.Default, oldName, newName, recordUndo);

    // Resolution

    /// <summary>
    /// Resolves effective list filter. Priority: explicitList > auto-detect (unless showAll) > null.
    /// </summary>
    public static string? ResolveListFilter(
        TaskerServices services,
        string? explicitList,
        bool showAll,
        string? workingDirectory = null)
    {
        if (explicitList != null) return explicitList;
        if (showAll) return null;

        workingDirectory ??= Directory.GetCurrentDirectory();
        var dirName = Path.GetFileName(workingDirectory);
        return !string.IsNullOrEmpty(dirName) && ListExists(services, dirName) ? dirName : null;
    }

    public static string? ResolveListFilter(string? explicitList, bool showAll, string? workingDirectory = null)
        => ResolveListFilter(TaskerServices.Default, explicitList, showAll, workingDirectory);

    // Factory

    public static TodoTaskList GetTaskList(TaskerServices services, string? listName)
    {
        // If no list specified, return unfiltered (all tasks)
        if (listName == null)
        {
            return new TodoTaskList(services);
        }

        // Otherwise return filtered to specific list
        return new TodoTaskList(services, listName);
    }

    public static TodoTaskList GetTaskList(string? listName) => GetTaskList(TaskerServices.Default, listName);

    public static TodoTaskList GetTaskListForAdding(TaskerServices services, string? listName)
    {
        // For adding: use specified list or default list
        var name = listName ?? services.Config.GetDefaultList();
        return new TodoTaskList(services, name);
    }

    public static TodoTaskList GetTaskListForAdding(string? listName) =>
        GetTaskListForAdding(TaskerServices.Default, listName);
}
