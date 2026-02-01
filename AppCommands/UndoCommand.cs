namespace cli_tasker;

using System.CommandLine;
using cli_tasker.Undo;

static class UndoCommand
{
    public static (Command undo, Command redo, Command history) CreateUndoCommands()
    {
        var undoCmd = new Command("undo", "Undo the last action");
        var redoCmd = new Command("redo", "Redo the last undone action");
        var historyCmd = new Command("history", "Show undo/redo history");

        undoCmd.SetAction(CommandHelper.WithErrorHandling(_ =>
        {
            var desc = UndoManager.Instance.Undo();
            if (desc != null)
                Output.Success($"Undone: {desc}");
            else
                Output.Info("Nothing to undo");
        }));

        redoCmd.SetAction(CommandHelper.WithErrorHandling(_ =>
        {
            var desc = UndoManager.Instance.Redo();
            if (desc != null)
                Output.Success($"Redone: {desc}");
            else
                Output.Info("Nothing to redo");
        }));

        var clearOption = new Option<bool>("--clear", "-c")
        {
            Description = "Clear all undo/redo history"
        };
        historyCmd.Options.Add(clearOption);

        historyCmd.SetAction(CommandHelper.WithErrorHandling(parseResult =>
        {
            var clear = parseResult.GetValue(clearOption);
            if (clear)
            {
                UndoManager.Instance.ClearHistory();
                Output.Success("Undo/redo history cleared");
                return;
            }

            var undo = UndoManager.Instance.UndoHistory;
            var redo = UndoManager.Instance.RedoHistory;

            if (undo.Count == 0 && redo.Count == 0)
            {
                Output.Info("No history");
                return;
            }

            if (undo.Count > 0)
            {
                Output.Markup($"[bold]Undo stack[/] [dim]({undo.Count} actions, {UndoConfig.MaxUndoStackSize} max)[/]");
                foreach (var cmd in undo.Take(10))
                {
                    var timeAgo = GetTimeAgo(cmd.ExecutedAt);
                    Output.Markup($"  [dim]{timeAgo}[/] {cmd.Description}");
                }
                if (undo.Count > 10)
                {
                    Output.Markup($"  [dim]... and {undo.Count - 10} more[/]");
                }
            }

            if (redo.Count > 0)
            {
                Output.Markup($"[bold]Redo stack[/] [dim]({redo.Count} actions)[/]");
                foreach (var cmd in redo.Take(10))
                {
                    var timeAgo = GetTimeAgo(cmd.ExecutedAt);
                    Output.Markup($"  [dim]{timeAgo}[/] {cmd.Description}");
                }
                if (redo.Count > 10)
                {
                    Output.Markup($"  [dim]... and {redo.Count - 10} more[/]");
                }
            }
        }));

        return (undoCmd, redoCmd, historyCmd);
    }

    private static string GetTimeAgo(DateTime time)
    {
        var diff = DateTime.Now - time;
        if (diff.TotalMinutes < 1)
            return "just now";
        if (diff.TotalMinutes < 60)
            return $"{(int)diff.TotalMinutes}m ago";
        if (diff.TotalHours < 24)
            return $"{(int)diff.TotalHours}h ago";
        return $"{(int)diff.TotalDays}d ago";
    }
}
