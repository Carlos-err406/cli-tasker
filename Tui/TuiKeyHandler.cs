namespace cli_tasker.Tui;

using Spectre.Console;

public class TuiKeyHandler
{
    private readonly TuiApp _app;

    public TuiKeyHandler(TuiApp app)
    {
        _app = app;
    }

    public TuiState Handle(ConsoleKeyInfo key, TuiState state, IReadOnlyList<TodoTask> tasks)
    {
        return state.Mode switch
        {
            TuiMode.Normal => HandleNormalMode(key, state, tasks),
            TuiMode.Search => HandleSearchMode(key, state, tasks),
            TuiMode.MultiSelect => HandleMultiSelectMode(key, state, tasks),
            _ => state
        };
    }

    private TuiState HandleNormalMode(ConsoleKeyInfo key, TuiState state, IReadOnlyList<TodoTask> tasks)
    {
        var taskCount = tasks.Count;

        switch (key.Key)
        {
            // Navigation
            case ConsoleKey.J:
            case ConsoleKey.DownArrow:
                if (taskCount == 0) return state;
                return state with { CursorIndex = Math.Min(taskCount - 1, state.CursorIndex + 1) };

            case ConsoleKey.K:
            case ConsoleKey.UpArrow:
                return state with { CursorIndex = Math.Max(0, state.CursorIndex - 1) };

            case ConsoleKey.G:
                if (taskCount == 0) return state;
                if (key.Modifiers == ConsoleModifiers.Shift)
                    return state with { CursorIndex = taskCount - 1 };
                return state with { CursorIndex = 0 };

            // Toggle check
            case ConsoleKey.Spacebar:
            case ConsoleKey.Enter:
                return ToggleTask(state, tasks);

            // Delete
            case ConsoleKey.X:
            case ConsoleKey.Delete:
                return DeleteTask(state, tasks);

            // Rename
            case ConsoleKey.R:
                return RenameTask(state, tasks);

            // Add
            case ConsoleKey.A:
                return AddTask(state);

            // Switch list
            case ConsoleKey.L:
                return SwitchList(state);

            // Move task
            case ConsoleKey.M:
                return MoveTask(state, tasks);

            // Search
            case ConsoleKey.Oem2: // '/' key
                return state with { Mode = TuiMode.Search, SearchQuery = "" };

            // Multi-select
            case ConsoleKey.V:
                return state with { Mode = TuiMode.MultiSelect, SelectedTaskIds = new HashSet<string>() };

            // Quit
            case ConsoleKey.Q:
            case ConsoleKey.Escape:
                _app.Quit();
                return state;

            default:
                return state;
        }
    }

    private TuiState HandleSearchMode(ConsoleKeyInfo key, TuiState state, IReadOnlyList<TodoTask> tasks)
    {
        switch (key.Key)
        {
            case ConsoleKey.Escape:
                return state with { Mode = TuiMode.Normal, SearchQuery = null, CursorIndex = 0 };

            case ConsoleKey.Enter:
                return state with { Mode = TuiMode.Normal };

            case ConsoleKey.Backspace:
                if (!string.IsNullOrEmpty(state.SearchQuery))
                    return state with
                    {
                        SearchQuery = state.SearchQuery[..^1],
                        CursorIndex = 0
                    };
                return state;

            default:
                if (!char.IsControl(key.KeyChar))
                    return state with
                    {
                        SearchQuery = (state.SearchQuery ?? "") + key.KeyChar,
                        CursorIndex = 0
                    };
                return state;
        }
    }

    private TuiState HandleMultiSelectMode(ConsoleKeyInfo key, TuiState state, IReadOnlyList<TodoTask> tasks)
    {
        var taskCount = tasks.Count;

        switch (key.Key)
        {
            // Navigation
            case ConsoleKey.J:
            case ConsoleKey.DownArrow:
                if (taskCount == 0) return state;
                return state with { CursorIndex = Math.Min(taskCount - 1, state.CursorIndex + 1) };

            case ConsoleKey.K:
            case ConsoleKey.UpArrow:
                return state with { CursorIndex = Math.Max(0, state.CursorIndex - 1) };

            // Toggle selection
            case ConsoleKey.Spacebar:
                if (taskCount == 0 || state.CursorIndex >= taskCount) return state;
                var taskId = tasks[state.CursorIndex].Id;
                var newSelection = new HashSet<string>(state.SelectedTaskIds);
                if (newSelection.Contains(taskId))
                    newSelection.Remove(taskId);
                else
                    newSelection.Add(taskId);
                return state with { SelectedTaskIds = newSelection };

            // Bulk delete
            case ConsoleKey.X:
            case ConsoleKey.Delete:
                return BulkDelete(state, tasks);

            // Bulk check
            case ConsoleKey.C:
                return BulkCheck(state, tasks);

            // Bulk uncheck
            case ConsoleKey.U:
                return BulkUncheck(state, tasks);

            // Exit multi-select
            case ConsoleKey.Escape:
            case ConsoleKey.V:
                return state with { Mode = TuiMode.Normal, SelectedTaskIds = new HashSet<string>() };

            // Quit
            case ConsoleKey.Q:
                _app.Quit();
                return state;

            default:
                return state;
        }
    }

    private TuiState ToggleTask(TuiState state, IReadOnlyList<TodoTask> tasks)
    {
        if (tasks.Count == 0 || state.CursorIndex >= tasks.Count)
            return state;

        var task = tasks[state.CursorIndex];
        var taskList = new TodoTaskList(state.CurrentList);

        if (task.IsChecked)
            taskList.UncheckTask(task.Id);
        else
            taskList.CheckTask(task.Id);

        // Keep cursor in place (task moves but cursor stays)
        return state.WithStatusMessage(task.IsChecked ? "Unchecked" : "Checked");
    }

    private TuiState DeleteTask(TuiState state, IReadOnlyList<TodoTask> tasks)
    {
        if (tasks.Count == 0 || state.CursorIndex >= tasks.Count)
            return state;

        var task = tasks[state.CursorIndex];
        var taskList = new TodoTaskList(state.CurrentList);
        taskList.DeleteTask(task.Id);

        var newIndex = Math.Min(state.CursorIndex, Math.Max(0, tasks.Count - 2));
        return state.WithStatusMessage("Deleted (z to undo)") with { CursorIndex = newIndex };
    }

    private TuiState RenameTask(TuiState state, IReadOnlyList<TodoTask> tasks)
    {
        if (tasks.Count == 0 || state.CursorIndex >= tasks.Count)
            return state;

        var task = tasks[state.CursorIndex];

        // Show current task info before prompt
        Console.Clear();
        AnsiConsole.MarkupLine("[dim]Current:[/]");
        foreach (var line in task.Description.Split('\n'))
        {
            AnsiConsole.MarkupLine($"  [dim]{Markup.Escape(line)}[/]");
        }
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]Enter new description (Ctrl+C to cancel):[/]");

        try
        {
            var newDesc = AnsiConsole.Prompt(
                new TextPrompt<string>(">")
                    .DefaultValue(task.Description)
                    .AllowEmpty());

            if (string.IsNullOrWhiteSpace(newDesc))
            {
                return state.WithStatusMessage("Cancelled");
            }

            if (newDesc != task.Description)
            {
                var taskList = new TodoTaskList(state.CurrentList);
                taskList.RenameTask(task.Id, newDesc);
                return state.WithStatusMessage("Renamed");
            }

            return state;
        }
        catch (OperationCanceledException)
        {
            return state.WithStatusMessage("Cancelled");
        }
    }

    private TuiState AddTask(TuiState state)
    {
        Console.Clear();
        var listName = state.CurrentList ?? ListManager.DefaultListName;
        AnsiConsole.MarkupLine($"[dim]Adding to list:[/] [bold]{Markup.Escape(listName)}[/]");
        AnsiConsole.MarkupLine("[dim]Enter description (empty to cancel, Ctrl+C to cancel):[/]");

        try
        {
            var desc = AnsiConsole.Prompt(
                new TextPrompt<string>(">")
                    .AllowEmpty());

            if (string.IsNullOrWhiteSpace(desc))
            {
                return state.WithStatusMessage("Cancelled");
            }

            var task = TodoTask.CreateTodoTask(desc, listName);
            var taskList = new TodoTaskList(state.CurrentList);
            taskList.AddTodoTask(task);
            return state.WithStatusMessage("Added") with { CursorIndex = 0 };
        }
        catch (OperationCanceledException)
        {
            return state.WithStatusMessage("Cancelled");
        }
    }

    private TuiState SwitchList(TuiState state)
    {
        Console.Clear();
        var lists = TodoTaskList.GetAllListNames().ToList();
        lists.Insert(0, "<All Lists>");

        var current = state.CurrentList ?? "<All Lists>";
        var selected = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Switch to list:")
                .AddChoices(lists)
                .UseConverter(s => s == current ? $"{s} [dim](current)[/]" : s));

        var newList = selected == "<All Lists>" ? null : selected;
        return state with { CurrentList = newList, CursorIndex = 0 };
    }

    private TuiState MoveTask(TuiState state, IReadOnlyList<TodoTask> tasks)
    {
        if (tasks.Count == 0 || state.CursorIndex >= tasks.Count)
            return state;

        var task = tasks[state.CursorIndex];

        Console.Clear();
        var lists = TodoTaskList.GetAllListNames().ToList();

        var selected = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title($"Move task to list:")
                .AddChoices(lists)
                .UseConverter(s => s == task.ListName ? $"{s} [dim](current)[/]" : s));

        if (selected != task.ListName)
        {
            var taskList = new TodoTaskList();
            taskList.MoveTask(task.Id, selected);

            // If viewing a specific list, task may disappear
            var newIndex = state.CurrentList != null
                ? Math.Min(state.CursorIndex, Math.Max(0, tasks.Count - 2))
                : 0;
            return state.WithStatusMessage($"Moved to {selected}") with { CursorIndex = newIndex };
        }

        return state;
    }

    private TuiState BulkDelete(TuiState state, IReadOnlyList<TodoTask> tasks)
    {
        if (state.SelectedTaskIds.Count == 0)
            return state;

        var count = state.SelectedTaskIds.Count;
        var taskList = new TodoTaskList();
        taskList.DeleteTasks(state.SelectedTaskIds.ToArray());

        return state.WithStatusMessage($"Deleted {count} tasks (z to undo)") with
        {
            Mode = TuiMode.Normal,
            SelectedTaskIds = new HashSet<string>(),
            CursorIndex = 0
        };
    }

    private TuiState BulkCheck(TuiState state, IReadOnlyList<TodoTask> tasks)
    {
        if (state.SelectedTaskIds.Count == 0)
            return state;

        var taskList = new TodoTaskList();
        taskList.CheckTasks(state.SelectedTaskIds.ToArray());

        return state.WithStatusMessage($"Checked {state.SelectedTaskIds.Count} tasks") with
        {
            Mode = TuiMode.Normal,
            SelectedTaskIds = new HashSet<string>(),
            CursorIndex = 0
        };
    }

    private TuiState BulkUncheck(TuiState state, IReadOnlyList<TodoTask> tasks)
    {
        if (state.SelectedTaskIds.Count == 0)
            return state;

        var taskList = new TodoTaskList();
        taskList.UncheckTasks(state.SelectedTaskIds.ToArray());

        return state.WithStatusMessage($"Unchecked {state.SelectedTaskIds.Count} tasks") with
        {
            Mode = TuiMode.Normal,
            SelectedTaskIds = new HashSet<string>(),
            CursorIndex = 0
        };
    }
}
