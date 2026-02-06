namespace cli_tasker;

using System.CommandLine;
using TaskerCore.Data;
using TaskStatus = TaskerCore.Models.TaskStatus;

static class StatusCommand
{
    public static (Command statusCommand, Command wipCommand) CreateStatusCommands()
    {
        var statusCommand = new Command("status", "Set the status of one or more tasks");

        var statusArg = new Argument<string>("status")
        {
            Description = "The status to set: pending, in-progress, done"
        };
        var taskIdsArg = new Argument<string[]>("taskIds")
        {
            Description = "The id(s) of the task(s)",
            Arity = ArgumentArity.OneOrMore
        };

        statusCommand.Arguments.Add(statusArg);
        statusCommand.Arguments.Add(taskIdsArg);

        statusCommand.SetAction(CommandHelper.WithErrorHandling(parseResult =>
        {
            var statusStr = parseResult.GetValue(statusArg);
            var taskIds = parseResult.GetValue(taskIdsArg);

            if (string.IsNullOrWhiteSpace(statusStr))
            {
                Output.Error("Status is required: pending, in-progress, done");
                return;
            }

            var status = ParseStatus(statusStr);
            if (status == null)
            {
                Output.Error($"Unknown status: '{statusStr}'. Use: pending, in-progress, done");
                return;
            }

            if (taskIds == null || taskIds.Length == 0)
            {
                Output.Error("At least one task id is required");
                return;
            }

            var todoTaskList = new TodoTaskList();
            Output.BatchResults(todoTaskList.SetStatuses(taskIds, status.Value));
        }));

        // wip alias = status in-progress
        var wipCommand = new Command("wip", "Mark tasks as in-progress");
        var wipTaskIdsArg = new Argument<string[]>("taskIds")
        {
            Description = "The id(s) of the task(s)",
            Arity = ArgumentArity.OneOrMore
        };
        wipCommand.Arguments.Add(wipTaskIdsArg);

        wipCommand.SetAction(CommandHelper.WithErrorHandling(parseResult =>
        {
            var taskIds = parseResult.GetValue(wipTaskIdsArg);
            if (taskIds == null || taskIds.Length == 0)
            {
                Output.Error("At least one task id is required");
                return;
            }

            var todoTaskList = new TodoTaskList();
            Output.BatchResults(todoTaskList.SetStatuses(taskIds, TaskStatus.InProgress));
        }));

        return (statusCommand, wipCommand);
    }

    private static TaskStatus? ParseStatus(string status)
    {
        return status.ToLowerInvariant() switch
        {
            "pending" => TaskStatus.Pending,
            "in-progress" or "inprogress" or "wip" => TaskStatus.InProgress,
            "done" or "complete" or "completed" => TaskStatus.Done,
            _ => null
        };
    }
}
