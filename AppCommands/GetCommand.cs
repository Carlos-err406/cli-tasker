namespace cli_tasker;

using System.CommandLine;
using System.Text.Json;
using TaskerCore.Data;
using TaskerCore.Models;
using TaskStatus = TaskerCore.Models.TaskStatus;

static class GetCommand
{
    public static Command CreateGetCommand()
    {
        var getCommand = new Command("get", "Get detailed information about a task");

        var taskIdArg = new Argument<string>("taskId")
        {
            Description = "The task ID to retrieve"
        };
        getCommand.Arguments.Add(taskIdArg);

        var jsonOption = new Option<bool>("--json")
        {
            Description = "Output in JSON format"
        };
        getCommand.Options.Add(jsonOption);

        getCommand.SetAction(CommandHelper.WithErrorHandling(parseResult =>
        {
            var taskId = parseResult.GetValue(taskIdArg);
            var asJson = parseResult.GetValue(jsonOption);

            if (string.IsNullOrWhiteSpace(taskId))
            {
                Output.Error("Task ID is required");
                return;
            }

            var taskList = new TodoTaskList();
            var task = taskList.GetTodoTaskById(taskId);

            if (task == null)
            {
                Output.Error($"Task not found: {taskId}");
                return;
            }

            if (asJson)
            {
                OutputJson(task);
            }
            else
            {
                OutputHumanReadable(task);
            }
        }));

        return getCommand;
    }

    private static void OutputJson(TodoTask task)
    {
        var obj = new
        {
            id = task.Id,
            description = task.Description,
            status = task.Status switch
            {
                TaskStatus.Pending => "pending",
                TaskStatus.InProgress => "in-progress",
                TaskStatus.Done => "done",
                _ => "pending"
            },
            priority = task.Priority?.ToString().ToLower(),
            dueDate = task.DueDate?.ToString("yyyy-MM-dd"),
            tags = task.Tags,
            listName = task.ListName,
            createdAt = task.CreatedAt.ToString("o")
        };
        Console.WriteLine(JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void OutputHumanReadable(TodoTask task)
    {
        var checkbox = task.Status switch
        {
            TaskStatus.Done => "[[x]]",
            TaskStatus.InProgress => "[[-]]",
            _ => "[[ ]]"
        };
        var priority = task.Priority.HasValue ? task.Priority.Value.ToString() : "-";
        var dueDate = task.DueDate.HasValue ? task.DueDate.Value.ToString("yyyy-MM-dd") : "-";
        var tags = task.Tags?.Length > 0 ? string.Join(" ", task.Tags.Select(t => $"#{t}")) : "-";

        Output.Markup($"[bold]ID:[/]          {task.Id}");
        Output.Markup($"[bold]List:[/]        {task.ListName}");
        Output.Markup($"[bold]Status:[/]      {checkbox}");
        Output.Markup($"[bold]Priority:[/]    {priority}");
        Output.Markup($"[bold]Due:[/]         {dueDate}");
        Output.Markup($"[bold]Tags:[/]        {tags}");
        Output.Markup($"[bold]Created:[/]     {task.CreatedAt:yyyy-MM-dd HH:mm}");
        Output.Markup($"[bold]Description:[/]");
        Console.WriteLine(task.Description);
    }
}
