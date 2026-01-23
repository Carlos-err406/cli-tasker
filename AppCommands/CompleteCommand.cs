namespace cli_tasker;

using System.CommandLine;

static class CompleteCommand
{
    public static Command CreateCompleteCommand(TodoTaskList todoTaskList)
    {   
        var completeCommand = new Command("complete", "Complete a task");

        var taskIdArg = new Argument<string>("taskId")
        {
            Description = "The id of the task to complete"
        };

        completeCommand.Arguments.Add(taskIdArg);
        completeCommand.SetAction(parseResult =>
        {
            var taskId = parseResult.GetValue(taskIdArg);
            if(taskId == null)
            {
                Console.Write("Task id is required to complete a task");
                return;
            }
            todoTaskList.CompleteTask(taskId);
            Console.WriteLine($"Completed task: {taskId}");
        });
        return completeCommand;
    }
}