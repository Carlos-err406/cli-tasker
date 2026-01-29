namespace cli_tasker;

using Spectre.Console;

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
}
