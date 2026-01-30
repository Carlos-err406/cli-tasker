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

        var allOption = new Option<bool>("--all", "-a")
        {
            Description = "Show trash from all lists"
        };
        listCommand.Options.Add(allOption);

        listCommand.SetAction(CommandHelper.WithErrorHandling(parseResult =>
        {
            var showAll = parseResult.GetValue(allOption);

            if (showAll)
            {
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
                var listName = parseResult.GetValue(listOption);
                var todoTaskList = ListManager.GetTaskList(listName);
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
            var listName = parseResult.GetValue(listOption);
            var todoTaskList = ListManager.GetTaskList(listName);

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

        var allOption = new Option<bool>("--all", "-a")
        {
            Description = "Clear trash from all lists"
        };
        clearCommand.Options.Add(allOption);

        clearCommand.SetAction(CommandHelper.WithErrorHandling(parseResult =>
        {
            var clearAll = parseResult.GetValue(allOption);

            if (clearAll)
            {
                var listNames = ListManager.GetAllListNames();
                var totalCleared = 0;
                foreach (var name in listNames)
                {
                    var taskList = new TodoTaskList(name);
                    totalCleared += taskList.ClearTrash(silent: true);
                }
                Output.Success($"Permanently deleted {totalCleared} task(s) from all trash");
            }
            else
            {
                var listName = parseResult.GetValue(listOption);
                var todoTaskList = ListManager.GetTaskList(listName);
                todoTaskList.ClearTrash();
            }
        }));

        return clearCommand;
    }
}
