namespace cli_tasker.Tui;

using Spectre.Console;

public class TuiRenderer
{
    private const int TaskPrefixLength = 12;

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

        // Calculate viewport
        var startIndex = Math.Max(0, state.CursorIndex - availableLines / 2);
        startIndex = Math.Min(startIndex, Math.Max(0, tasks.Count - availableLines));
        var endIndex = Math.Min(tasks.Count, startIndex + availableLines);

        var linesRendered = 0;
        for (var i = startIndex; i < endIndex; i++)
        {
            var task = tasks[i];
            var isSelected = i == state.CursorIndex;
            var isMultiSelected = state.SelectedTaskIds.Contains(task.Id);

            RenderTask(task, isSelected, isMultiSelected, state.Mode, state.SearchQuery);
            linesRendered++;
        }

        // Clear remaining lines
        for (var i = linesRendered; i < availableLines; i++)
            ClearLine();
    }

    private void RenderTask(TodoTask task, bool isSelected, bool isMultiSelected, TuiMode mode, string? searchQuery)
    {
        var selectionIndicator = mode == TuiMode.MultiSelect
            ? (isMultiSelected ? "[blue][[*]][/]" : "[dim][[ ]][/]")
            : "";

        var cursor = isSelected ? "[bold blue]>[/]" : " ";
        var checkbox = task.IsChecked ? "[green][[x]][/]" : "[grey][[ ]][/]";
        var taskId = $"[dim]({task.Id})[/]";

        // Get first line of description, highlight search matches
        var lines = task.Description.Split('\n');
        var firstLine = lines[0];

        // Highlight search query in description
        if (!string.IsNullOrEmpty(searchQuery))
        {
            var escapedLine = Markup.Escape(firstLine);
            var idx = firstLine.IndexOf(searchQuery, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                var before = Markup.Escape(firstLine[..idx]);
                var match = Markup.Escape(firstLine.Substring(idx, searchQuery.Length));
                var after = Markup.Escape(firstLine[(idx + searchQuery.Length)..]);
                firstLine = $"{before}[yellow bold]{match}[/]{after}";
            }
            else
            {
                firstLine = escapedLine;
            }
        }
        else
        {
            firstLine = Markup.Escape(firstLine);
        }

        var description = isSelected
            ? $"[bold]{firstLine}[/]"
            : (task.IsChecked ? $"[dim strikethrough]{firstLine}[/]" : firstLine);

        // Truncate if too long
        var maxWidth = Console.WindowWidth - TaskPrefixLength - 10;
        if (firstLine.Length > maxWidth && maxWidth > 3)
        {
            // Can't truncate markup easily, just render as-is
        }

        var multiLineIndicator = lines.Length > 1 ? "[dim]...[/]" : "";

        AnsiConsole.MarkupLine($"{cursor}{selectionIndicator}{taskId} {checkbox} {description}{multiLineIndicator}");
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
