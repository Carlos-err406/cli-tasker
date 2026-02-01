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
            TuiMode.InputAdd => HandleInputMode(key, state, isRename: false),
            TuiMode.InputRename => HandleInputMode(key, state, isRename: true),
            _ => state
        };
    }

    private TuiState HandleNormalMode(ConsoleKeyInfo key, TuiState state, IReadOnlyList<TodoTask> tasks)
    {
        var taskCount = tasks.Count;

        switch (key.Key)
        {
            // Navigation (arrow keys only)
            case ConsoleKey.DownArrow:
                if (taskCount == 0) return state;
                return state with { CursorIndex = Math.Min(taskCount - 1, state.CursorIndex + 1) };

            case ConsoleKey.UpArrow:
                return state with { CursorIndex = Math.Max(0, state.CursorIndex - 1) };

            case ConsoleKey.Home:
                return state with { CursorIndex = 0 };

            case ConsoleKey.End:
                if (taskCount == 0) return state;
                return state with { CursorIndex = taskCount - 1 };

            // Toggle check
            case ConsoleKey.Spacebar:
            case ConsoleKey.Enter:
                return ToggleTask(state, tasks);

            // Delete
            case ConsoleKey.X:
            case ConsoleKey.Delete:
                return DeleteTask(state, tasks);

            // Rename (inline)
            case ConsoleKey.R:
                if (taskCount == 0 || state.CursorIndex >= taskCount) return state;
                var taskToRename = tasks[state.CursorIndex];
                return state.StartInputRename(taskToRename.Id, taskToRename.Description);

            // Add (inline)
            case ConsoleKey.A:
                var listName = state.CurrentList ?? ListManager.DefaultListName;
                return state.StartInputAdd(listName);

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
            // Navigation (arrow keys only)
            case ConsoleKey.DownArrow:
                if (taskCount == 0) return state;
                return state with { CursorIndex = Math.Min(taskCount - 1, state.CursorIndex + 1) };

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
                return BulkDelete(state);

            // Bulk check
            case ConsoleKey.C:
                return BulkCheck(state);

            // Bulk uncheck
            case ConsoleKey.U:
                return BulkUncheck(state);

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

    private TuiState HandleInputMode(ConsoleKeyInfo key, TuiState state, bool isRename)
    {
        switch (key.Key)
        {
            case ConsoleKey.Escape:
                return state.CancelInput();

            case ConsoleKey.Enter:
                return ConfirmInput(state, isRename);

            case ConsoleKey.Backspace:
                if (state.InputCursor > 0)
                {
                    var newBuffer = state.InputBuffer.Remove(state.InputCursor - 1, 1);
                    return state with
                    {
                        InputBuffer = newBuffer,
                        InputCursor = state.InputCursor - 1
                    };
                }
                return state;

            case ConsoleKey.Delete:
                if (state.InputCursor < state.InputBuffer.Length)
                {
                    var newBuffer = state.InputBuffer.Remove(state.InputCursor, 1);
                    return state with { InputBuffer = newBuffer };
                }
                return state;

            case ConsoleKey.LeftArrow:
                return state with { InputCursor = Math.Max(0, state.InputCursor - 1) };

            case ConsoleKey.RightArrow:
                return state with { InputCursor = Math.Min(state.InputBuffer.Length, state.InputCursor + 1) };

            case ConsoleKey.Home:
                return state with { InputCursor = 0 };

            case ConsoleKey.End:
                return state with { InputCursor = state.InputBuffer.Length };

            default:
                if (!char.IsControl(key.KeyChar))
                {
                    var newBuffer = state.InputBuffer.Insert(state.InputCursor, key.KeyChar.ToString());
                    return state with
                    {
                        InputBuffer = newBuffer,
                        InputCursor = state.InputCursor + 1
                    };
                }
                return state;
        }
    }

    private TuiState ConfirmInput(TuiState state, bool isRename)
    {
        var text = state.InputBuffer.Trim();

        if (string.IsNullOrWhiteSpace(text))
        {
            return state.CancelInput();
        }

        if (isRename && state.InputTargetTaskId != null)
        {
            var taskList = new TodoTaskList(state.CurrentList);
            taskList.RenameTask(state.InputTargetTaskId, text);
            return (state with
            {
                Mode = TuiMode.Normal,
                InputBuffer = "",
                InputCursor = 0,
                InputTargetTaskId = null
            }).WithStatusMessage("Renamed");
        }
        else if (!isRename)
        {
            var listName = state.CurrentList ?? ListManager.DefaultListName;
            var task = TodoTask.CreateTodoTask(text, listName);
            var taskList = new TodoTaskList(state.CurrentList);
            taskList.AddTodoTask(task);
            return (state with
            {
                Mode = TuiMode.Normal,
                InputBuffer = "",
                InputCursor = 0,
                CursorIndex = 0
            }).WithStatusMessage("Added");
        }

        return state.CancelInput();
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
        return state.WithStatusMessage("Deleted") with { CursorIndex = newIndex };
    }

    private TuiState SwitchList(TuiState state)
    {
        Console.Clear();
        var lists = TodoTaskList.GetAllListNames().ToList();
        lists.Insert(0, "<All Lists>");
        lists.Add("<Cancel>");

        var current = state.CurrentList ?? "<All Lists>";
        var selected = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Switch to list:")
                .AddChoices(lists)
                .UseConverter(s => s == current ? $"{s} [dim](current)[/]" : s));

        if (selected == "<Cancel>")
            return state.WithStatusMessage("Cancelled");

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
        lists.Add("<Cancel>");

        var selected = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title($"Move task to list:")
                .AddChoices(lists)
                .UseConverter(s => s == task.ListName ? $"{s} [dim](current)[/]" : s));

        if (selected == "<Cancel>")
            return state.WithStatusMessage("Cancelled");

        if (selected != task.ListName)
        {
            var taskList = new TodoTaskList();
            taskList.MoveTask(task.Id, selected);

            var newIndex = state.CurrentList != null
                ? Math.Min(state.CursorIndex, Math.Max(0, tasks.Count - 2))
                : 0;
            return state.WithStatusMessage($"Moved to {selected}") with { CursorIndex = newIndex };
        }

        return state;
    }

    private TuiState BulkDelete(TuiState state)
    {
        if (state.SelectedTaskIds.Count == 0)
            return state;

        var count = state.SelectedTaskIds.Count;
        var taskList = new TodoTaskList();
        taskList.DeleteTasks(state.SelectedTaskIds.ToArray());

        return state.WithStatusMessage($"Deleted {count} tasks") with
        {
            Mode = TuiMode.Normal,
            SelectedTaskIds = new HashSet<string>(),
            CursorIndex = 0
        };
    }

    private TuiState BulkCheck(TuiState state)
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

    private TuiState BulkUncheck(TuiState state)
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
