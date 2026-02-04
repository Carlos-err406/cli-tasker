namespace cli_tasker;

using System.CommandLine;
using TaskerCore.Data;
using TaskerCore.Models;

static class PriorityCommand
{
    public static Command CreatePriorityCommand()
    {
        var priorityCommand = new Command("priority", "Set or clear a task's priority");
        var taskIdArg = new Argument<string>("taskId")
        {
            Description = "The task ID"
        };
        var levelArg = new Argument<string>("level")
        {
            Description = "Priority level (high, medium, low, 1, 2, 3, or 'clear')"
        };
        levelArg.AcceptOnlyFromAmong("high", "medium", "low", "clear", "1", "2", "3", "p1", "p2", "p3");

        priorityCommand.Arguments.Add(taskIdArg);
        priorityCommand.Arguments.Add(levelArg);
        priorityCommand.SetAction(CommandHelper.WithErrorHandling(parseResult =>
        {
            var taskList = new TodoTaskList();

            var taskId = parseResult.GetValue(taskIdArg);
            var level = parseResult.GetValue(levelArg);
            if (taskId == null || level == null)
            {
                Output.Error("Task ID and priority level are both required");
                return;
            }

            Priority? priority = level.ToLower() switch
            {
                "high" or "1" or "p1" => Priority.High,
                "medium" or "2" or "p2" => Priority.Medium,
                "low" or "3" or "p3" => Priority.Low,
                "clear" => null,
                _ => null
            };

            Output.Result(taskList.SetTaskPriority(taskId, priority));
        }));
        return priorityCommand;
    }
}
