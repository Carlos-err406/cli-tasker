namespace cli_tasker;

using System.CommandLine;

static class DeleteCommand
{
    public static (Command, Command) CreateDeleteCommands(Option<string?> listOption)
    {
        var deleteCommand = new Command("delete", "Delete one or more tasks");
        var clearCommand = new Command("clear", "Delete all tasks");

        var taskIdsArg = new Argument<string[]>("taskIds")
        {
            Description = "The id(s) of the task(s) to delete",
            Arity = ArgumentArity.OneOrMore
        };

        deleteCommand.Arguments.Add(taskIdsArg);

        deleteCommand.SetAction(CommandHelper.WithErrorHandling(parseResult =>
        {
            var listName = parseResult.GetValue(listOption);
            var todoTaskList = ListManager.GetTaskList(listName);

            var taskIds = parseResult.GetValue(taskIdsArg);
            if (taskIds == null || taskIds.Length == 0)
            {
                Output.Error("At least one task id is required");
                return;
            }
            todoTaskList.DeleteTasks(taskIds);
        }));

        clearCommand.SetAction(CommandHelper.WithErrorHandling(parseResult =>
        {
            var listName = parseResult.GetValue(listOption);
            var todoTaskList = ListManager.GetTaskList(listName);

            todoTaskList.ClearTasks();
            Output.Success("Cleared all tasks");
        }));

        return (deleteCommand, clearCommand);
    }
}