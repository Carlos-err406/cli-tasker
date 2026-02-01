namespace cli_tasker;

using System.CommandLine;
using TaskerCore.Data;

static class DeleteCommand
{
    public static (Command, Command) CreateDeleteCommands(Option<string?> listOption)
    {
        var deleteCommand = new Command("delete", "Delete one or more tasks");
        var clearCommand = new Command("clear", "Delete all tasks from a list");

        var taskIdsArg = new Argument<string[]>("taskIds")
        {
            Description = "The id(s) of the task(s) to delete",
            Arity = ArgumentArity.OneOrMore
        };

        deleteCommand.Arguments.Add(taskIdsArg);
        clearCommand.Options.Add(listOption);

        deleteCommand.SetAction(CommandHelper.WithErrorHandling(parseResult =>
        {
            // Operations by ID work globally (no list filter)
            var todoTaskList = new TodoTaskList();

            var taskIds = parseResult.GetValue(taskIdsArg);
            if (taskIds == null || taskIds.Length == 0)
            {
                Output.Error("At least one task id is required");
                return;
            }
            Output.BatchResults(todoTaskList.DeleteTasks(taskIds));
        }));

        clearCommand.SetAction(CommandHelper.WithErrorHandling(parseResult =>
        {
            var listName = parseResult.GetValue(listOption);
            if (listName == null)
            {
                Output.Error("Please specify a list with -l <list-name>");
                return;
            }

            var todoTaskList = new TodoTaskList(listName);
            var count = todoTaskList.ClearTasks();
            Output.Success($"Cleared {count} task(s) from '{listName}'");
        }));

        return (deleteCommand, clearCommand);
    }
}
