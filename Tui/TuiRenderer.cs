namespace cli_tasker.Tui;

using Spectre.Console;

public class TuiRenderer
{

    public void Render(TuiState state, IReadOnlyList<TodoTask> tasks)
    {
        Console.SetCursorPosition(0, 0);

        RenderHeader(state, tasks.Count);
        RenderTasks(state, tasks);
        RenderStatusBar(state, tasks.Count);
    }

    private void RenderHeader(TuiState state, int taskCount)
    {
        var listName = state.CurrentList ?? "all lists";
        var modeIndicator = state.Mode switch
        {
            TuiMode.Search => $" [yellow]/[/][bold]{Markup.Escape(state.SearchQuery ?? "")}[/]",
            TuiMode.MultiSelect => $" [blue]({state.SelectedTaskIds.Count} selected)[/]",
            _ => ""
        };

        AnsiConsole.MarkupLine($"[bold underline]tasker[/] [dim]([/]{Markup.Escape(listName)}[dim])[/]{modeIndicator}");
        AnsiConsole.WriteLine();
    }

    private void RenderTasks(TuiState state, IReadOnlyList<TodoTask> tasks)
    {
        var terminalHeight = Console.WindowHeight;
        var availableLines = terminalHeight - 6; // Header (2) + status bar (3) + padding

        if (tasks.Count == 0)
        {
            AnsiConsole.MarkupLine("[dim]No tasks. Press [bold]a[/] to add one.[/]");
            // Fill remaining space
            for (var i = 0; i < availableLines - 1; i++)
                ClearLine();
            return;
        }

        // Group tasks by list if viewing all lists
        var showingAllLists = state.CurrentList == null;

        // Calculate viewport - simple task-based viewport (not line-based for simplicity)
        var startIndex = Math.Max(0, state.CursorIndex - availableLines / 2);
        startIndex = Math.Min(startIndex, Math.Max(0, tasks.Count - availableLines));
        var endIndex = Math.Min(tasks.Count, startIndex + availableLines);

        var linesRendered = 0;
        string? lastListName = null;

        for (var i = startIndex; i < endIndex && linesRendered < availableLines; i++)
        {
            var task = tasks[i];

            // Render list header when switching to a new list (only when viewing all lists)
            if (showingAllLists && task.ListName != lastListName)
            {
                if (linesRendered >= availableLines) break;
                AnsiConsole.MarkupLine($"[bold cyan]── {Markup.Escape(task.ListName)} ──[/]");
                linesRendered++;
                lastListName = task.ListName;
            }

            if (linesRendered >= availableLines) break;

            var isSelected = i == state.CursorIndex;
            var isMultiSelected = state.SelectedTaskIds.Contains(task.Id);

            var taskLines = RenderTask(task, isSelected, isMultiSelected, state.Mode, state.SearchQuery);
            linesRendered += taskLines;
        }

        // Clear remaining lines
        for (var i = linesRendered; i < availableLines; i++)
            ClearLine();
    }

    private int RenderTask(TodoTask task, bool isSelected, bool isMultiSelected, TuiMode mode, string? searchQuery)
    {
        var selectionIndicator = mode == TuiMode.MultiSelect
            ? (isMultiSelected ? "[blue][[*]][/]" : "[dim][[ ]][/]")
            : "";

        var cursor = isSelected ? "[bold blue]>[/]" : " ";
        var checkbox = task.IsChecked ? "[green][[x]][/]" : "[grey][[ ]][/]";
        var taskId = $"[dim]({task.Id})[/]";

        // Calculate prefix length for indent: cursor(1) + selection(0 or 3) + id(5) + space(1) + checkbox(3) + space(1)
        var prefixLength = 1 + (mode == TuiMode.MultiSelect ? 3 : 0) + 5 + 1 + 3 + 1;
        var indent = new string(' ', prefixLength);

        // Get all lines of description
        var lines = task.Description.Split('\n');
        var linesRendered = 0;

        // Render first line with full prefix
        var firstLine = HighlightSearch(lines[0], searchQuery);
        var description = isSelected
            ? $"[bold]{firstLine}[/]"
            : (task.IsChecked ? $"[dim strikethrough]{firstLine}[/]" : firstLine);

        AnsiConsole.MarkupLine($"{cursor}{selectionIndicator}{taskId} {checkbox} {description}");
        linesRendered++;

        // Render continuation lines with indent
        if (lines.Length > 1)
        {
            var style = task.IsChecked ? "[dim strikethrough]" : "[dim]";
            for (var i = 1; i < lines.Length; i++)
            {
                var continuationLine = HighlightSearch(lines[i], searchQuery);
                AnsiConsole.MarkupLine($"{indent}{style}{continuationLine}[/]");
                linesRendered++;
            }
        }

        return linesRendered;
    }

    private static string HighlightSearch(string text, string? searchQuery)
    {
        if (string.IsNullOrEmpty(searchQuery))
        {
            return Markup.Escape(text);
        }

        var idx = text.IndexOf(searchQuery, StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            var before = Markup.Escape(text[..idx]);
            var match = Markup.Escape(text.Substring(idx, searchQuery.Length));
            var after = Markup.Escape(text[(idx + searchQuery.Length)..]);
            return $"{before}[yellow bold]{match}[/]{after}";
        }

        return Markup.Escape(text);
    }

    private void RenderStatusBar(TuiState state, int taskCount)
    {
        AnsiConsole.WriteLine();

        // Status message or hints
        if (!string.IsNullOrEmpty(state.StatusMessage))
        {
            AnsiConsole.MarkupLine($"[green]{Markup.Escape(state.StatusMessage)}[/]");
        }
        else
        {
            ClearLine();
        }

        // Key hints based on mode
        var hints = state.Mode switch
        {
            TuiMode.Normal => "[dim]j/k[/]:nav [dim]space[/]:toggle [dim]x[/]:del [dim]r[/]:rename [dim]a[/]:add [dim]l[/]:lists [dim]m[/]:move [dim]/[/]:search [dim]v[/]:select [dim]q[/]:quit",
            TuiMode.Search => "[dim]type[/]:filter [dim]enter[/]:done [dim]esc[/]:clear",
            TuiMode.MultiSelect => "[dim]space[/]:toggle [dim]x[/]:del [dim]c[/]:check [dim]u[/]:uncheck [dim]esc[/]:exit",
            _ => ""
        };

        AnsiConsole.MarkupLine(hints);
    }

    private static void ClearLine()
    {
        var width = Console.WindowWidth;
        Console.Write(new string(' ', width));
        Console.SetCursorPosition(0, Console.CursorTop + 1);
    }

    public void Clear()
    {
        Console.Clear();
    }
}
