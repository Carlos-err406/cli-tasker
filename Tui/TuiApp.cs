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

    private static int StatusSortOrder(TaskStatus status) => status switch
    {
        TaskStatus.InProgress => 0,
        TaskStatus.Pending => 1,
        TaskStatus.Done => 2,
        _ => 1
    };

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

        // Sort: by list name (when viewing all), then by status (in-progress first), then by creation date descending
        List<TodoTask> sorted;
        if (_state.CurrentList == null)
        {
            // Viewing all lists - group by list name, default list first
            sorted = tasks
                .OrderBy(t => t.ListName != ListManager.DefaultListName) // default list first
                .ThenBy(t => t.ListName)
                .ThenBy(t => StatusSortOrder(t.Status))
                .ThenByDescending(t => t.CreatedAt)
                .ToList();
        }
        else
        {
            // Single list view - in-progress first, then pending, then done
            sorted = tasks
                .OrderBy(t => StatusSortOrder(t.Status))
                .ThenByDescending(t => t.CreatedAt)
                .ToList();
        }

        // Only cache when not searching — search results change per keystroke
        if (string.IsNullOrEmpty(_state.SearchQuery))
            _cachedTasks = sorted;

        return sorted;
    }
}
