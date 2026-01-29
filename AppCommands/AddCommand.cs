namespace cli_tasker;

using System.CommandLine;

static class AddCommand
{
    public static Command CreateAddCommand(Option<string?> listOption)
    {
        var addCommand = new Command("add", "Add a new task");
        var descriptionArg = new Argument<string>("description")
        {
            Description = "The task description"
        };
        addCommand.Arguments.Add(descriptionArg);
        addCommand.SetAction(CommandHelper.WithErrorHandling(parseResult =>
        {
            var listName = parseResult.GetValue(listOption);
            var taskList = ListManager.GetTaskList(listName);

            var description = parseResult.GetValue(descriptionArg);
            if (description == null)
            {
                Output.Error("Need a description to create a new task...");
                return;
            }
            var task = TodoTask.CreateTodoTask(description);
            taskList.AddTodoTask(task);
            Output.Success("Task saved. Use the list command to see your tasks");
        }));
        return addCommand;
    }
}