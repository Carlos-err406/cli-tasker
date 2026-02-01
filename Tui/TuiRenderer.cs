namespace cli_tasker.Tui;

using Spectre.Console;

public class TuiRenderer
{
    // ANSI escape code to clear from cursor to end of line
    private const string ClearToEndOfLine = "\x1b[K";

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
            TuiMode.InputAdd => " [yellow]+ new task[/]",
            TuiMode.InputRename => " [yellow]editing[/]",
            _ => ""
        };

        WriteLineCleared($"[bold underline]tasker[/] [dim]([/]{Markup.Escape(listName)}[dim])[/]{modeIndicator}");
        ClearLine(); // Empty line after header
    }

    private void RenderTasks(TuiState state, IReadOnlyList<TodoTask> tasks)
    {
        var terminalHeight = Console.WindowHeight;
        // Reserve extra space for multiline input (3 lines + hint)
        var inputModeExtraSpace = (state.Mode == TuiMode.InputAdd || state.Mode == TuiMode.InputRename) ? 4 : 0;
        var availableLines = terminalHeight - 6 - inputModeExtraSpace;

        if (tasks.Count == 0)
        {
            WriteLineCleared("[dim]No tasks. Press [bold]a[/] to add one.[/]");
            for (var i = 0; i < availableLines - 1; i++)
                ClearLine();
            return;
        }

        var showingAllLists = state.CurrentList == null;
        var startIndex = Math.Max(0, state.CursorIndex - availableLines / 2);
        startIndex = Math.Min(startIndex, Math.Max(0, tasks.Count - availableLines));
        var endIndex = Math.Min(tasks.Count, startIndex + availableLines);

        var linesRendered = 0;
        string? lastListName = null;

        for (var i = startIndex; i < endIndex && linesRendered < availableLines; i++)
        {
            var task = tasks[i];

            if (showingAllLists && task.ListName != lastListName)
            {
                if (linesRendered >= availableLines) break;
                WriteLineCleared($"[bold cyan]── {Markup.Escape(task.ListName)} ──[/]");
                linesRendered++;
                lastListName = task.ListName;
            }

            if (linesRendered >= availableLines) break;

            var isSelected = i == state.CursorIndex;
            var isMultiSelected = state.SelectedTaskIds.Contains(task.Id);

            var taskLines = RenderTask(task, isSelected, isMultiSelected, state.Mode, state.SearchQuery);
            linesRendered += taskLines;
        }

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

        var prefixLength = 1 + (mode == TuiMode.MultiSelect ? 3 : 0) + 5 + 1 + 3 + 1;
        var indent = new string(' ', prefixLength);

        var lines = task.Description.Split('\n');
        var linesRendered = 0;

        var firstLine = HighlightSearch(lines[0], searchQuery);
        var description = isSelected
            ? $"[bold]{firstLine}[/]"
            : (task.IsChecked ? $"[dim strikethrough]{firstLine}[/]" : firstLine);

        WriteLineCleared($"{cursor}{selectionIndicator}{taskId} {checkbox} {description}");
        linesRendered++;

        if (lines.Length > 1)
        {
            for (var i = 1; i < lines.Length; i++)
            {
                var continuationLine = HighlightSearch(lines[i], searchQuery);
                if (isSelected)
                {
                    // Dimmed but NOT strikethrough when selected
                    WriteLineCleared($"{indent}[dim]{continuationLine}[/]");
                }
                else if (task.IsChecked)
                {
                    WriteLineCleared($"{indent}[dim strikethrough]{continuationLine}[/]");
                }
                else
                {
                    WriteLineCleared($"{indent}[dim]{continuationLine}[/]");
                }
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
        ClearLine(); // Empty line before status bar

        if (state.Mode == TuiMode.InputAdd || state.Mode == TuiMode.InputRename)
        {
            RenderInputField(state);
            return;
        }

        if (!string.IsNullOrEmpty(state.StatusMessage))
        {
            WriteLineCleared($"[green]{Markup.Escape(state.StatusMessage)}[/]");
        }
        else
        {
            ClearLine();
        }

        var hints = state.Mode switch
        {
            TuiMode.Normal => "[dim]↑↓[/]:nav [dim]space[/]:toggle [dim]x[/]:del [dim]z[/]:undo [dim]Z[/]:redo [dim]a[/]:add [dim]r[/]:rename [dim]q[/]:quit",
            TuiMode.Search => "[dim]type[/]:filter [dim]enter[/]:done [dim]esc[/]:clear",
            TuiMode.MultiSelect => "[dim]space[/]:toggle [dim]x[/]:del [dim]c[/]:check [dim]u[/]:uncheck [dim]esc[/]:exit",
            _ => ""
        };

        WriteLineCleared(hints);
    }

    private void RenderInputField(TuiState state)
    {
        if (!string.IsNullOrEmpty(state.StatusMessage))
        {
            WriteLineCleared($"[dim]{Markup.Escape(state.StatusMessage)}[/]");
        }
        else
        {
            ClearLine();
        }

        var buffer = state.InputBuffer;
        var cursorPos = state.InputCursor;
        var maxWidth = Console.WindowWidth - 4;

        // Split buffer into lines for multiline display
        var lines = buffer.Split('\n');
        var currentLineIndex = 0;
        var posInLine = cursorPos;

        // Find which line the cursor is on
        var charCount = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            if (cursorPos <= charCount + lines[i].Length)
            {
                currentLineIndex = i;
                posInLine = cursorPos - charCount;
                break;
            }
            charCount += lines[i].Length + 1; // +1 for newline
        }

        // Render each line (max 3 lines shown to keep UI compact)
        var startLine = Math.Max(0, currentLineIndex - 1);
        var endLine = Math.Min(lines.Length, startLine + 3);

        for (var i = startLine; i < endLine; i++)
        {
            var line = lines[i];
            var prefix = i == 0 ? "[bold]>[/] " : "  ";
            var isCursorLine = i == currentLineIndex;

            if (isCursorLine)
            {
                // Render line with cursor highlight
                var cursorInLine = Math.Min(posInLine, line.Length);
                var beforeCursor = line[..cursorInLine];
                var atCursor = cursorInLine < line.Length ? line[cursorInLine].ToString() : " ";
                var afterCursor = cursorInLine < line.Length - 1 ? line[(cursorInLine + 1)..] : "";

                Console.Write(ClearToEndOfLine);
                AnsiConsole.Markup($"{prefix}{Markup.Escape(beforeCursor)}[bold underline]{Markup.Escape(atCursor)}[/]{Markup.Escape(afterCursor)}");
                Console.WriteLine();
            }
            else
            {
                // Render line without cursor
                var displayLine = line.Length > maxWidth ? line[..maxWidth] + "…" : line;
                WriteLineCleared($"{prefix}[dim]{Markup.Escape(displayLine)}[/]");
            }
        }

        // Clear remaining lines if fewer than 3
        for (var i = endLine - startLine; i < 3; i++)
            ClearLine();

        WriteLineCleared("[dim]^S[/]:save [dim]enter[/]:newline [dim]esc[/]:cancel [dim]⌥←→[/]:word");
    }

    /// <summary>
    /// Write markup and clear to end of line, then move to next line
    /// </summary>
    private static void WriteLineCleared(string markup)
    {
        AnsiConsole.Markup(markup);
        Console.Write(ClearToEndOfLine);
        Console.WriteLine();
    }

    /// <summary>
    /// Clear entire line and move to next line
    /// </summary>
    private static void ClearLine()
    {
        Console.Write(ClearToEndOfLine);
        Console.WriteLine();
    }

    public void Clear()
    {
        Console.Clear();
    }
}
