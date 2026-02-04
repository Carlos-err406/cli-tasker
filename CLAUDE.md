# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

cli-tasker is a lightweight CLI task manager built with C# and .NET 10.0. It's packaged as a .NET global tool (`tasker` command) and uses a single unified JSON file for persistent storage.

## Building and Testing

### Development workflow
```bash
# Build the project
dotnet build

# Run during development
dotnet run -- <command> [args]

# Package as NuGet package
dotnet pack -c Release -o ./nupkg

# Update global tool installation (requires version bump first)
dotnet tool update -g cli-tasker --add-source ./nupkg
```

### Version bumping
Before updating the global tool, increment the version in `cli-tasker.csproj`:
```xml
<Version>2.2.0</Version>  <!-- Bump this -->
```

## Architecture (v2.3)

### List-First Storage
All tasks are stored in a list-first structure where lists are first-class entities:
- `~/Library/Application Support/cli-tasker/all-tasks.json` - lists with their tasks
- `~/Library/Application Support/cli-tasker/all-tasks.trash.json` - soft-deleted tasks (same format)
- `~/Library/Application Support/cli-tasker/config.json` - default list setting

**JSON Structure:**
```json
[
  {"ListName": "tasks", "Tasks": [...]},
  {"ListName": "work", "Tasks": []}
]
```

This structure allows:
- Empty lists (lists can exist without tasks)
- Pre-creating lists before adding tasks
- Lists persist even after all tasks are deleted

### Command Flow
```
Program.cs (Entry Point)
    ↓
RootCommand with global --list option
    ↓
Individual Command Factories (AddCommand, ListCommand, etc.)
    ↓
Command.SetAction() + CommandHelper.WithErrorHandling()
    ↓
Data Layer (TodoTaskList, ListManager)
    ↓
Persistent Storage (JSON files)
```

### Command Behavior
- `tasker list` - shows ALL tasks grouped by list name
- `tasker list -l work` - filters to show only "work" list
- `tasker list -c/-u` - filter by checked/unchecked status
- `tasker add "task"` - adds to default list (configurable)
- `tasker add "task" -l work` - adds to "work" list
- `tasker delete/check/rename/move <id>` - works globally by task ID (no list filter needed)
- `tasker system status` - shows statistics across all lists
- `tasker lists create <name>` - creates a new empty list

### Command Pattern
The app uses System.CommandLine for CLI parsing. Each command lives in `AppCommands/` and exposes a static factory method that returns a `Command` instance:

```csharp
public static Command CreateAddCommand(Option<string?> listOption)
```

**Key pattern**: The `listOption` global option is threaded through commands for list filtering (`-l` flag). Commands that operate by ID don't use the list filter.

### Command Registration (Program.cs)
Commands are registered in `Program.Main()`. Some commands return tuples when they need to register multiple related commands:
- `DeleteCommand.CreateDeleteCommands()` returns `(deleteCommand, clearCommand)`
- `CheckCommand.CreateCheckCommands()` returns `(checkCommand, uncheckCommand)`

### Error Handling
All command actions are wrapped with `CommandHelper.WithErrorHandling()` which catches `TaskerException` subclasses and displays formatted error messages. This provides consistent error UX across commands.

## Data Layer

### TaskList (List Container)
```csharp
record TaskList(string ListName, TodoTask[] Tasks)
```

**Factory method**: `TaskList.Create(string listName)` creates an empty list.

**Methods** (all return new instances - functional style):
- `AddTask(task)` - add task to list
- `RemoveTask(taskId)` - remove task from list
- `UpdateTask(task)` - update a task in place
- `ReplaceTasks(tasks)` - replace all tasks

### TodoTask (Immutable Record)
```csharp
record TodoTask(string Id, string Description, bool IsChecked, DateTime CreatedAt, string ListName)
```

**Factory method**: `CreateTodoTask(string description, string listName)` creates a new task with 3-char GUID ID.

**Methods** (all return new instances - functional style):
- `Check()` / `UnCheck()` - toggle completion status
- `Rename(newDescription)` - update description
- `MoveToList(listName)` - change list assignment

### TodoTaskList (Main Data Manager)
Manages all tasks with optional list filtering:

**Constructor**: `TodoTaskList(string? listName = null)` - null means all tasks, string filters to specific list.

**Instance Operations**:
| Operation | Behavior | Scope |
|-----------|----------|-------|
| `AddTodoTask(task)` | Inserts at top of array | Global |
| `GetTodoTaskById(id)` | Returns task or null | Always global |
| `CheckTask(id)` | Toggles checked, re-inserts at top | Global |
| `UncheckTask(id)` | Toggles unchecked, re-inserts at top | Global |
| `DeleteTask(id)` | Moves to trash | Global |
| `DeleteTasks(ids[])` | Batch delete with per-task feedback | Global |
| `RenameTask(id, desc)` | Updates description, re-inserts at top | Global |
| `MoveTask(id, targetList)` | Changes list, re-inserts at top | Global |
| `ClearTasks()` | Moves all filtered tasks to trash | Respects filter |
| `ListTodoTasks(filter?)` | Displays tasks | Respects filter |
| `RestoreFromTrash(id)` | Moves task from trash to active | Global |
| `ListTrash()` | Displays deleted tasks | Respects filter |
| `ClearTrash()` | Permanently deletes | Respects filter |
| `GetStats()` | Returns TaskStats record | Respects filter |

**Static Methods** (for list management):
- `GetAllListNames()` - returns distinct list names (default first, then alphabetical)
- `ListHasTasks(listName)` - checks if list has any tasks
- `ListExists(listName)` - checks if list exists (with or without tasks)
- `CreateList(listName)` - creates a new empty list
- `DeleteList(listName)` - removes list and all tasks/trash
- `RenameList(oldName, newName)` - updates ListName on list object

**Migration**: When loading old format (flat TodoTask array), automatically migrates to new list-first format on save.

**Persistence**:
- `Save()` - serializes TaskList[] to JSON
- Uses `SaveLock` for thread-safe file writes
- `EnsureDirectory()` - creates config directory if missing

### ListManager (Static Utility)
**Constants**:
- `DefaultListName = "tasks"` - cannot be deleted or renamed

**Validation**:
- `IsValidListName(name)` - regex: `^[a-zA-Z0-9_-]+$`
- `ListExists(name)` - returns true if list exists in storage (default always exists)

**Operations**:
- `CreateList(name)` - creates empty list, validates name, throws if exists
- `DeleteList(name)` - prevents deletion of default, updates config if needed
- `RenameList(oldName, newName)` - validates, prevents default rename, updates config if needed

**Factory Methods**:
- `GetTaskList(listName?)` - returns filtered or unfiltered TodoTaskList
- `GetTaskListForAdding(listName?)` - returns TodoTaskList for specified or default list

### TaskStats (Record)
```csharp
record TaskStats { int Total; int Checked; int Unchecked; int Trash; }
```
Used by `TodoTaskList.GetStats()` for system status display.

### AppConfig (Static Utility)
Manages the default list in `config.json`:
- `GetDefaultList()` - returns default list for adding new tasks
- `SetDefaultList()` - persists default list preference
- `TaskPrefixLength = 12` - width of "(xxx) [ ] - " for display formatting

## Output Formatting

**Output.cs**: Centralized output with Spectre.Console markup:
- `Success()`, `Error()`, `Warning()`, `Info()` - auto-escape content
- `Markup()` - raw markup for complex formatting (used in list display)

**List display formatting**:
- Task IDs: `[dim](xxx)[/]`
- Checkboxes: `[green][[x]][/]` or `[grey][[ ]][/]`
- First line: `[bold]...[/]`
- Continuation lines: `[dim]...[/]` with 12-char indent

**Task ordering**: Unchecked first, then checked. Within each group, newest first (descending by `CreatedAt`).

## Exception Hierarchy

Base class: `TaskerException(string message)`

**Specific subclasses**:
- `ListNotFoundException` - list doesn't exist
- `ListAlreadyExistsException` - list already exists
- `InvalidListNameException` - invalid characters in name
- `CannotModifyDefaultListException` - attempt to delete/rename "tasks"
- `TaskNotFoundException` - task ID not found

All exceptions caught by `CommandHelper.WithErrorHandling()` and displayed via `Output.Error()`.

## Commands Reference

| File | Commands | Notes |
|------|----------|-------|
| `AddCommand.cs` | `add` | Uses default list if no `-l` option |
| `ListCommand.cs` | `list` | Supports `-l`, `-c`, `-u` options |
| `CheckCommand.cs` | `check`, `uncheck` | Returns tuple, supports multiple IDs |
| `DeleteCommand.cs` | `delete`, `clear` | Returns tuple, supports multiple IDs |
| `RenameCommand.cs` | `rename` | Single task by ID |
| `MoveCommand.cs` | `move` | Single task, validates target list |
| `ListsCommand.cs` | `lists`, `lists create`, `lists delete`, `lists rename`, `lists set-default` | Complex with subcommands |
| `TrashCommand.cs` | `trash list`, `trash restore`, `trash clear` | Complex with subcommands |
| `SystemCommand.cs` | `system status` | Shows per-list statistics |

## Important Implementation Details

### Task Re-insertion Pattern
When checking, unchecking, renaming, or moving a task, the task is removed from its current position and re-inserted at the top of the array. This ensures modified tasks appear at the top of their section.

### Multi-line Task Formatting
Tasks can have newlines in descriptions:
1. Splits on `\n`
2. First line is bold
3. Continuation lines are dimmed with 12-char indent
4. All continuation lines share a single `[dim]...[/]` wrapper

### Default List Protection
The "tasks" list cannot be deleted or renamed. Enforced via `CannotModifyDefaultListException` in `ListManager` operations.

### List Rename and Default
When renaming a list that is currently the default, `AppConfig` is updated automatically in `ListManager.RenameList()`.

### Batch Operations
`check`, `uncheck`, and `delete` commands accept multiple task IDs. Each ID is processed individually with per-task success/error feedback.

## Key Dependencies

- **System.CommandLine 2.0.2**: Modern CLI parsing with type-safe options
- **Spectre.Console 0.54.0**: Terminal formatting and markup rendering
