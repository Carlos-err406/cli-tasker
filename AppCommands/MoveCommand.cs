namespace cli_tasker;

using System.CommandLine;

static class MoveCommand
{
    public static Command CreateMoveCommand()
    {
        var moveCommand = new Command("move", "Move a task to a different list");
        var taskIdArg = new Argument<string>("taskId")
        {
            Description = "The task ID to move"
        };
        var targetListArg = new Argument<string>("targetList")
        {
            Description = "The list to move the task to"
        };
        moveCommand.Arguments.Add(taskIdArg);
        moveCommand.Arguments.Add(targetListArg);
        moveCommand.SetAction(CommandHelper.WithErrorHandling(parseResult =>
        {
            // Operations by ID work globally (no list filter)
            var taskList = new TodoTaskList();

            var taskId = parseResult.GetValue(taskIdArg);
            var targetList = parseResult.GetValue(targetListArg);
            if (taskId == null || targetList == null)
            {
                Output.Error("Task ID and target list are both required");
                return;
            }
            taskList.MoveTask(taskId, targetList);
        }));
        return moveCommand;
    }
}
