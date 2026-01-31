namespace cli_tasker.Tui;

public class TuiApp
{
    private TuiState _state = new();
    private readonly TuiRenderer _renderer = new();
    private readonly TuiKeyHandler _keyHandler;
    private bool _running = true;

    public TuiApp(string? initialList = null)
    {
        _state = _state with { CurrentList = initialList };
        _keyHandler = new TuiKeyHandler(this);
    }

    public void Run()
    {
        // Check for TTY
        if (Console.IsInputRedirected)
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
        _renderer.Clear();

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
            Console.Clear();
        }
    }

    public void Quit() => _running = false;

    private List<TodoTask> LoadTasks()
    {
        var taskList = new TodoTaskList(_state.CurrentList);
        var tasks = taskList.GetAllTasks();

        // Apply search filter
        if (!string.IsNullOrEmpty(_state.SearchQuery))
        {
            tasks = tasks
                .Where(t => t.Description.Contains(_state.SearchQuery, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        // Sort: unchecked first, then by creation date descending
        return tasks
            .OrderBy(t => t.IsChecked)
            .ThenByDescending(t => t.CreatedAt)
            .ToList();
    }
}
