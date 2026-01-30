# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

cli-tasker is a lightweight CLI task manager built with C# and .NET 10.0. It's packaged as a .NET global tool (`tasker` command) and uses JSON files for persistent storage.

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
<Version>1.4.2</Version>  <!-- Bump this -->
```

## Architecture

### Command Pattern
The app uses System.CommandLine for CLI parsing. Each command lives in `AppCommands/` and exposes a static factory method that returns a `Command` instance:

```csharp
public static Command CreateAddCommand(Option<string?> listOption)
```

**Key pattern**: The `listOption` global option is threaded through all commands to enable `-l` flag for list selection.

### Command Registration (Program.cs)
Commands are registered in `Program.Main()`. Some commands return tuples when they need to register multiple related commands (e.g., `DeleteCommand` returns both `delete` and `clear`).

### Error Handling
All command actions are wrapped with `CommandHelper.WithErrorHandling()` which catches `TaskerException` subclasses and displays formatted error messages. This provides consistent error UX across commands.

### Data Layer

**TodoTask**: Immutable record with methods that return new instances (functional style):
- `Check()` / `UnCheck()` - toggle completion
- `Rename()` - update description
- Task IDs are 3-character GUIDs

**TodoTaskList**: Manages a single list's tasks and trash:
- Handles JSON serialization/deserialization
- Operations like `AddTodoTask()`, `DeleteTask()`, `CheckTask()`, etc.
- Maintains separate trash for soft deletes
- All mutations call `Save()` to persist to disk

**ListManager**: Static utility for list management:
- File path resolution (`GetFilePath()`, `GetTrashFilePath()`)
- CRUD operations on lists (`CreateList()`, `DeleteList()`, `RenameList()`)
- List discovery (`GetAllListNames()`)
- Factory method `GetTaskList()` handles list resolution priority: `-l` flag > selected list > default

### State Management

**AppConfig**: Manages the selected list in `config.json`:
- `GetSelectedList()` - returns current selection or default
- `SetSelectedList()` - persists selection

**Storage locations** (macOS ApplicationData):
- Tasks: `~/Library/Application Support/cli-tasker/{listname}.json`
- Trash: `~/Library/Application Support/cli-tasker/{listname}.trash.json`
- Config: `~/Library/Application Support/cli-tasker/config.json`

### Output Formatting

**Output.cs**: Centralized output with Spectre.Console markup:
- `Success()`, `Error()`, `Warning()`, `Info()` - auto-escape content
- `Markup()` - raw markup for complex formatting (used in list display)

**List display formatting** (TodoTaskList.cs:187-221):
- Task IDs are shown dim: `[dim](xxx)[/]`
- Checkboxes: `[green][[x]][/]` or `[grey][[ ]][/]`
- First line is bold, continuation lines are dim and indented
- **Critical**: Each continuation line must have its own `[dim]...[/]` wrapper to prevent formatting bugs across newlines

## Important Implementation Details

### List Rename and Selection
When renaming a list, if it's the currently selected list, the selection must be updated in `AppConfig`. This is handled in `ListManager.RenameList()` (lines 118-122).

### Multi-line Task Formatting
Tasks can have newlines in descriptions. The formatting logic:
1. Splits on `\n`
2. Makes first line bold
3. Indents continuation lines by `AppConfig.TaskPrefixLength` (12 chars)
4. Wraps all continuation lines with a single `[dim]...[/]` tag

```csharp
var restLines = lines.Length > 1
    ? "\n" + indent + "[dim]" + string.Join("\n" + indent, lines.Skip(1).Select(Markup.Escape)) + "[/]"
    : "";
```

### Exception Hierarchy
Custom exceptions in `Exceptions/TaskerException.cs` provide user-friendly error messages. Always throw specific subclasses (e.g., `ListNotFoundException`) rather than generic exceptions.

### Default List Protection
The default list ("tasks") cannot be deleted or renamed. This is enforced in `ListManager` operations via `CannotModifyDefaultListException`.

## Key Dependencies

- **System.CommandLine 2.0.2**: Modern CLI parsing with type-safe options
- **Spectre.Console 0.54.0**: Terminal formatting and markup rendering
