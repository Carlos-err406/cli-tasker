namespace cli_tasker;

using System.CommandLine;
using System.Text.Json;
using TaskerCore;
using TaskerCore.Data;
using TaskerCore.Models;
using TaskerCore.Parsing;
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
                OutputJson(task, taskList);
            }
            else
            {
                OutputHumanReadable(task, taskList);
            }
        }));

        return getCommand;
    }

    private static void OutputJson(TodoTask task, TodoTaskList taskList)
    {
        var parsed = TaskDescriptionParser.Parse(task.Description);

        var subtaskObjs = (parsed.HasSubtaskIds ?? []).Select(id =>
        {
            var s = taskList.GetTodoTaskById(id);
            return new { id, description = s != null ? StringHelpers.Truncate(s.Description, 50) : "?" };
        }).ToArray();

        var blocksObjs = (parsed.BlocksIds ?? []).Select(id =>
        {
            var b = taskList.GetTodoTaskById(id);
            return new { id, description = b != null ? StringHelpers.Truncate(b.Description, 50) : "?" };
        }).ToArray();

        var blockedByObjs = (parsed.BlockedByIds ?? []).Select(id =>
        {
            var b = taskList.GetTodoTaskById(id);
            return new { id, description = b != null ? StringHelpers.Truncate(b.Description, 50) : "?" };
        }).ToArray();

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
            createdAt = task.CreatedAt.ToString("o"),
            completedAt = task.CompletedAt?.ToString("o"),
            parentId = parsed.ParentId,
            subtasks = subtaskObjs,
            blocks = blocksObjs,
            blockedBy = blockedByObjs
        };
        Console.WriteLine(JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void OutputHumanReadable(TodoTask task, TodoTaskList taskList)
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
        if (task.CompletedAt.HasValue)
            Output.Markup($"[bold]Completed:[/]   {task.CompletedAt.Value:yyyy-MM-dd HH:mm}");

        // Relationships (from parsed markers)
        var parsed = TaskDescriptionParser.Parse(task.Description);

        if (parsed.ParentId != null)
        {
            var parent = taskList.GetTodoTaskById(parsed.ParentId);
            var parentDesc = parent != null ? StringHelpers.Truncate(parent.Description, 40) : "?";
            Output.Markup($"[bold]Parent:[/]      [dim]({parsed.ParentId}) {Spectre.Console.Markup.Escape(parentDesc)}[/]");
        }

        if (parsed.HasSubtaskIds is { Length: > 0 })
        {
            Output.Markup($"[bold]Subtasks:[/]");
            foreach (var subId in parsed.HasSubtaskIds)
            {
                var sub = taskList.GetTodoTaskById(subId);
                var subDesc = sub != null ? StringHelpers.Truncate(sub.Description, 40) : "?";
                Output.Markup($"               [dim]({subId}) {Spectre.Console.Markup.Escape(subDesc)}[/]");
            }
        }

        if (parsed.BlocksIds is { Length: > 0 })
        {
            Output.Markup($"[bold]Blocks:[/]");
            foreach (var bId in parsed.BlocksIds)
            {
                var b = taskList.GetTodoTaskById(bId);
                var bDesc = b != null ? StringHelpers.Truncate(b.Description, 40) : "?";
                Output.Markup($"               [dim]({bId}) {Spectre.Console.Markup.Escape(bDesc)}[/]");
            }
        }

        if (parsed.BlockedByIds is { Length: > 0 })
        {
            Output.Markup($"[bold]Blocked by:[/]");
            foreach (var bbId in parsed.BlockedByIds)
            {
                var bb = taskList.GetTodoTaskById(bbId);
                var bbDesc = bb != null ? StringHelpers.Truncate(bb.Description, 40) : "?";
                Output.Markup($"               [dim]({bbId}) {Spectre.Console.Markup.Escape(bbDesc)}[/]");
            }
        }

        Output.Markup($"[bold]Description:[/]");
        Console.WriteLine(task.Description);
    }
}
