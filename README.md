# cli-tasker

A lightweight task manager with two interfaces: a CLI (`tasker` command) and a macOS menu bar app — all backed by the same SQLite database.

## Features

- **Two interfaces** — CLI commands and macOS menu bar app (Electron tray)
- **3-state tasks** — Pending, In-Progress, and Done with status cycling
- **Multiple lists** — Organize tasks into named lists with a configurable default
- **Task dependencies** — Subtask hierarchies and blocking relationships with cascade operations
- **Inline metadata** — Set priority, due dates, tags, and relationships right in the task description
- **Directory auto-detection** — Automatically filters to a list matching your current directory
- **Due dates** — Natural language input (today, tomorrow, friday, +3d, jan15) with overdue tracking
- **Priority levels** — High, Medium, Low with colored indicators
- **Tags** — Colored tag display with consistent hashing
- **Search** — Filter tasks by text across all lists
- **Undo/Redo** — Full undo history for all operations
- **Backup & Restore** — Automatic SQLite backups with hot backup API
- **Batch operations** — Check, uncheck, and delete multiple tasks at once
- **Soft delete** — Trash with restore, not permanent deletion
- **Drag-and-drop reordering** — Manual task ordering in the desktop app
- **Markdown descriptions** — Rich text rendering with task checklists, links, and nested lists
- **macOS Reminders sync** — Tasks with due dates sync to macOS Reminders
- **3-character IDs** — Short task IDs for easy reference

## Tech Stack

| | |
|---|---|
| **Language** | TypeScript |
| **Monorepo** | pnpm workspaces |
| **Database** | SQLite (Drizzle ORM + libsql) |
| **CLI** | Commander.js + Chalk |
| **Desktop** | Electron + React + Tailwind CSS |
| **Testing** | Vitest |

## Requirements

- Node.js 22+
- pnpm 10+
- macOS for the desktop menu bar app

## Installation

### From GitHub Releases

Download the latest release from the [Releases page](https://github.com/Carlos-err406/cli-tasker/releases).

#### macOS: "damaged" warning

macOS quarantines apps downloaded from the internet. Since Tasker is not code-signed, macOS will show a "damaged" warning when opening the app. To fix this, run:

```bash
xattr -cr /Applications/Tasker.app
```

This is not needed when installing from source.

### From source

```bash
git clone https://github.com/Carlos-err406/cli-tasker.git
cd cli-tasker
./install.sh
```

## Quick Start

```bash
# Add a task
tasker add "Buy groceries"

# Add with priority, due date, and tags
tasker add "Review PR\np1 @tomorrow #work"

# List all tasks
tasker list

# Set a task to in-progress
tasker wip abc

# Mark as done
tasker check abc
```

## Usage

### CLI Commands

| Command | Description |
|---------|-------------|
| `add <description>` | Add a new task |
| `list` | Display all tasks grouped by list |
| `check <id...>` | Mark tasks as done |
| `uncheck <id...>` | Mark tasks as pending |
| `status <id> <pending\|inprogress\|done>` | Set task status |
| `wip <id>` | Set task to in-progress |
| `delete <id...>` | Move tasks to trash |
| `rename <id> <description>` | Update task description |
| `move <id> <list>` | Move a task to a different list |
| `get <id>` | Show task details (`--json` for JSON output) |
| `due <id> <date>` | Set due date (`--clear` to remove) |
| `priority <id> <high\|medium\|low\|none>` | Set priority |
| `clear` | Move all tasks to trash |
| `lists` | Manage lists (create, delete, rename, set-default) |
| `trash` | View and manage deleted tasks (list, restore, clear) |
| `deps` | Manage dependencies (set-parent, unset-parent, add-blocker, remove-blocker) |
| `init` | Create a list named after the current directory |
| `backup` | List or restore backups |
| `undo` / `redo` | Undo or redo last operation |
| `history` | Show undo history |
| `system status` | Show statistics per list |

### Global Options

| Option | Description |
|--------|-------------|
| `-l, --list <name>` | Filter to a specific list |
| `-a, --all` | Show all lists (bypass directory auto-detection) |

### List Filters

| Option | Description |
|--------|-------------|
| `-c, --checked` | Show only completed tasks |
| `-u, --unchecked` | Show only incomplete tasks |
| `-p, --priority <level>` | Filter by priority (high, medium, low) |
| `--overdue` | Show only overdue tasks |

### Menu Bar App

A macOS menu bar app that shares the same database. Features task list popup, quick-add, search, drag-and-drop reordering, markdown rendering, tag filtering, and keyboard shortcuts (Cmd+Z for undo).

### Inline Metadata

Add metadata on the last line of a task description:

```bash
# Priority + due date + tag
tasker add "Deploy to production\np1 @friday #release"

# Subtask of another task
tasker add "Write unit tests\n^abc"

# Blocking relationship
tasker add "Fix auth bug\n!def"

# Related task
tasker add "Update docs\n~ghi"
```

| Marker | Meaning |
|--------|---------|
| `p1` / `p2` / `p3` | Priority: High / Medium / Low |
| `@date` | Due date (today, tomorrow, friday, jan15, +3d) |
| `#tag` | Tag |
| `^abc` | Subtask of task abc |
| `!abc` | Blocks task abc |
| `~abc` | Related to task abc |

### Task Dependencies

```bash
# Make a task a subtask
tasker deps set-parent <taskId> <parentId>

# Remove parent
tasker deps unset-parent <taskId>

# Add blocking relationship
tasker deps add-blocker <blockerId> <blockedId>

# Remove blocking
tasker deps remove-blocker <blockerId> <blockedId>
```

Cascade behavior: checking a parent marks all subtasks as done. Deleting a parent trashes all subtasks. Moving a parent moves all subtasks.

### Directory Auto-Detection

When a list matches your current directory name, commands automatically filter to that list:

```bash
cd ~/projects/my-app
tasker init              # Creates a list named "my-app"
tasker add "Fix bug"     # Added to "my-app" list
tasker list              # Shows only "my-app" tasks
tasker list -a           # Shows all lists
```

## Architecture

pnpm monorepo with three packages sharing a core library:

```
packages/core/    → Shared core (SQLite, Drizzle ORM, models, queries, undo system)
apps/cli/         → CLI tool (Commander.js + Chalk)
apps/desktop/     → macOS menu bar app (Electron + React + Tailwind)
```

All data stored in SQLite with WAL mode for concurrent CLI + desktop access:
`~/Library/Application Support/cli-tasker/tasker.db`

## Building from Source

```bash
pnpm install
pnpm build                # Build all packages
pnpm test                 # Run all tests
pnpm typecheck            # Typecheck all packages
pnpm dev:desktop          # Run desktop app in dev mode
```

## License

MIT
