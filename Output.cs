namespace cli_tasker;

using Spectre.Console;
using TaskerCore.Models;
using TaskerCore.Results;

static class Output
{
    public static string FormatPriority(Priority? priority) => priority switch
    {
        Priority.High => "[red bold]>>>[/]",
        Priority.Medium => "[yellow]>> [/]",
        Priority.Low => "[blue]>  [/]",
        _ => "[dim]·  [/]"
    };

    public static string FormatDueDate(DateOnly? dueDate)
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

    public static string FormatTags(string[]? tags)
    {
        if (tags is not { Length: > 0 }) return "";
        var tagStr = string.Join(" ", tags.Select(t => $"#{t}"));
        return $"  [cyan]{Spectre.Console.Markup.Escape(tagStr)}[/]";
    }

    public static void Success(string message)
    {
        AnsiConsole.MarkupLine($"[green]{Spectre.Console.Markup.Escape(message)}[/]");
    }

    public static void Error(string message)
    {
        AnsiConsole.MarkupLine($"[red]{Spectre.Console.Markup.Escape(message)}[/]");
    }

    public static void Warning(string message)
    {
        AnsiConsole.MarkupLine($"[yellow]{Spectre.Console.Markup.Escape(message)}[/]");
    }

    public static void Info(string message)
    {
        AnsiConsole.MarkupLine(Spectre.Console.Markup.Escape(message));
    }

    public static void Markup(string markup)
    {
        AnsiConsole.MarkupLine(markup);
    }

    /// <summary>
    /// Outputs a TaskResult with appropriate formatting.
    /// </summary>
    public static void Result(TaskResult result)
    {
        switch (result)
        {
            case TaskResult.Success success:
                Success(success.Message);
                break;
            case TaskResult.NotFound notFound:
                Error($"Could not find task with id {notFound.TaskId}");
                break;
            case TaskResult.NoChange noChange:
                Info(noChange.Message);
                break;
            case TaskResult.Error error:
                Error(error.Message);
                break;
        }
    }

    /// <summary>
    /// Outputs multiple TaskResults from a batch operation.
    /// </summary>
    public static void BatchResults(BatchTaskResult batch)
    {
        foreach (var result in batch.Results)
        {
            Result(result);
        }
    }
}
