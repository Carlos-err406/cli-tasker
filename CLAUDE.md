# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

cli-tasker is a lightweight task manager with three interfaces: a CLI (`tasker` command), an interactive TUI, and a macOS menu bar app (TaskerTray). Built with C# and .NET 10.0, packaged as a .NET global tool, using SQLite for persistent storage.

**Projects:**
- `cli-tasker/` — CLI + TUI (.NET global tool, references TaskerCore)
- `src/TaskerCore/` — shared core library (models, data layer, no UI dependencies)
- `src/TaskerTray/` — macOS menu bar app (Avalonia, references TaskerCore)
- `tests/TaskerCore.Tests/` — unit tests

Design docs, brainstorms, and plans live in `docs/`.

## Building and Testing

```bash
dotnet build              # Build
dotnet test               # Run all tests (isolated storage)
dotnet run -- <cmd>       # Run during development

./update.sh patch         # Bug fixes: bump version, install CLI + TaskerTray
./update.sh minor         # New features
./update.sh major         # Breaking changes
```

**Important:** When verifying new functionality, write tests instead of using `dotnet run --` commands. Tests are repeatable, don't affect real data, and serve as documentation.

### Interpreting task references

When the user provides 3 hex characters (e.g. `0e0`, `a3f`, `1b2`), it's a tasker task ID. Run `tasker get <id>` to see the full task before proceeding.

### Working on tasks from the backlog

Always read the **full task description** — tasks often have multi-line descriptions with important context:

```bash
tasker get <taskId>           # Full description
tasker get <taskId> --json    # JSON output
tasker wip <taskId>           # Mark as in-progress when starting
tasker check <taskId>         # Mark as done (AFTER update.sh, so user can test first)
```

## Reference Docs

### Models and Schema
TodoTask record definition, TaskStatus enum, full SQLite schema, and result types.
→ `docs/reference/models-and-schema.md`

### Commands Reference
All CLI commands with their files, options, and patterns.
→ `docs/reference/commands.md`

### Inline Metadata Parsing
How `TaskDescriptionParser` parses priority, due dates, tags, and relationships from task descriptions.
→ `docs/reference/inline-metadata.md`

### Conventions
Three-surface consistency, task ordering, display formatting, cascade operations, undo system, directory auto-detection, TaskStatus alias, sort order, and default list protection.
→ `docs/reference/conventions.md`

## Key Dependencies

| Project | Package | Version |
|---------|---------|---------|
| CLI + TUI | System.CommandLine | 2.0.2 |
| CLI + TUI | Spectre.Console | 0.54.0 |
| TaskerCore | Microsoft.Data.Sqlite | 10.0.2 |
| TaskerTray | Avalonia | 11.3.0 |
| TaskerTray | CommunityToolkit.Mvvm | 8.4.0 |

## Maintaining This File

After completing tasks that change architecture, commands, models, or data layer:
1. Check if CLAUDE.md and the reference docs are still accurate
2. Suggest specific updates to the user
3. Common triggers: new commands, model field changes, schema migrations, new undo command types, new TUI modes
