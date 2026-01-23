# cli-tasker

A lightweight command-line task manager built with C# and .NET.

## Features

- Add, list, delete, and manage tasks from the terminal
- Mark tasks as complete or incomplete
- Persistent storage via JSON file
- Simple 3-character task IDs for easy reference

## Requirements

- .NET 10.0 or later

## Installation

```bash
git clone <repository-url>
cd cli-tasker
dotnet build
```

## Usage

```bash
# Add a new task
dotnet run -- add "Buy groceries"

# List all tasks
dotnet run -- list

# Mark a task as complete
dotnet run -- check abc

# Mark a task as incomplete
dotnet run -- uncheck abc

# Delete a task
dotnet run -- delete abc
```

### Example Output

```
$ dotnet run -- list
[x] abc  Buy groceries
[ ] def  Write documentation
[ ] ghi  Review pull request
```

## Commands

| Command | Description |
|---------|-------------|
| `add <description>` | Add a new task |
| `list` | Display all tasks |
| `check <taskId>` | Mark a task as completed |
| `uncheck <taskId>` | Mark a task as incomplete |
| `delete <taskId>` | Remove a task |

## Data Storage

Tasks are stored in `~/.config/cli-tasker/tasks.json`. The directory and file are created automatically on first use.

## Project Structure

```
cli-tasker/
├── Program.cs           # Entry point and CLI setup
├── AppCommands/         # Command implementations
│   ├── AddCommand.cs
│   ├── ListCommand.cs
│   ├── DeleteCommand.cs
│   └── CheckCommand.cs
└── TodoTask/            # Core data models
    ├── TodoTask.cs
    └── TodoTaskList.cs
```

## License

MIT
