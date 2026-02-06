# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

cli-tasker is a lightweight CLI task manager built with C# and .NET 10.0. It's packaged as a .NET global tool (`tasker` command) and uses SQLite for persistent storage.

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

### Running tests
```bash
# Run all tests
dotnet test

# Run specific test class
dotnet test --filter "FullyQualifiedName~TaskDescriptionParserTests"

# Run with verbose output
dotnet test -v n
```

Tests use isolated storage (temp directory or in-memory SQLite) to avoid affecting real tasks.

**Important:** When verifying new functionality, write tests instead of using `dotnet run --` commands. Tests are repeatable, don't affect real data, and serve as documentation.

### Reading tasks from the backlog
When referencing a task by ID, always read the **full task description**, not just the title. Tasks often have multi-line descriptions with important context.

```bash
# Get a specific task with full description
tasker get <taskId>

# Example: Get task 286 with all details
tasker get 286

# Get task as JSON (useful for agents/scripts)
tasker get 286 --json
```

### Updating CLI and TaskerTray
To update both the global CLI tool and the menu bar app after making changes:

```bash
./update.sh patch   # 2.29.0 → 2.29.1 (bug fixes)
./update.sh minor   # 2.29.0 → 2.30.0 (new features)
./update.sh major   # 2.29.0 → 3.0.0  (breaking changes)
```

The script automatically bumps the version in `cli-tasker.csproj`, then:
1. Packs and updates the global `tasker` CLI tool
2. Stops the running TaskerTray app
3. Builds and installs TaskerTray to /Applications
4. Relaunches TaskerTray

## Architecture (v3.0)

### SQLite Storage
All data is stored in a single SQLite database with WAL mode for concurrent CLI + tray access:
- `~/Library/Application Support/cli-tasker/tasker.db` - single database file

**Schema:**
```sql
lists (name TEXT PK, is_collapsed INTEGER, sort_order INTEGER)
tasks (id TEXT PK, description, is_checked, created_at, list_name FK→lists, due_date, priority, tags, is_trashed, sort_order)
config (key TEXT PK, value TEXT)
undo_history (id INTEGER PK, stack_type TEXT, command_json TEXT, created_at TEXT)
```

**Key design decisions:**
- Trash is a flag (`is_trashed`) on the tasks table, not a separate table
- Tags stored as JSON string array
- Foreign keys with `ON UPDATE CASCADE ON DELETE CASCADE` for list operations
- Sort order: highest `sort_order` = newest (display uses `ORDER BY sort_order DESC`)
- WAL journal mode enables concurrent reads from CLI + tray

**Migration from JSON:** On first launch, `JsonMigrator` detects old JSON files (`all-tasks.json`, `all-tasks.trash.json`, `config.json`, `undo-history.json`), imports data into SQLite, and renames originals to `.bak`.

### Service Container (TaskerServices)
Central dependency injection container:
```csharp
TaskerServices {
    Paths: StoragePaths      // File system paths
    Db: TaskerDb             // SQLite connection + helpers
    Config: AppConfig        // Default list setting (config table)
    Backup: BackupManager    // SQLite hot backup API
    Undo: UndoManager        // Undo/redo stacks (undo_history table)
}
```

**Factory methods:**
- `new TaskerServices()` - production (default paths)
- `new TaskerServices(baseDir)` - file-based tests (backup tests need real files)
- `TaskerServices.CreateInMemory()` - in-memory SQLite for fast unit tests

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
TaskerDb (SQLite)
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

### TaskerDb (Database Connection)
Wraps `SqliteConnection` with helper methods:
- `Execute(sql, params)` - run non-query SQL
- `ExecuteScalar<T>(sql, params)` - single value (handles nullable types)
- `Query<T>(sql, mapper, params)` - read multiple rows
- `QuerySingle<T>(sql, mapper, params)` - read one row
- `BeginTransaction()` - for multi-step operations
- `CreateInMemory()` - isolated in-memory DB for tests

### TodoTask (Immutable Record)
```csharp
record TodoTask(string Id, string Description, bool IsChecked, DateTime CreatedAt, string ListName,
    DateOnly? DueDate, Priority? Priority, string[]? Tags)
```

**Factory method**: `CreateTodoTask(string description, string listName)` creates a new task with 3-char GUID ID.

**Methods** (all return new instances - functional style):
- `Check()` / `UnCheck()` - toggle completion status
- `Rename(newDescription)` - update description
- `MoveToList(listName)` - change list assignment

### TodoTaskList (Main Data Manager)
Manages all tasks with optional list filtering via direct SQL:

**Constructor**: `TodoTaskList(services, listName?)` - null means all tasks, string filters to specific list.

**Instance Operations**:
| Operation | Behavior | Scope |
|-----------|----------|-------|
| `AddTodoTask(task)` | Inserts with highest sort_order | Global |
| `GetTodoTaskById(id)` | Returns task or null | Always global |
| `CheckTask(id)` | Toggles checked, bumps sort_order | Global |
| `UncheckTask(id)` | Toggles unchecked, bumps sort_order | Global |
| `DeleteTask(id)` | Sets is_trashed = 1 | Global |
| `DeleteTasks(ids[])` | Batch delete with per-task feedback | Global |
| `RenameTask(id, desc)` | Updates description, bumps sort_order | Global |
| `MoveTask(id, targetList)` | Changes list_name, bumps sort_order | Global |
| `ClearTasks()` | Trashes all filtered tasks | Respects filter |
| `ListTodoTasks(filter?)` | Displays tasks | Respects filter |
| `RestoreFromTrash(id)` | Sets is_trashed = 0 | Global |
| `GetTrash()` | Returns deleted tasks | Respects filter |
| `ClearTrash()` | DELETE from tasks | Respects filter |
| `GetStats()` | Returns TaskStats record | Respects filter |

**Static Methods** (for list management):
- `GetAllListNames()` - returns list names ordered by sort_order
- `ListHasTasks(listName)` - checks if list has any active tasks
- `ListExists(listName)` - checks if list exists in lists table
- `CreateList(listName)` - INSERT into lists
- `DeleteList(listName)` - DELETE from lists (cascades to tasks)
- `RenameList(oldName, newName)` - UPDATE lists (cascades to tasks via FK)
- `ReorderTask(taskId, newIndex)` - reorder within a list
- `ReorderList(listName, newIndex)` - reorder lists

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

### AppConfig
Manages the default list via `config` table:
- `GetDefaultList()` - returns default list for adding new tasks
- `SetDefaultList()` - persists default list preference
- `TaskPrefixLength = 12` - width of "(xxx) [ ] - " for display formatting

### BackupManager
Uses SQLite hot backup API (`SqliteConnection.BackupDatabase()`):
- `CreateBackup()` - creates version + daily backup files (`.backup.db`)
- `RestoreBackup(timestamp)` - restores from backup with pre-restore safety copy
- `ListBackups()` - lists available backups, newest first
- Automatic rotation: keeps last 10 version backups, 7 days of dailies

### UndoManager
Persists undo/redo stacks to `undo_history` table:
- `RecordCommand(cmd)` - push to undo stack
- `Undo()` / `Redo()` - execute and swap between stacks
- `BeginBatch()` / `EndBatch()` - group multiple commands
- Commands use `IUndoableCommand` interface with JSON polymorphic serialization

## Output Formatting

**Output.cs**: Centralized output with Spectre.Console markup:
- `Success()`, `Error()`, `Warning()`, `Info()` - auto-escape content
- `Markup()` - raw markup for complex formatting (used in list display)

**List display formatting**:
- Task IDs: `[dim](xxx)[/]`
- Checkboxes: `[green][[x]][/]` or `[grey][[ ]][/]`
- First line: `[bold]...[/]`
- Continuation lines: `[dim]...[/]` with 12-char indent

**Task ordering**: Unchecked first, then checked. Within each group, by priority, due date, then newest first.

## Exception Hierarchy

Base class: `TaskerException(string message)`

**Specific subclasses**:
- `ListNotFoundException` - list doesn't exist
- `ListAlreadyExistsException` - list already exists
- `InvalidListNameException` - invalid characters in name
- `CannotModifyDefaultListException` - attempt to delete/rename "tasks"
- `TaskNotFoundException` - task ID not found
- `BackupNotFoundException` - backup timestamp not found

All exceptions caught by `CommandHelper.WithErrorHandling()` and displayed via `Output.Error()`.

## Commands Reference

| File | Commands | Notes |
|------|----------|-------|
| `AddCommand.cs` | `add` | Uses default list if no `-l` option |
| `ListCommand.cs` | `list` | Supports `-l`, `-c`, `-u` options |
| `CheckCommand.cs` | `check`, `uncheck` | Returns tuple, supports multiple IDs |
| `DeleteCommand.cs` | `delete`, `clear` | Returns tuple, supports multiple IDs |
| `RenameCommand.cs` | `rename` | Single task by ID |
| `GetCommand.cs` | `get` | Single task by ID, supports `--json` |
| `MoveCommand.cs` | `move` | Single task, validates target list |
| `ListsCommand.cs` | `lists`, `lists create`, `lists delete`, `lists rename`, `lists set-default` | Complex with subcommands |
| `TrashCommand.cs` | `trash list`, `trash restore`, `trash clear` | Complex with subcommands |
| `SystemCommand.cs` | `system status` | Shows per-list statistics |

## Important Implementation Details

### Sort Order Convention
Newest tasks get the highest `sort_order` value. Display queries use `ORDER BY sort_order DESC` so newest appears first. `BumpSortOrder()` sets `sort_order = MAX(sort_order) + 1` to move a task to the top after modification.

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
`check`, `uncheck`, and `delete` commands accept multiple task IDs. Each ID is processed individually with per-task success/error feedback, wrapped in a transaction.

### Undo System
- Commands implement `IUndoableCommand` with `Execute()` and `Undo()` methods
- Use `recordUndo: false` parameter when calling from undo/redo to prevent recursion
- Register new commands in `IUndoableCommand.cs` with `[JsonDerivedType]` attribute
- Commands use `TaskerServices.Default` internally for storage operations

## Key Dependencies

- **System.CommandLine 2.0.2**: Modern CLI parsing with type-safe options
- **Spectre.Console 0.54.0**: Terminal formatting and markup rendering
- **Microsoft.Data.Sqlite 10.0.2**: SQLite database access (raw ADO.NET, no ORM)
