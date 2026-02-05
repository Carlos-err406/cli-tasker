---
title: Add `get` command to retrieve a single task by ID
type: feat
date: 2026-02-05
---

# Add `get` Command to Retrieve Single Task by ID

Enable agents to retrieve a single task without context pollution (vs `list` which shows all tasks).

## Acceptance Criteria

- [x] `tasker get <taskId>` retrieves and displays a single task
- [x] Output shows all task fields: ID, description (full), status, priority, due date, tags, list name, created date
- [x] `--json` flag outputs machine-readable JSON format
- [x] Task not found returns clear error message
- [x] Works globally (no list filter needed - IDs are unique)
- [x] Update CLAUDE.md memory to reference this command for agent use

## Implementation

### GetCommand.cs

```csharp
namespace cli_tasker;

using System.CommandLine;
using System.Text.Json;
using TaskerCore.Data;
using TaskerCore.Models;

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

        var jsonOption = new Option<bool>("--json", "Output in JSON format");
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
            isChecked = task.IsChecked,
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
        var checkbox = task.IsChecked ? "[x]" : "[ ]";
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
```

### Program.cs Registration

Add after other command registrations (~line 40):

```csharp
rootCommand.Add(GetCommand.CreateGetCommand());
```

### CLAUDE.md Commands Table Update

Add to the Commands Reference table:

```markdown
| `GetCommand.cs` | `get` | Single task by ID, supports `--json` |
```

## Output Examples

### Human-readable (default)

```
ID:          abc
List:        work
Status:      [ ]
Priority:    High
Due:         2026-02-10
Tags:        #backend #urgent
Created:     2026-02-01 14:30
Description:
Fix the login bug
with multi-line support
```

### JSON (`--json`)

```json
{
  "id": "abc",
  "description": "Fix the login bug\nwith multi-line support",
  "isChecked": false,
  "priority": "high",
  "dueDate": "2026-02-10",
  "tags": ["backend", "urgent"],
  "listName": "work",
  "createdAt": "2026-02-01T14:30:00.0000000Z"
}
```

## Edge Cases

| Case | Behavior |
|------|----------|
| Task not found | `Output.Error("Task not found: {id}")` |
| Empty task ID | `Output.Error("Task ID is required")` |
| Task in trash | Not found (consistent with other commands) |

## References

- Pattern: `AppCommands/RenameCommand.cs` (single-task-by-ID pattern)
- Data: `TodoTaskList.GetTodoTaskById()` returns `TodoTask?`
- Model: `src/TaskerCore/Models/TodoTask.cs`
