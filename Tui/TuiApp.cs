namespace cli_tasker.Tui;

using TaskerCore.Data;
using TaskerCore.Models;
using TaskStatus = TaskerCore.Models.TaskStatus;

public class TuiApp
{
    private TuiState _state = new();
    private readonly TuiRenderer _renderer = new();
    private readonly TuiKeyHandler _keyHandler;
    private bool _running = true;
    private List<TodoTask>? _cachedTasks;

    public TuiApp(string? initialList = null)
    {
        _state = _state with { CurrentList = initialList };
        _keyHandler = new TuiKeyHandler(this);
    }

    public void Run()
    {
        // Check for TTY
        if (Console.IsInputRedirected || Console.IsOutputRedirected)
        {
            Output.Error("TUI mode requires an interactive terminal.");
            return;
        }

        // Check minimum terminal size
        if (Console.WindowWidth < 40 || Console.WindowHeight < 10)
        {
            Output.Error($"Terminal too small. Minimum size: 40x10. Current: {Console.WindowWidth}x{Console.WindowHeight}");
            return;
        }

        Console.CursorVisible = false;
        // Use alternate screen buffer to preserve terminal history
        Console.Write("\x1b[?1049h");
        Console.Clear(); // Clear the alternate buffer

        try
        {
            while (_running)
            {
                var tasks = LoadTasks();

                // Ensure cursor is within bounds
                if (tasks.Count > 0 && _state.CursorIndex >= tasks.Count)
                {
                    _state = _state with { CursorIndex = tasks.Count - 1 };
                }
                else if (tasks.Count == 0)
                {
                    _state = _state with { CursorIndex = 0 };
                }

                // Clear expired status message
                _state = _state.ClearStatusIfExpired(TimeSpan.FromSeconds(2));

                _renderer.Render(_state, tasks);

                var key = Console.ReadKey(intercept: true);
                _state = _keyHandler.Handle(key, _state, tasks);
            }
        }
        finally
        {
            Console.CursorVisible = true;
            // Exit alternate screen buffer - restores previous terminal content
            Console.Write("\x1b[?1049l");
        }
    }

    public void Quit() => _running = false;

    public void InvalidateCache() => _cachedTasks = null;

    public void UpdateCachedTask(int index, TodoTask updated)
    {
        if (_cachedTasks != null && index >= 0 && index < _cachedTasks.Count)
            _cachedTasks[index] = updated;
    }

    private List<TodoTask> LoadTasks()
    {
        // Don't use cache during active search — query changes every keystroke
        if (_cachedTasks != null && string.IsNullOrEmpty(_state.SearchQuery))
            return _cachedTasks;

        var taskList = new TodoTaskList(_state.CurrentList);
        var tasks = taskList.GetSortedTasks();

        // Apply search filter
        if (!string.IsNullOrEmpty(_state.SearchQuery))
        {
            tasks = tasks
                .Where(t => t.Description.Contains(_state.SearchQuery, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        // GetSortedTasks() already returns correct order (status-grouped, sort_order within groups).
        // For all-lists view, group by list name while preserving within-list order.
        List<TodoTask> sorted;
        if (_state.CurrentList == null)
        {
            sorted = tasks
                .GroupBy(t => t.ListName)
                .OrderBy(g => g.Key != ListManager.DefaultListName)
                .ThenBy(g => g.Key)
                .SelectMany(g => g)
                .ToList();
        }
        else
        {
            sorted = tasks;
        }

        // Only cache when not searching — search results change per keystroke
        if (string.IsNullOrEmpty(_state.SearchQuery))
            _cachedTasks = sorted;

        return sorted;
    }
}
