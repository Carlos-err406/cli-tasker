# Commands Reference

All command files live in `AppCommands/`.

| File | Commands | Notes |
|------|----------|-------|
| `AddCommand.cs` | `add` | Uses default list if no `-l`, supports `--all` |
| `ListCommand.cs` | `list` | `-l`, `-c`, `-u`, `-p` (priority), `--overdue`, `--all` |
| `CheckCommand.cs` | `check`, `uncheck` | Returns tuple, multiple IDs |
| `DeleteCommand.cs` | `delete`, `clear` | Returns tuple, multiple IDs, `--all` |
| `StatusCommand.cs` | `status`, `wip` | Returns tuple. `wip` = shortcut for in-progress |
| `RenameCommand.cs` | `rename` | Single task by ID |
| `GetCommand.cs` | `get` | `--json` output, shows relationships |
| `MoveCommand.cs` | `move` | Validates target list |
| `DueCommand.cs` | `due` | `due <id> <date>`, `--clear` |
| `PriorityCommand.cs` | `priority` | `high\|medium\|low\|none` |
| `ListsCommand.cs` | `lists`, `lists create/delete/rename/set-default` | Subcommands |
| `TrashCommand.cs` | `trash list/restore/clear` | `-l` and `--all` |
| `SystemCommand.cs` | `system status` | Per-list statistics |
| `InitCommand.cs` | `init` | Creates list named after current directory |
| `BackupCommand.cs` | `backup list/restore` | SQLite backups |
| `DepsCommand.cs` | `deps set-parent/unset-parent/add-blocker/remove-blocker` | Dependencies |
| `UndoCommand.cs` | `undo`, `redo`, `history` | Returns triple |

## Global Options

| Option | Description |
|--------|-------------|
| `-l, --list <name>` | Filter to a specific list |
| `-a, --all` | Bypass directory auto-detection, show all lists |

## Command Pattern

Each command exposes a static factory method returning a `Command` instance. Some return tuples for related commands (e.g., `DeleteCommand.CreateDeleteCommands()` → `(deleteCommand, clearCommand)`). Commands are registered in `Program.cs`.

All command actions are wrapped with `CommandHelper.WithErrorHandling()` which catches `TaskerException` subclasses.
