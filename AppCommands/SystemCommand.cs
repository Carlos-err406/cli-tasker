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
            var totalChecked = 0;
            var totalUnchecked = 0;
            var totalTrash = 0;

            // Collect stats and display each list
            Output.Markup("[bold underline]Tasks[/]");
            Output.Info("");

            foreach (var name in listNames)
            {
                var taskList = new TodoTaskList(name);
                var stats = taskList.GetStats();

                totalTasks += stats.Total;
                totalChecked += stats.Checked;
                totalUnchecked += stats.Unchecked;
                totalTrash += stats.Trash;

                var checkedLabel = stats.Checked > 0 ? $"[green]{stats.Checked} checked[/]" : "[dim]0 checked[/]";
                var uncheckedLabel = stats.Unchecked > 0 ? $"[yellow]{stats.Unchecked} unchecked[/]" : "[dim]0 unchecked[/]";
                var trashLabel = stats.Trash > 0 ? $"[red]{stats.Trash} in trash[/]" : "[dim]0 in trash[/]";

                Output.Markup($"  [bold]{name}[/]: {checkedLabel}, {uncheckedLabel}, {trashLabel}");
            }

            // Summary
            Output.Info("");
            Output.Markup("[bold underline]Summary[/]");
            Output.Info("");
            Output.Markup($"  Lists: [bold]{listNames.Length}[/]");
            Output.Markup($"  Total tasks: [bold]{totalTasks}[/] ([green]{totalChecked} checked[/], [yellow]{totalUnchecked} unchecked[/])");
            Output.Markup($"  Total in trash: [red]{totalTrash}[/]");
        }));

        return statusCommand;
    }
}
