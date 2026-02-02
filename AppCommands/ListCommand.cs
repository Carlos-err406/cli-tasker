namespace cli_tasker;

using System.CommandLine;
using Spectre.Console;
using TaskerCore.Config;
using TaskerCore.Data;
using TaskerCore.Models;

static class ListCommand
{
    public static Command CreateListCommand(Option<string?> listOption)
    {
        var listCommand = new Command("list", "List all tasks");
        var checkedOption = new Option<bool>("--checked", "-c")
        {
            Description = "Show only checked tasks"
        };
        var uncheckedOption = new Option<bool>("--unchecked", "-u")
        {
            Description = "Show only unchecked tasks"
        };

        listCommand.Options.Add(listOption);
        listCommand.Options.Add(checkedOption);
        listCommand.Options.Add(uncheckedOption);

        listCommand.SetAction(CommandHelper.WithErrorHandling(parseResult =>
        {
            var showChecked = parseResult.GetValue(checkedOption);
            var showUnchecked = parseResult.GetValue(uncheckedOption);
            var listName = parseResult.GetValue(listOption);

            if (showChecked && showUnchecked)
            {
                Output.Error("Cannot use both --checked and --unchecked at the same time");
                return;
            }

            bool? filterChecked = (showChecked, showUnchecked) switch
            {
                (true, false) => true,
                (false, true) => false,
                _ => null
            };

            if (listName == null)
            {
                // Default: show all lists grouped
                var listNames = ListManager.GetAllListNames();
                foreach (var name in listNames)
                {
                    var taskList = new TodoTaskList(name);
                    Output.Markup($"[bold underline]{name}[/]");
                    DisplayTasks(taskList.GetSortedTasks(filterChecked), filterChecked);
                    Output.Info("");
                }
            }
            else
            {
                // Filter to specific list
                var todoTaskList = new TodoTaskList(listName);
                DisplayTasks(todoTaskList.GetSortedTasks(filterChecked), filterChecked);
            }
        }));
        return listCommand;
    }

    private static void DisplayTasks(List<TodoTask> tasks, bool? filterChecked)
    {
        if (tasks.Count == 0)
        {
            var message = filterChecked switch
            {
                true => "No checked tasks found",
                false => "No unchecked tasks found",
                null => "No tasks saved yet... use the add command to create one"
            };
            Output.Info(message);
            return;
        }

        foreach (var td in tasks)
        {
            var indent = new string(' ', AppConfig.TaskPrefixLength);
            var lines = td.Description.Split('\n');
            var firstLine = $"[bold]{Markup.Escape(lines[0])}[/]";
            var restLines = lines.Length > 1
                ? "\n" + indent + "[dim]" + string.Join("\n" + indent, lines.Skip(1).Select(Markup.Escape)) + "[/]"
                : "";

            var checkbox = td.IsChecked ? "[green][[x]][/]" : "[grey][[ ]][/]";
            var taskId = $"[dim]({td.Id})[/]";
            Output.Markup($"{taskId} {checkbox} - {firstLine}{restLines}");
        }
    }
}
