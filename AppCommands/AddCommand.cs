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
            Description = "The task description (supports: p1/p2/p3 for priority, @date for due date)"
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

            // Parse inline metadata from description (last line only, text kept intact)
            var parsed = TaskDescriptionParser.Parse(description);

            var task = TodoTask.CreateTodoTask(parsed.Description, listName);

            // Apply extracted metadata
            if (parsed.Priority.HasValue)
                task = task.SetPriority(parsed.Priority.Value);
            if (parsed.DueDate.HasValue)
                task = task.SetDueDate(parsed.DueDate.Value);

            var taskList = new TodoTaskList();
            taskList.AddTodoTask(task);

            Output.Success($"Task saved to '{listName}'. Use the list command to see your tasks");
        }));
        return addCommand;
    }
}
