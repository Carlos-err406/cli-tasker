namespace cli_tasker;

using System.CommandLine;
using TaskerCore.Config;
using TaskerCore.Data;
using TaskerCore.Models;
using TaskerCore.Parsing;

static class AddCommand
{
    public static Command CreateAddCommand(Option<string?> listOption)
    {
        var addCommand = new Command("add", "Add a new task");
        addCommand.Options.Add(listOption);
        var descriptionArg = new Argument<string>("description")
        {
            Description = "The task description (supports: !! or ! for priority, @date for due date)"
        };
        addCommand.Arguments.Add(descriptionArg);
        addCommand.SetAction(CommandHelper.WithErrorHandling(parseResult =>
        {
            var listName = parseResult.GetValue(listOption) ?? AppConfig.GetDefaultList();

            var description = parseResult.GetValue(descriptionArg);
            if (description == null)
            {
                Output.Error("Need a description to create a new task...");
                return;
            }

            // Parse inline metadata from description
            var parsed = TaskDescriptionParser.Parse(description);

            var task = TodoTask.CreateTodoTask(parsed.Description, listName);

            // Apply extracted metadata
            if (parsed.Priority.HasValue)
                task = task.SetPriority(parsed.Priority.Value);
            if (parsed.DueDate.HasValue)
                task = task.SetDueDate(parsed.DueDate.Value);

            var taskList = new TodoTaskList();
            taskList.AddTodoTask(task);

            // Build feedback message
            var extras = new List<string>();
            if (parsed.Priority.HasValue)
                extras.Add($"priority: {parsed.Priority.Value}");
            if (parsed.DueDate.HasValue)
                extras.Add($"due: {parsed.DueDate.Value:MMM d}");

            var extraInfo = extras.Count > 0 ? $" ({string.Join(", ", extras)})" : "";
            Output.Success($"Task saved to '{listName}'{extraInfo}");
        }));
        return addCommand;
    }
}
