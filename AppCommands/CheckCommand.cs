namespace cli_tasker;

using System.CommandLine;

static class CheckCommand
{
    public static (Command, Command) CreateCheckCommands(Option<string?> listOption)
    {
        var checkCommand = new Command("check", "Check one or more tasks");
        var uncheckCommand = new Command("uncheck", "Uncheck one or more tasks");

        var taskIdsArg = new Argument<string[]>("taskIds")
        {
            Description = "The id(s) of the task(s) to check/uncheck",
            Arity = ArgumentArity.OneOrMore
        };

        checkCommand.Arguments.Add(taskIdsArg);
        uncheckCommand.Arguments.Add(taskIdsArg);

        void action(ParseResult parseResult, bool check)
        {
            var listName = parseResult.GetValue(listOption);
            var todoTaskList = ListManager.GetTaskList(listName);

            var taskIds = parseResult.GetValue(taskIdsArg);
            if (taskIds == null || taskIds.Length == 0)
            {
                Console.WriteLine("At least one task id is required");
                return;
            }
            if (check)
            {
                todoTaskList.CheckTasks(taskIds);
            }
            else
            {
                todoTaskList.UncheckTasks(taskIds);
            }
        }

        checkCommand.SetAction(CommandHelper.WithErrorHandling(parseResult => action(parseResult, true)));
        uncheckCommand.SetAction(CommandHelper.WithErrorHandling(parseResult => action(parseResult, false)));

        return (checkCommand, uncheckCommand);
    }
}