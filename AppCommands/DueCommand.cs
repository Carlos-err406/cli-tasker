namespace cli_tasker;

using System.CommandLine;
using TaskerCore.Data;
using TaskerCore.Parsing;

static class DueCommand
{
    public static Command CreateDueCommand()
    {
        var dueCommand = new Command("due", "Set or clear a task's due date");
        var taskIdArg = new Argument<string>("taskId")
        {
            Description = "The task ID"
        };
        var dateArg = new Argument<string>("date")
        {
            Description = "Due date (today, tomorrow, friday, jan15, +3d, or 'clear')"
        };
        dueCommand.Arguments.Add(taskIdArg);
        dueCommand.Arguments.Add(dateArg);
        dueCommand.SetAction(CommandHelper.WithErrorHandling(parseResult =>
        {
            var taskList = new TodoTaskList();

            var taskId = parseResult.GetValue(taskIdArg);
            var dateStr = parseResult.GetValue(dateArg);
            if (taskId == null || dateStr == null)
            {
                Output.Error("Task ID and date are both required");
                return;
            }

            DateOnly? dueDate = dateStr.ToLower() == "clear"
                ? null
                : DateParser.Parse(dateStr);

            if (dateStr.ToLower() != "clear" && dueDate == null)
            {
                Output.Error($"Could not parse date: {dateStr}");
                return;
            }

            Output.Result(taskList.SetTaskDueDate(taskId, dueDate));
        }));
        return dueCommand;
    }
}
