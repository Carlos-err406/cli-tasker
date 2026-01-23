namespace cli_tasker;

using System.CommandLine;

static class CheckCommand
{
    public static (Command, Command) CreateCheckCommands(TodoTaskList todoTaskList)
    {
        var checkCommand = new Command("check", "Check a task");
        var uncheckCommand = new Command("uncheck", "Uncheck a task");

        var taskIdArg = new Argument<string>("taskId")
        {
            Description = "The id of the task to complete"
        };

        checkCommand.Arguments.Add(taskIdArg);
        uncheckCommand.Arguments.Add(taskIdArg);

        void action(ParseResult parseResult, bool check)
        {
            var taskId = parseResult.GetValue(taskIdArg);
            if (taskId == null)
            {
                Console.Write("Task id is required to complete a task");
                return;
            }
            if (check)
            {
                todoTaskList.CheckTask(taskId);
                Console.WriteLine($"Checked task: {taskId}");
            }
            else
            {
                todoTaskList.UncheckTask(taskId);
                Console.WriteLine($"Unchecked task: {taskId}");
            }
        }

        checkCommand.SetAction(parseResult => action(parseResult, true));
        uncheckCommand.SetAction(parseResult => action(parseResult, false));

        return (checkCommand, uncheckCommand);
    }
}