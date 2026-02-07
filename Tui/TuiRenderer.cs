namespace cli_tasker.Tui;

using System.IO;
using Spectre.Console;
using TaskerCore.Models;
using TaskerCore.Parsing;
using TaskerCore.Utilities;
using TaskStatus = TaskerCore.Models.TaskStatus;

public class TuiRenderer
{
    // ANSI escape code to clear from cursor to end of line
    private const string ClearToEndOfLine = "\x1b[K";

    // Frame buffer: all rendering writes here, flushed in one shot
    private StringWriter _buffer = new();
    private IAnsiConsole _ansi = null!;

    public void Render(TuiState state, IReadOnlyList<TodoTask> tasks)
    {
        _buffer = new StringWriter();
        _buffer.NewLine = "\n";
        _ansi = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.Yes,
            ColorSystem = ColorSystemSupport.TrueColor,
            Out = new AnsiConsoleOutput(_buffer),
        });
        _ansi.Profile.Width = int.MaxValue; // never wrap — let terminal handle overflow

        RenderHeader(state, tasks.Count);
        RenderTasks(state, tasks);
        RenderStatusBar(state, tasks.Count);

        Console.SetCursorPosition(0, 0);
        Console.Write(_buffer.ToString());
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
        // Reserve extra space for multiline input (3 lines + hint) or selection (title + 5 options + hint)
        var inputModeExtraSpace = (state.Mode == TuiMode.InputAdd || state.Mode == TuiMode.InputRename) ? 4 :
                                   state.Mode == TuiMode.InputDueDate ? 2 : 0;
        var selectModeExtraSpace = (state.Mode == TuiMode.SelectMoveTarget || state.Mode == TuiMode.SelectList) ? 7 : 0;
        var availableLines = Math.Max(1, terminalHeight - 6 - inputModeExtraSpace - selectModeExtraSpace);

        if (tasks.Count == 0)
        {
            WriteLineCleared("[dim]No tasks. Press [bold]a[/] to add one.[/]");
            for (var i = 0; i < availableLines - 1; i++)
                ClearLine();
            return;
        }

        var showingAllLists = state.CurrentList == null;

        // Pre-compute line heights (accounting for multi-line descriptions, wrapping, and list headers)
        var prefixLen = 1 + (state.Mode == TuiMode.MultiSelect ? 3 : 0) + 5 + 1 + 3 + 3 + 1;
        var wrapWidth = Math.Max(10, Console.WindowWidth - prefixLen);
        var lineHeights = new int[tasks.Count];
        string? preGroup = null;
        for (var i = 0; i < tasks.Count; i++)
        {
            var height = CountTaskLines(tasks[i], wrapWidth);
            if (showingAllLists && tasks[i].ListName != preGroup)
            {
                height += 1; // list group header
                preGroup = tasks[i].ListName;
            }
            lineHeights[i] = height;
        }

        var (startIndex, endIndex) = ComputeViewport(state.CursorIndex, lineHeights, availableLines);

        var linesRendered = 0;
        // Initialize from context before startIndex so headers match pre-computation
        string? lastListName = showingAllLists && startIndex > 0 ? tasks[startIndex - 1].ListName : null;

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

            var remaining = availableLines - linesRendered;
            var taskLines = RenderTask(task, isSelected, isMultiSelected, state.Mode, state.SearchQuery, remaining);
            linesRendered += taskLines;
        }

        for (var i = linesRendered; i < availableLines; i++)
            ClearLine();
    }

    private int RenderTask(TodoTask task, bool isSelected, bool isMultiSelected, TuiMode mode, string? searchQuery, int maxLines = int.MaxValue)
    {
        var selectionIndicator = mode == TuiMode.MultiSelect
            ? (isMultiSelected ? "[blue][[*]][/]" : "[dim][[ ]][/]")
            : "";

        var cursor = isSelected ? "[bold blue]>[/]" : " ";
        var checkbox = task.Status switch
        {
            TaskStatus.Done => "[green][[x]][/]",
            TaskStatus.InProgress => "[yellow][[-]][/]",
            _ => "[grey][[ ]][/]"
        };
        var taskId = $"[dim]({task.Id})[/]";
        var priority = FormatPriority(task.Priority);
        var dueDate = FormatDueDate(task.DueDate);
        var tags = FormatTags(task.Tags);

        var prefixLength = 1 + (mode == TuiMode.MultiSelect ? 3 : 0) + 5 + 1 + 3 + 3 + 1; // +3 for priority indicator
        var indent = new string(' ', prefixLength);

        var displayDesc = TaskDescriptionParser.GetDisplayDescription(task.Description);
        var lines = displayDesc.Split('\n');
        var linesRendered = 0;

        var firstLine = HighlightSearch(lines[0], searchQuery);
        var description = isSelected
            ? $"[bold]{firstLine}[/]"
            : (task.Status == TaskStatus.Done ? $"[dim strikethrough]{firstLine}[/]" : firstLine);

        WriteLineCleared($"{cursor}{selectionIndicator}{taskId} {priority} {checkbox} {description}{dueDate}{tags}");
        linesRendered++;

        if (lines.Length > 1)
        {
            var wrapWidth = Math.Max(10, Console.WindowWidth - indent.Length);
            for (var i = 1; i < lines.Length && linesRendered < maxLines; i++)
            {
                var wrappedSegments = WrapLine(lines[i], wrapWidth);
                foreach (var segment in wrappedSegments)
                {
                    if (linesRendered >= maxLines) break;
                    var continuationLine = HighlightSearch(segment, searchQuery);
                    if (isSelected)
                    {
                        // Dimmed but NOT strikethrough when selected
                        WriteLineCleared($"{indent}[dim]{continuationLine}[/]");
                    }
                    else if (task.Status == TaskStatus.Done)
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
        }

        // Relationship indicator: subtask of parent
        if (task.ParentId != null && linesRendered < maxLines)
        {
            WriteLineCleared($"{indent}[dim]^ Subtask of ({task.ParentId})[/]");
            linesRendered++;
        }

        return linesRendered;
    }

    private static int CountTaskLines(TodoTask task, int wrapWidth)
    {
        var displayDesc = TaskDescriptionParser.GetDisplayDescription(task.Description);
        var lines = displayDesc.Split('\n');
        var count = 1; // first line is always 1 visual line
        for (var i = 1; i < lines.Length; i++)
            count += WrapLine(lines[i], wrapWidth).Count;
        // Relationship indicator line
        if (task.ParentId != null)
            count++;
        return count;
    }

    /// <summary>
    /// Word-wrap a single line of text to fit within maxWidth characters.
    /// Breaks at spaces when possible, mid-word only as a last resort.
    /// </summary>
    internal static List<string> WrapLine(string line, int maxWidth)
    {
        if (maxWidth < 1) maxWidth = 1;
        if (line.Length <= maxWidth) return [line];

        var result = new List<string>();
        var remaining = line;
        while (remaining.Length > maxWidth)
        {
            var breakAt = remaining.LastIndexOf(' ', maxWidth - 1);
            if (breakAt <= 0) breakAt = maxWidth;
            result.Add(remaining[..breakAt]);
            remaining = remaining[breakAt..].TrimStart();
        }
        if (remaining.Length > 0)
            result.Add(remaining);
        return result;
    }

    internal static (int StartIndex, int EndIndex) ComputeViewport(
        int cursorIndex, int[] lineHeights, int availableLines)
    {
        availableLines = Math.Max(1, availableLines);
        if (lineHeights.Length == 0) return (0, 0);
        cursorIndex = Math.Clamp(cursorIndex, 0, lineHeights.Length - 1);

        // Start with the cursor task, then expand upward, then fill downward
        var startIndex = cursorIndex;
        var budget = lineHeights[cursorIndex];

        // Expand upward
        while (startIndex > 0 && budget + lineHeights[startIndex - 1] <= availableLines)
            budget += lineHeights[--startIndex];

        // Expand downward
        var endIndex = cursorIndex + 1;
        while (endIndex < lineHeights.Length && budget + lineHeights[endIndex] <= availableLines)
            budget += lineHeights[endIndex++];

        return (startIndex, endIndex);
    }

    private static string FormatPriority(Priority? priority) => priority switch
    {
        Priority.High => "[red bold]>>>[/]",
        Priority.Medium => "[yellow]>> [/]",
        Priority.Low => "[blue]>  [/]",
        _ => "[dim]·  [/]"
    };

    private static string FormatDueDate(DateOnly? dueDate)
    {
        if (!dueDate.HasValue) return "";
        var today = DateOnly.FromDateTime(DateTime.Today);
        var diff = dueDate.Value.DayNumber - today.DayNumber;

        return diff switch
        {
            < 0 => $"  [red]OVERDUE ({-diff}d)[/]",
            0 => "  [yellow]Due: Today[/]",
            1 => "  [dim]Due: Tomorrow[/]",
            < 7 => $"  [dim]Due: {dueDate.Value:dddd}[/]",
            _ => $"  [dim]Due: {dueDate.Value:MMM d}[/]"
        };
    }

    private static string FormatTags(string[]? tags)
    {
        if (tags is not { Length: > 0 }) return "";
        var formatted = tags.Select(t =>
            $"{TagColors.GetSpectreMarkup(t)}#{Markup.Escape(t)}[/]");
        return "  " + string.Join(" ", formatted);
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

        if (state.Mode == TuiMode.InputDueDate)
        {
            RenderDueDateInputField(state);
            return;
        }

        if (state.Mode == TuiMode.SelectMoveTarget || state.Mode == TuiMode.SelectList)
        {
            RenderSelectField(state);
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
            TuiMode.Normal => "[dim]↑↓[/]:nav [dim]space[/]:cycle [dim]x[/]:del [dim]1/2/3[/]:priority [dim]d[/]:due [dim]z[/]:undo [dim]a[/]:add [dim]s[/]:subtask [dim]q[/]:quit",
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

                _buffer.Write(ClearToEndOfLine);
                _ansi.Markup($"{prefix}{Markup.Escape(beforeCursor)}[bold underline]{Markup.Escape(atCursor)}[/]{Markup.Escape(afterCursor)}");
                _buffer.WriteLine();
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

    private void RenderDueDateInputField(TuiState state)
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

        var cursorInLine = Math.Min(cursorPos, buffer.Length);
        var beforeCursor = buffer[..cursorInLine];
        var atCursor = cursorInLine < buffer.Length ? buffer[cursorInLine].ToString() : " ";
        var afterCursor = cursorInLine < buffer.Length - 1 ? buffer[(cursorInLine + 1)..] : "";

        _buffer.Write(ClearToEndOfLine);
        _ansi.Markup($"[bold]>[/] {Markup.Escape(beforeCursor)}[bold underline]{Markup.Escape(atCursor)}[/]{Markup.Escape(afterCursor)}");
        _buffer.WriteLine();

        WriteLineCleared("[dim]enter[/]:save [dim]esc[/]:cancel");
    }

    private void RenderSelectField(TuiState state)
    {
        var title = state.Mode == TuiMode.SelectMoveTarget ? "Move to list:" : "Switch to list:";
        WriteLineCleared($"[bold]{title}[/]");

        // Show up to 5 options at a time, centered around cursor
        var options = state.SelectOptions;
        var cursor = state.SelectCursor;
        var maxVisible = 5;

        var startIndex = Math.Max(0, cursor - maxVisible / 2);
        startIndex = Math.Min(startIndex, Math.Max(0, options.Length - maxVisible));
        var endIndex = Math.Min(options.Length, startIndex + maxVisible);

        for (var i = startIndex; i < endIndex; i++)
        {
            var option = options[i];
            var isSelected = i == cursor;
            var isCurrent = option == state.SelectCurrentValue;

            var prefix = isSelected ? "[bold blue]>[/]" : " ";
            var suffix = isCurrent ? " [dim](current)[/]" : "";
            var text = isSelected
                ? $"[bold]{Markup.Escape(option)}[/]"
                : Markup.Escape(option);

            WriteLineCleared($"{prefix} {text}{suffix}");
        }

        // Clear remaining lines if less than maxVisible
        for (var i = endIndex - startIndex; i < maxVisible; i++)
            ClearLine();

        WriteLineCleared("[dim]↑↓[/]:select [dim]enter[/]:confirm [dim]esc[/]:cancel");
    }

    /// <summary>
    /// Write markup and clear to end of line, then move to next line
    /// </summary>
    private void WriteLineCleared(string markup)
    {
        _ansi.Markup(markup);
        _buffer.Write(ClearToEndOfLine);
        _buffer.WriteLine();
    }

    /// <summary>
    /// Clear entire line and move to next line
    /// </summary>
    private void ClearLine()
    {
        _buffer.Write(ClearToEndOfLine);
        _buffer.WriteLine();
    }

    public void Clear()
    {
        Console.Clear();
    }
}
