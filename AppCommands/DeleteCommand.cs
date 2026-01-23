namespace cli_tasker;

using System.CommandLine;

class DeleteCommand
{
    public static Command CreateDeleteCommand(TodoTaskList todoTaskList)
    {
        var deleteCommand = new Command("delete", "Delete a task");
        var taskIdArg = new Argument<string>("taskId")
        {
            Description="The id of the task to delete"
        };

        deleteCommand.Arguments.Add(taskIdArg);
        deleteCommand.SetAction(parseResult =>
        {
            var taskId = parseResult.GetValue(taskIdArg);
            if(taskId == null)
            {
                Console.WriteLine("Task id is required to delete a task");
                return;
            }
            todoTaskList.DeleteTask(taskId);
            Console.WriteLine($"Deleted task: {taskId}");
        });
        return deleteCommand;
    }
}