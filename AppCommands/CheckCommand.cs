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
            // Operations by ID work globally (no list filter)
            var todoTaskList = new TodoTaskList();

            var taskIds = parseResult.GetValue(taskIdsArg);
            if (taskIds == null || taskIds.Length == 0)
            {
                Output.Error("At least one task id is required");
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
