# cli-tasker

A lightweight command-line task manager built with C# and .NET 10.0.

## Features

- Add, list, delete, and manage tasks from the terminal
- Mark tasks as complete or incomplete (supports batch operations)
- Organize tasks into multiple lists with a configurable default
- Move tasks between lists
- Soft-delete with trash and restore functionality
- Simple 3-character task IDs for easy reference
- Tasks sorted by status (unchecked first) then by date (newest first)
- System diagnostics to view task statistics across all lists

## Requirements

- .NET 10.0 or later

## Installation

### As a global tool (recommended)

```bash
dotnet tool install -g cli-tasker
```

### From source

```bash
git clone <repository-url>
cd cli-tasker
dotnet build
```

## Usage

```bash
# Add a new task (to default list)
tasker add "Buy groceries"

# Add a task to a specific list
tasker add "Review PR" -l work

# List all tasks (grouped by list)
tasker list

# List tasks from a specific list
tasker list -l work

# List only unchecked or checked tasks
tasker list -u
tasker list -c

# Mark tasks as complete (supports multiple IDs)
tasker check abc def ghi

# Mark tasks as incomplete
tasker uncheck abc

# Delete tasks (moves to trash, supports multiple IDs)
tasker delete abc def

# Rename a task
tasker rename abc "Updated description"

# Move a task to a different list
tasker move abc work

# Clear all tasks from a specific list
tasker clear -l work

# View system status and statistics
tasker system status
```

### Example Output

```
$ tasker list
tasks
(def) [ ] - Write documentation
(ghi) [ ] - Review pull request
(abc) [x] - Buy groceries

work
(jkl) [ ] - Deploy to staging

$ tasker system status
tasks: 2 unchecked, 1 checked, 0 trash
work: 1 unchecked, 0 checked, 0 trash
─────────────────────────────────
Total: 3 unchecked, 1 checked, 0 trash
```

## Commands

| Command | Description |
|---------|-------------|
| `add <description>` | Add a new task |
| `list` | Display all tasks grouped by list |
| `check <taskId...>` | Mark one or more tasks as completed |
| `uncheck <taskId...>` | Mark one or more tasks as incomplete |
| `delete <taskId...>` | Move one or more tasks to trash |
| `rename <taskId> <description>` | Update task description |
| `move <taskId> <list>` | Move a task to a different list |
| `clear -l <list>` | Move all tasks from a list to trash |
| `lists` | Manage task lists |
| `trash` | View and manage deleted tasks |
| `system status` | Display task statistics per list |

### Global Options

| Option | Description |
|--------|-------------|
| `-l, --list <name>` | Specify which list to use |

### List Command Options

| Option | Description |
|--------|-------------|
| `-c, --checked` | Show only completed tasks |
| `-u, --unchecked` | Show only incomplete tasks |

### Lists Management

```bash
# Show all lists (marks default with *)
tasker lists

# Set default list for new tasks
tasker lists set-default work

# Rename a list
tasker lists rename work projects

# Delete a list (and all its tasks)
tasker lists delete old-list
```

Note: The default list ("tasks") cannot be deleted or renamed.

### Trash Management

```bash
# View all trashed tasks (grouped by list)
tasker trash list

# View trashed tasks from a specific list
tasker trash list -l work

# Restore a task from trash
tasker trash restore abc

# Permanently delete all trashed tasks
tasker trash clear

# Permanently delete trashed tasks from a specific list
tasker trash clear -l work
```

## Data Storage

Tasks are stored in JSON files in a platform-specific config directory:
- macOS: `~/Library/Application Support/cli-tasker/`
- Linux: `~/.config/cli-tasker/`
- Windows: `%APPDATA%/cli-tasker/`

Files:
- `all-tasks.json` - All active tasks (with `ListName` property)
- `all-tasks.trash.json` - Soft-deleted tasks
- `config.json` - User preferences (default list)

## License

MIT
