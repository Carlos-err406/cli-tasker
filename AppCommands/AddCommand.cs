namespace cli_tasker;

using System.CommandLine;

static class AddCommand
{

    public static Command CreateAddCommand(TodoTaskList taskList)
    {

        var addCommand = new Command("add", "Add a new task");
        var descriptionArg = new Argument<string>("description")
        {
            Description = "The task description"
        };
        addCommand.Arguments.Add(descriptionArg);
        addCommand.SetAction(parseResult =>
        {
            var description = parseResult.GetValue(descriptionArg);
            if (description == null)
            {
                Console.WriteLine("Need a description to create a new task...");
                return;
            }
            var task = TodoTask.CreateTodoTask(description);
            taskList.AddTodoTask(task);
            Console.WriteLine("Task saved. Use the list command to see your tasks");

        });
        return addCommand;
    }
}