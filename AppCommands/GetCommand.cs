namespace cli_tasker;

using System.CommandLine;
using System.Text.Json;
using Spectre.Console;
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

        var recursiveOption = new Option<bool>("--recursive", "-r")
        {
            Description = "Recursively show all related tasks in a tree"
        };
        getCommand.Options.Add(recursiveOption);

        getCommand.SetAction(CommandHelper.WithErrorHandling(parseResult =>
        {
            var taskId = parseResult.GetValue(taskIdArg);
            var asJson = parseResult.GetValue(jsonOption);
            var recursive = parseResult.GetValue(recursiveOption);

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
                OutputJson(task, taskList, recursive);
            }
            else if (recursive)
            {
                OutputRecursiveTree(task, taskList);
            }
            else
            {
                OutputHumanReadable(task, taskList);
            }
        }));

        return getCommand;
    }

    private static void OutputJson(TodoTask task, TodoTaskList taskList, bool recursive = false)
    {
        if (recursive)
        {
            var visited = new HashSet<string>();
            var obj = BuildJsonTree(task, taskList, visited);
            Console.WriteLine(JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true }));
            return;
        }

        var parsed = TaskDescriptionParser.Parse(task.Description);

        var parentTask = parsed.ParentId != null ? taskList.GetTodoTaskById(parsed.ParentId) : null;

        var subtaskObjs = (parsed.HasSubtaskIds ?? []).Select(id =>
        {
            var s = taskList.GetTodoTaskById(id);
            return new { id, description = s != null ? StringHelpers.Truncate(s.Description, 50) : "?", status = FormatStatus(s?.Status) };
        }).ToArray();

        var blocksObjs = (parsed.BlocksIds ?? []).Select(id =>
        {
            var b = taskList.GetTodoTaskById(id);
            return new { id, description = b != null ? StringHelpers.Truncate(b.Description, 50) : "?", status = FormatStatus(b?.Status) };
        }).ToArray();

        var blockedByObjs = (parsed.BlockedByIds ?? []).Select(id =>
        {
            var b = taskList.GetTodoTaskById(id);
            return new { id, description = b != null ? StringHelpers.Truncate(b.Description, 50) : "?", status = FormatStatus(b?.Status) };
        }).ToArray();

        var relatedObjs = (parsed.RelatedIds ?? []).Select(id =>
        {
            var r = taskList.GetTodoTaskById(id);
            return new { id, description = r != null ? StringHelpers.Truncate(r.Description, 50) : "?", status = FormatStatus(r?.Status) };
        }).ToArray();

        var obj2 = new
        {
            id = task.Id,
            description = task.Description,
            status = FormatStatus(task.Status),
            priority = task.Priority?.ToString().ToLower(),
            dueDate = task.DueDate?.ToString("yyyy-MM-dd"),
            tags = task.Tags,
            listName = task.ListName,
            createdAt = task.CreatedAt.ToString("o"),
            completedAt = task.CompletedAt?.ToString("o"),
            parentId = parsed.ParentId,
            parentStatus = parentTask != null ? FormatStatus(parentTask.Status) : null,
            subtasks = subtaskObjs,
            blocks = blocksObjs,
            blockedBy = blockedByObjs,
            related = relatedObjs
        };
        Console.WriteLine(JsonSerializer.Serialize(obj2, new JsonSerializerOptions { WriteIndented = true }));
    }

    internal static string FormatStatus(TaskStatus? s) => s switch
    {
        TaskStatus.Done => "done",
        TaskStatus.InProgress => "in-progress",
        _ => "pending"
    };

    internal static Dictionary<string, object?> BuildJsonTree(TodoTask task, TodoTaskList taskList, HashSet<string> visited)
    {
        visited.Add(task.Id);
        var parsed = TaskDescriptionParser.Parse(task.Description);

        var result = new Dictionary<string, object?>
        {
            ["id"] = task.Id,
            ["description"] = task.Description,
            ["status"] = FormatStatus(task.Status),
            ["priority"] = task.Priority?.ToString().ToLower(),
            ["dueDate"] = task.DueDate?.ToString("yyyy-MM-dd"),
            ["tags"] = task.Tags,
            ["listName"] = task.ListName,
            ["createdAt"] = task.CreatedAt.ToString("o"),
            ["completedAt"] = task.CompletedAt?.ToString("o"),
            ["parent"] = BuildJsonRelationship(parsed.ParentId, taskList, visited),
            ["subtasks"] = BuildJsonRelationshipArray(parsed.HasSubtaskIds, taskList, visited),
            ["blocks"] = BuildJsonRelationshipArray(parsed.BlocksIds, taskList, visited),
            ["blockedBy"] = BuildJsonRelationshipArray(parsed.BlockedByIds, taskList, visited),
            ["related"] = BuildJsonRelationshipArray(parsed.RelatedIds, taskList, visited)
        };

        return result;
    }

    private static object? BuildJsonRelationship(string? id, TodoTaskList taskList, HashSet<string> visited)
    {
        if (id == null) return null;

        if (visited.Contains(id))
            return new Dictionary<string, object?> { ["id"] = id, ["$ref"] = true };

        var task = taskList.GetTodoTaskById(id);
        if (task == null)
            return new Dictionary<string, object?> { ["id"] = id, ["error"] = "task not found" };

        return BuildJsonTree(task, taskList, visited);
    }

    private static object[] BuildJsonRelationshipArray(string[]? ids, TodoTaskList taskList, HashSet<string> visited)
    {
        if (ids == null || ids.Length == 0) return [];

        return ids.Select(id =>
        {
            if (visited.Contains(id))
                return (object)new Dictionary<string, object?> { ["id"] = id, ["$ref"] = true };

            var task = taskList.GetTodoTaskById(id);
            if (task == null)
                return new Dictionary<string, object?> { ["id"] = id, ["error"] = "task not found" };

            return BuildJsonTree(task, taskList, visited);
        }).ToArray();
    }

    private static void OutputRecursiveTree(TodoTask rootTask, TodoTaskList taskList)
    {
        var visited = new HashSet<string>();
        var tree = new Tree(FormatTaskNodeLabel(rootTask));
        visited.Add(rootTask.Id);
        AddTaskDetails(tree, rootTask);
        AddRelationshipNodes(tree, rootTask, taskList, visited);
        AnsiConsole.Write(tree);
    }

    private static string FormatTaskNodeLabel(TodoTask task)
    {
        var checkbox = task.Status switch
        {
            TaskStatus.Done => "[[x]]",
            TaskStatus.InProgress => "[[-]]",
            _ => "[[ ]]"
        };
        return $"[bold]({task.Id})[/] {checkbox} {Markup.Escape(TaskDescriptionParser.GetDisplayDescription(task.Description))}";
    }

    private static void AddTaskDetails(IHasTreeNodes parent, TodoTask task)
    {
        var priority = task.Priority.HasValue ? task.Priority.Value.ToString() : "-";
        var dueDate = task.DueDate.HasValue ? task.DueDate.Value.ToString("yyyy-MM-dd") : "-";
        var tags = task.Tags?.Length > 0 ? string.Join(" ", task.Tags.Select(t => $"#{t}")) : "-";

        var details = $"[dim]List: {task.ListName} | Priority: {priority} | Due: {dueDate} | Tags: {tags}[/]";
        parent.AddNode(details);
    }

    private static void AddRelationshipNodes(IHasTreeNodes parent, TodoTask task, TodoTaskList taskList, HashSet<string> visited)
    {
        var parsed = TaskDescriptionParser.Parse(task.Description);

        if (parsed.ParentId != null)
        {
            var section = parent.AddNode("[bold]Parent:[/]");
            AddRelatedTaskNode(section, parsed.ParentId, taskList, visited);
        }

        if (parsed.HasSubtaskIds is { Length: > 0 })
        {
            var section = parent.AddNode("[bold]Subtasks:[/]");
            foreach (var id in parsed.HasSubtaskIds)
                AddRelatedTaskNode(section, id, taskList, visited);
        }

        if (parsed.BlocksIds is { Length: > 0 })
        {
            var section = parent.AddNode("[bold]Blocks:[/]");
            foreach (var id in parsed.BlocksIds)
                AddRelatedTaskNode(section, id, taskList, visited);
        }

        if (parsed.BlockedByIds is { Length: > 0 })
        {
            var section = parent.AddNode("[bold]Blocked by:[/]");
            foreach (var id in parsed.BlockedByIds)
                AddRelatedTaskNode(section, id, taskList, visited);
        }

        if (parsed.RelatedIds is { Length: > 0 })
        {
            var section = parent.AddNode("[bold]Related:[/]");
            foreach (var id in parsed.RelatedIds)
                AddRelatedTaskNode(section, id, taskList, visited);
        }
    }

    private static void AddRelatedTaskNode(TreeNode section, string id, TodoTaskList taskList, HashSet<string> visited)
    {
        if (visited.Contains(id))
        {
            section.AddNode($"[dim]({id}) (see above)[/]");
            return;
        }

        var relatedTask = taskList.GetTodoTaskById(id);
        if (relatedTask == null)
        {
            section.AddNode($"[dim]({id}) (task not found)[/]");
            return;
        }

        visited.Add(id);
        var node = section.AddNode(FormatTaskNodeLabel(relatedTask));
        AddTaskDetails(node, relatedTask);
        AddRelationshipNodes(node, relatedTask, taskList, visited);
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
            var parentStatus = parent != null ? Output.FormatLinkedStatus(parent.Status) : "";
            Output.Markup($"[bold]Parent:[/]      [dim]({parsed.ParentId}) {Markup.Escape(parentDesc)}[/]{parentStatus}");
        }

        if (parsed.HasSubtaskIds is { Length: > 0 })
        {
            Output.Markup($"[bold]Subtasks:[/]");
            foreach (var subId in parsed.HasSubtaskIds)
            {
                var sub = taskList.GetTodoTaskById(subId);
                var subDesc = sub != null ? StringHelpers.Truncate(sub.Description, 40) : "?";
                var subStatus = sub != null ? Output.FormatLinkedStatus(sub.Status) : "";
                Output.Markup($"               [dim]({subId}) {Markup.Escape(subDesc)}[/]{subStatus}");
            }
        }

        if (parsed.BlocksIds is { Length: > 0 })
        {
            Output.Markup($"[bold]Blocks:[/]");
            foreach (var bId in parsed.BlocksIds)
            {
                var b = taskList.GetTodoTaskById(bId);
                var bDesc = b != null ? StringHelpers.Truncate(b.Description, 40) : "?";
                var bStatus = b != null ? Output.FormatLinkedStatus(b.Status) : "";
                Output.Markup($"               [dim]({bId}) {Markup.Escape(bDesc)}[/]{bStatus}");
            }
        }

        if (parsed.BlockedByIds is { Length: > 0 })
        {
            Output.Markup($"[bold]Blocked by:[/]");
            foreach (var bbId in parsed.BlockedByIds)
            {
                var bb = taskList.GetTodoTaskById(bbId);
                var bbDesc = bb != null ? StringHelpers.Truncate(bb.Description, 40) : "?";
                var bbStatus = bb != null ? Output.FormatLinkedStatus(bb.Status) : "";
                Output.Markup($"               [dim]({bbId}) {Markup.Escape(bbDesc)}[/]{bbStatus}");
            }
        }

        if (parsed.RelatedIds is { Length: > 0 })
        {
            Output.Markup($"[bold]Related:[/]");
            foreach (var rId in parsed.RelatedIds)
            {
                var r = taskList.GetTodoTaskById(rId);
                var rDesc = r != null ? StringHelpers.Truncate(r.Description, 40) : "?";
                var rStatus = r != null ? Output.FormatLinkedStatus(r.Status) : "";
                Output.Markup($"               [dim]({rId}) {Markup.Escape(rDesc)}[/]{rStatus}");
            }
        }

        Output.Markup($"[bold]Description:[/]");
        Console.WriteLine(task.Description);
    }
}
