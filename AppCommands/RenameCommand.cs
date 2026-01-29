namespace cli_tasker;

using System.CommandLine;

static class RenameCommand
{
    public static Command CreateRenameCommand(Option<string?> listOption)
    {
        var renameCommand = new Command("rename", "Rename a task");
        var taskIdArg = new Argument<string>("taskId")
        {
            Description = "The task ID to rename"
        };
        var descriptionArg = new Argument<string>("description")
        {
            Description = "The new task description"
        };
        renameCommand.Arguments.Add(taskIdArg);
        renameCommand.Arguments.Add(descriptionArg);
        renameCommand.SetAction(CommandHelper.WithErrorHandling(parseResult =>
        {
            var listName = parseResult.GetValue(listOption);
            var taskList = ListManager.GetTaskList(listName);

            var taskId = parseResult.GetValue(taskIdArg);
            var description = parseResult.GetValue(descriptionArg);
            if (taskId == null || description == null)
            {
                Output.Error("Task ID and new description are both required");
                return;
            }
            taskList.RenameTask(taskId, description);
        }));
        return renameCommand;
    }
}
