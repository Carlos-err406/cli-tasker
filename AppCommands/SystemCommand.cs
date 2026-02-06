namespace cli_tasker;

using System.CommandLine;
using TaskerCore.Data;

static class SystemCommand
{
    public static Command CreateSystemCommand()
    {
        var systemCommand = new Command("system", "System information and diagnostics");

        systemCommand.Add(CreateStatusCommand());

        return systemCommand;
    }

    private static Command CreateStatusCommand()
    {
        var statusCommand = new Command("status", "Show status of all tasks and trash across all lists");

        statusCommand.SetAction(CommandHelper.WithErrorHandling(_ =>
        {
            var listNames = ListManager.GetAllListNames();

            if (listNames.Length == 0)
            {
                Output.Info("No lists found");
                return;
            }

            var totalTasks = 0;
            var totalPending = 0;
            var totalInProgress = 0;
            var totalDone = 0;
            var totalTrash = 0;

            // Collect stats and display each list
            Output.Markup("[bold underline]Tasks[/]");
            Output.Info("");

            foreach (var name in listNames)
            {
                var taskList = new TodoTaskList(name);
                var stats = taskList.GetStats();

                totalTasks += stats.Total;
                totalPending += stats.Pending;
                totalInProgress += stats.InProgress;
                totalDone += stats.Done;
                totalTrash += stats.Trash;

                var doneLabel = stats.Done > 0 ? $"[green]{stats.Done} done[/]" : "[dim]0 done[/]";
                var wipLabel = stats.InProgress > 0 ? $"[yellow]{stats.InProgress} in-progress[/]" : "[dim]0 in-progress[/]";
                var pendingLabel = stats.Pending > 0 ? $"[grey]{stats.Pending} pending[/]" : "[dim]0 pending[/]";
                var trashLabel = stats.Trash > 0 ? $"[red]{stats.Trash} in trash[/]" : "[dim]0 in trash[/]";

                Output.Markup($"  [bold]{name}[/]: {wipLabel}, {pendingLabel}, {doneLabel}, {trashLabel}");
            }

            // Summary
            Output.Info("");
            Output.Markup("[bold underline]Summary[/]");
            Output.Info("");
            Output.Markup($"  Lists: [bold]{listNames.Length}[/]");
            Output.Markup($"  Total tasks: [bold]{totalTasks}[/] ([yellow]{totalInProgress} in-progress[/], [grey]{totalPending} pending[/], [green]{totalDone} done[/])");
            Output.Markup($"  Total in trash: [red]{totalTrash}[/]");
        }));

        return statusCommand;
    }
}
