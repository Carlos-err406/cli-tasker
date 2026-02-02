namespace cli_tasker;

using Spectre.Console;
using TaskerCore.Results;

static class Output
{
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
