namespace cli_tasker;

using System.CommandLine;

static class TrashCommand
{
    public static Command CreateTrashCommand(Option<string?> listOption)
    {
        var trashCommand = new Command("trash", "Manage deleted tasks");

        // Subcommands
        trashCommand.Add(CreateListCommand(listOption));
        trashCommand.Add(CreateRestoreCommand(listOption));
        trashCommand.Add(CreateClearCommand(listOption));

        return trashCommand;
    }

    private static Command CreateListCommand(Option<string?> listOption)
    {
        var listCommand = new Command("list", "List deleted tasks in trash");
        listCommand.Options.Add(listOption);

        listCommand.SetAction(CommandHelper.WithErrorHandling(parseResult =>
        {
            var listName = parseResult.GetValue(listOption);

            if (listName == null)
            {
                // Default: show all trash grouped by list
                var listNames = ListManager.GetAllListNames();
                foreach (var name in listNames)
                {
                    var taskList = new TodoTaskList(name);
                    Output.Markup($"[bold underline]{name}[/]");
                    taskList.ListTrash();
                    Output.Info("");
                }
            }
            else
            {
                // Filter to specific list
                var todoTaskList = new TodoTaskList(listName);
                todoTaskList.ListTrash();
            }
        }));

        return listCommand;
    }

    private static Command CreateRestoreCommand(Option<string?> listOption)
    {
        var restoreCommand = new Command("restore", "Restore a task from trash");
        var taskIdArg = new Argument<string>("taskId")
        {
            Description = "The id of the task to restore"
        };
        restoreCommand.Arguments.Add(taskIdArg);

        restoreCommand.SetAction(CommandHelper.WithErrorHandling(parseResult =>
        {
            // Operations by ID work globally (no list filter)
            var todoTaskList = new TodoTaskList();

            var taskId = parseResult.GetValue(taskIdArg);
            if (taskId == null)
            {
                Output.Error("Task id is required");
                return;
            }

            todoTaskList.RestoreFromTrash(taskId);
        }));

        return restoreCommand;
    }

    private static Command CreateClearCommand(Option<string?> listOption)
    {
        var clearCommand = new Command("clear", "Permanently delete all tasks in trash");
        clearCommand.Options.Add(listOption);

        clearCommand.SetAction(CommandHelper.WithErrorHandling(parseResult =>
        {
            var listName = parseResult.GetValue(listOption);

            if (listName == null)
            {
                // Clear all trash
                var todoTaskList = new TodoTaskList();
                todoTaskList.ClearTrash();
            }
            else
            {
                // Clear trash for specific list
                var todoTaskList = new TodoTaskList(listName);
                todoTaskList.ClearTrash();
            }
        }));

        return clearCommand;
    }
}
