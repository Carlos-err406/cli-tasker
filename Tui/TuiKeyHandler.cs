namespace cli_tasker.Tui;

using Spectre.Console;
using TaskerCore.Data;
using TaskerCore.Models;
using TaskerCore.Undo;

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
            TuiMode.SelectMoveTarget => HandleSelectMoveMode(key, state),
            TuiMode.SelectList => HandleSelectListMode(key, state),
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
                return StartSwitchList(state);

            // Move task
            case ConsoleKey.M:
                return StartMoveTask(state, tasks);

            // Search
            case ConsoleKey.Oem2: // '/' key
                return state with { Mode = TuiMode.Search, SearchQuery = "" };

            // Multi-select
            case ConsoleKey.V:
                return state with { Mode = TuiMode.MultiSelect, SelectedTaskIds = new HashSet<string>() };

            // Redo (Shift+Z)
            case ConsoleKey.Z when (key.Modifiers & ConsoleModifiers.Shift) != 0:
                return PerformRedo(state);

            // Undo (z)
            case ConsoleKey.Z:
                return PerformUndo(state);

            // Quit
            case ConsoleKey.Q:
            case ConsoleKey.Escape:
                _app.Quit();
                return state;

            default:
                return state;
        }
    }

    private static TuiState PerformUndo(TuiState state)
    {
        var desc = UndoManager.Instance.Undo();
        return desc != null
            ? state.WithStatusMessage($"Undone: {desc}")
            : state.WithStatusMessage("Nothing to undo");
    }

    private static TuiState PerformRedo(TuiState state)
    {
        var desc = UndoManager.Instance.Redo();
        return desc != null
            ? state.WithStatusMessage($"Redone: {desc}")
            : state.WithStatusMessage("Nothing to redo");
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
        var hasAlt = (key.Modifiers & ConsoleModifiers.Alt) != 0;
        var hasShift = (key.Modifiers & ConsoleModifiers.Shift) != 0;

        switch (key.Key)
        {
            case ConsoleKey.Escape:
                return state.CancelInput();

            case ConsoleKey.S when (key.Modifiers & ConsoleModifiers.Control) != 0:
                // Ctrl+S = save/confirm
                return ConfirmInput(state, isRename);

            case ConsoleKey.Enter:
            {
                // Enter = insert newline
                var buffer = state.InputBuffer.Insert(state.InputCursor, "\n");
                return state with { InputBuffer = buffer, InputCursor = state.InputCursor + 1 };
            }

            case ConsoleKey.Backspace:
            {
                if (hasAlt)
                {
                    // Delete word backward
                    var wordStart = FindWordBoundaryBackward(state.InputBuffer, state.InputCursor);
                    if (wordStart < state.InputCursor)
                    {
                        var buffer = state.InputBuffer.Remove(wordStart, state.InputCursor - wordStart);
                        return state with { InputBuffer = buffer, InputCursor = wordStart };
                    }
                }
                else if (state.InputCursor > 0)
                {
                    var buffer = state.InputBuffer.Remove(state.InputCursor - 1, 1);
                    return state with { InputBuffer = buffer, InputCursor = state.InputCursor - 1 };
                }
                return state;
            }

            case ConsoleKey.Delete:
            {
                if (state.InputCursor < state.InputBuffer.Length)
                {
                    var buffer = state.InputBuffer.Remove(state.InputCursor, 1);
                    return state with { InputBuffer = buffer };
                }
                return state;
            }

            case ConsoleKey.LeftArrow:
                if (hasAlt)
                {
                    // Jump to previous word
                    return state with { InputCursor = FindWordBoundaryBackward(state.InputBuffer, state.InputCursor) };
                }
                return state with { InputCursor = Math.Max(0, state.InputCursor - 1) };

            case ConsoleKey.RightArrow:
                if (hasAlt)
                {
                    // Jump to next word
                    return state with { InputCursor = FindWordBoundaryForward(state.InputBuffer, state.InputCursor) };
                }
                return state with { InputCursor = Math.Min(state.InputBuffer.Length, state.InputCursor + 1) };

            case ConsoleKey.UpArrow:
                return state with { InputCursor = MoveLineUp(state.InputBuffer, state.InputCursor) };

            case ConsoleKey.DownArrow:
                return state with { InputCursor = MoveLineDown(state.InputBuffer, state.InputCursor) };

            case ConsoleKey.Home:
                return state with { InputCursor = 0 };

            case ConsoleKey.End:
                return state with { InputCursor = state.InputBuffer.Length };

            default:
            {
                if (!char.IsControl(key.KeyChar))
                {
                    var buffer = state.InputBuffer.Insert(state.InputCursor, key.KeyChar.ToString());
                    return state with { InputBuffer = buffer, InputCursor = state.InputCursor + 1 };
                }
                return state;
            }
        }
    }

    private static int FindWordBoundaryBackward(string text, int position)
    {
        if (position <= 0) return 0;

        var i = position - 1;
        // Skip whitespace
        while (i > 0 && char.IsWhiteSpace(text[i])) i--;
        // Skip word characters
        while (i > 0 && !char.IsWhiteSpace(text[i - 1])) i--;
        return i;
    }

    private static int FindWordBoundaryForward(string text, int position)
    {
        if (position >= text.Length) return text.Length;

        var i = position;
        // Skip current word characters
        while (i < text.Length && !char.IsWhiteSpace(text[i])) i++;
        // Skip whitespace
        while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
        return i;
    }

    private static int MoveLineUp(string text, int position)
    {
        // Find start of current line
        var lineStart = text.LastIndexOf('\n', Math.Max(0, position - 1));
        if (lineStart < 0) return position; // Already on first line

        var colInCurrentLine = position - lineStart - 1;

        // Find start of previous line
        var prevLineStart = text.LastIndexOf('\n', Math.Max(0, lineStart - 1));
        prevLineStart = prevLineStart < 0 ? 0 : prevLineStart + 1;

        var prevLineLength = lineStart - prevLineStart;
        return prevLineStart + Math.Min(colInCurrentLine, prevLineLength);
    }

    private static int MoveLineDown(string text, int position)
    {
        // Find start of current line
        var lineStart = text.LastIndexOf('\n', Math.Max(0, position - 1));
        lineStart = lineStart < 0 ? 0 : lineStart + 1;

        var colInCurrentLine = position - lineStart;

        // Find end of current line (next newline)
        var lineEnd = text.IndexOf('\n', position);
        if (lineEnd < 0) return position; // Already on last line

        // Find end of next line
        var nextLineEnd = text.IndexOf('\n', lineEnd + 1);
        var nextLineLength = (nextLineEnd < 0 ? text.Length : nextLineEnd) - (lineEnd + 1);

        return lineEnd + 1 + Math.Min(colInCurrentLine, nextLineLength);
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

    private static TuiState StartSwitchList(TuiState state)
    {
        var lists = TodoTaskList.GetAllListNames().ToList();
        lists.Insert(0, "<All Lists>");
        var current = state.CurrentList ?? "<All Lists>";
        return state.StartSelectList(current, lists.ToArray());
    }

    private static TuiState StartMoveTask(TuiState state, IReadOnlyList<TodoTask> tasks)
    {
        if (tasks.Count == 0 || state.CursorIndex >= tasks.Count)
            return state;

        var task = tasks[state.CursorIndex];
        var lists = TodoTaskList.GetAllListNames().ToArray();
        return state.StartSelectMoveTarget(task.Id, task.ListName, lists);
    }

    private TuiState HandleSelectMoveMode(ConsoleKeyInfo key, TuiState state)
    {
        switch (key.Key)
        {
            case ConsoleKey.Escape:
                return state.CancelSelect();

            case ConsoleKey.DownArrow:
                return state with { SelectCursor = Math.Min(state.SelectOptions.Length - 1, state.SelectCursor + 1) };

            case ConsoleKey.UpArrow:
                return state with { SelectCursor = Math.Max(0, state.SelectCursor - 1) };

            case ConsoleKey.Enter:
            case ConsoleKey.Spacebar:
                var selected = state.SelectOptions[state.SelectCursor];
                if (selected == state.SelectCurrentValue)
                {
                    return state.CancelSelect() with { StatusMessage = "Already in this list" };
                }

                var taskList = new TodoTaskList();
                taskList.MoveTask(state.SelectTargetTaskId!, selected);

                return (state with
                {
                    Mode = TuiMode.Normal,
                    SelectOptions = Array.Empty<string>(),
                    SelectCursor = 0,
                    SelectTargetTaskId = null,
                    SelectCurrentValue = null,
                    CursorIndex = 0
                }).WithStatusMessage($"Moved to {selected}");

            case ConsoleKey.Q:
                _app.Quit();
                return state;

            default:
                return state;
        }
    }

    private TuiState HandleSelectListMode(ConsoleKeyInfo key, TuiState state)
    {
        switch (key.Key)
        {
            case ConsoleKey.Escape:
                return state.CancelSelect();

            case ConsoleKey.DownArrow:
                return state with { SelectCursor = Math.Min(state.SelectOptions.Length - 1, state.SelectCursor + 1) };

            case ConsoleKey.UpArrow:
                return state with { SelectCursor = Math.Max(0, state.SelectCursor - 1) };

            case ConsoleKey.Enter:
            case ConsoleKey.Spacebar:
                var selected = state.SelectOptions[state.SelectCursor];
                var newList = selected == "<All Lists>" ? null : selected;

                return (state with
                {
                    Mode = TuiMode.Normal,
                    CurrentList = newList,
                    SelectOptions = Array.Empty<string>(),
                    SelectCursor = 0,
                    SelectCurrentValue = null,
                    CursorIndex = 0
                }).WithStatusMessage($"Switched to {selected}");

            case ConsoleKey.Q:
                _app.Quit();
                return state;

            default:
                return state;
        }
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
