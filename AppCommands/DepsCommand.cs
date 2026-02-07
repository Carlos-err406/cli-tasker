namespace cli_tasker;

using System.CommandLine;
using TaskerCore.Data;

static class DepsCommand
{
    public static Command CreateDepsCommand()
    {
        var depsCommand = new Command("deps", "Manage task dependencies (subtasks, blocking, and related)");

        depsCommand.Add(CreateSetParentCommand());
        depsCommand.Add(CreateUnsetParentCommand());
        depsCommand.Add(CreateAddBlockerCommand());
        depsCommand.Add(CreateRemoveBlockerCommand());
        depsCommand.Add(CreateAddRelatedCommand());
        depsCommand.Add(CreateRemoveRelatedCommand());

        return depsCommand;
    }

    private static Command CreateSetParentCommand()
    {
        var command = new Command("set-parent", "Make a task a subtask of another task");
        var taskIdArg = new Argument<string>("taskId") { Description = "The task to make a subtask" };
        var parentIdArg = new Argument<string>("parentId") { Description = "The parent task ID" };
        command.Arguments.Add(taskIdArg);
        command.Arguments.Add(parentIdArg);

        command.SetAction(CommandHelper.WithErrorHandling(parseResult =>
        {
            var taskId = parseResult.GetValue(taskIdArg);
            var parentId = parseResult.GetValue(parentIdArg);

            if (string.IsNullOrWhiteSpace(taskId) || string.IsNullOrWhiteSpace(parentId))
            {
                Output.Error("Both task ID and parent ID are required");
                return;
            }

            var taskList = new TodoTaskList();
            Output.Result(taskList.SetParent(taskId, parentId));
        }));

        return command;
    }

    private static Command CreateUnsetParentCommand()
    {
        var command = new Command("unset-parent", "Remove a task's parent (make it top-level)");
        var taskIdArg = new Argument<string>("taskId") { Description = "The subtask to detach" };
        command.Arguments.Add(taskIdArg);

        command.SetAction(CommandHelper.WithErrorHandling(parseResult =>
        {
            var taskId = parseResult.GetValue(taskIdArg);

            if (string.IsNullOrWhiteSpace(taskId))
            {
                Output.Error("Task ID is required");
                return;
            }

            var taskList = new TodoTaskList();
            Output.Result(taskList.UnsetParent(taskId));
        }));

        return command;
    }

    private static Command CreateAddBlockerCommand()
    {
        var command = new Command("add-blocker", "Mark a task as blocking another task");
        var blockerIdArg = new Argument<string>("blockerId") { Description = "The blocking task ID" };
        var blockedIdArg = new Argument<string>("blockedId") { Description = "The blocked task ID" };
        command.Arguments.Add(blockerIdArg);
        command.Arguments.Add(blockedIdArg);

        command.SetAction(CommandHelper.WithErrorHandling(parseResult =>
        {
            var blockerId = parseResult.GetValue(blockerIdArg);
            var blockedId = parseResult.GetValue(blockedIdArg);

            if (string.IsNullOrWhiteSpace(blockerId) || string.IsNullOrWhiteSpace(blockedId))
            {
                Output.Error("Both blocker ID and blocked ID are required");
                return;
            }

            var taskList = new TodoTaskList();
            Output.Result(taskList.AddBlocker(blockerId, blockedId));
        }));

        return command;
    }

    private static Command CreateRemoveBlockerCommand()
    {
        var command = new Command("remove-blocker", "Remove a blocking relationship between tasks");
        var blockerIdArg = new Argument<string>("blockerId") { Description = "The blocking task ID" };
        var blockedIdArg = new Argument<string>("blockedId") { Description = "The blocked task ID" };
        command.Arguments.Add(blockerIdArg);
        command.Arguments.Add(blockedIdArg);

        command.SetAction(CommandHelper.WithErrorHandling(parseResult =>
        {
            var blockerId = parseResult.GetValue(blockerIdArg);
            var blockedId = parseResult.GetValue(blockedIdArg);

            if (string.IsNullOrWhiteSpace(blockerId) || string.IsNullOrWhiteSpace(blockedId))
            {
                Output.Error("Both blocker ID and blocked ID are required");
                return;
            }

            var taskList = new TodoTaskList();
            Output.Result(taskList.RemoveBlocker(blockerId, blockedId));
        }));

        return command;
    }

    private static Command CreateAddRelatedCommand()
    {
        var command = new Command("add-related", "Mark two tasks as related to each other");
        var taskId1Arg = new Argument<string>("taskId1") { Description = "First task ID" };
        var taskId2Arg = new Argument<string>("taskId2") { Description = "Second task ID" };
        command.Arguments.Add(taskId1Arg);
        command.Arguments.Add(taskId2Arg);

        command.SetAction(CommandHelper.WithErrorHandling(parseResult =>
        {
            var taskId1 = parseResult.GetValue(taskId1Arg);
            var taskId2 = parseResult.GetValue(taskId2Arg);

            if (string.IsNullOrWhiteSpace(taskId1) || string.IsNullOrWhiteSpace(taskId2))
            {
                Output.Error("Both task IDs are required");
                return;
            }

            var taskList = new TodoTaskList();
            Output.Result(taskList.AddRelated(taskId1, taskId2));
        }));

        return command;
    }

    private static Command CreateRemoveRelatedCommand()
    {
        var command = new Command("remove-related", "Remove a related relationship between two tasks");
        var taskId1Arg = new Argument<string>("taskId1") { Description = "First task ID" };
        var taskId2Arg = new Argument<string>("taskId2") { Description = "Second task ID" };
        command.Arguments.Add(taskId1Arg);
        command.Arguments.Add(taskId2Arg);

        command.SetAction(CommandHelper.WithErrorHandling(parseResult =>
        {
            var taskId1 = parseResult.GetValue(taskId1Arg);
            var taskId2 = parseResult.GetValue(taskId2Arg);

            if (string.IsNullOrWhiteSpace(taskId1) || string.IsNullOrWhiteSpace(taskId2))
            {
                Output.Error("Both task IDs are required");
                return;
            }

            var taskList = new TodoTaskList();
            Output.Result(taskList.RemoveRelated(taskId1, taskId2));
        }));

        return command;
    }
}
