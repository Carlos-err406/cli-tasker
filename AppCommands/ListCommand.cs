namespace cli_tasker;

using System.CommandLine;
using Spectre.Console;
using TaskerCore;
using TaskerCore.Config;
using TaskerCore.Data;
using TaskerCore.Models;
using TaskerCore.Parsing;
using TaskStatus = TaskerCore.Models.TaskStatus;

static class ListCommand
{
    public static Command CreateListCommand(Option<string?> listOption, Option<bool> allOption)
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
        var priorityOption = new Option<string?>("--priority", "-p")
        {
            Description = "Filter by priority (high, medium, low)"
        };
        priorityOption.AcceptOnlyFromAmong("high", "medium", "low", "1", "2", "3");
        var overdueOption = new Option<bool>("--overdue")
        {
            Description = "Show only overdue tasks"
        };

        listCommand.Options.Add(listOption);
        listCommand.Options.Add(checkedOption);
        listCommand.Options.Add(uncheckedOption);
        listCommand.Options.Add(priorityOption);
        listCommand.Options.Add(overdueOption);

        listCommand.SetAction(CommandHelper.WithErrorHandling(parseResult =>
        {
            var showChecked = parseResult.GetValue(checkedOption);
            var showUnchecked = parseResult.GetValue(uncheckedOption);
            var explicitList = parseResult.GetValue(listOption);
            var showAll = parseResult.GetValue(allOption);
            var listName = ListManager.ResolveListFilter(explicitList, showAll);
            var priorityStr = parseResult.GetValue(priorityOption);
            var showOverdue = parseResult.GetValue(overdueOption);

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

            Priority? filterPriority = priorityStr?.ToLower() switch
            {
                "high" or "1" => Priority.High,
                "medium" or "2" => Priority.Medium,
                "low" or "3" => Priority.Low,
                _ => null
            };

            bool? filterOverdue = showOverdue ? true : null;

            // Show indicator when auto-detection is active
            if (explicitList == null && !showAll && listName != null)
            {
                Output.Info($"[dim](auto: {listName})[/]");
            }

            // Unfiltered task list for relationship queries (global by ID)
            var relTaskList = new TodoTaskList();

            if (listName == null)
            {
                // Default: show all lists grouped
                var listNames = ListManager.GetAllListNames();
                foreach (var name in listNames)
                {
                    var taskList = new TodoTaskList(name);
                    Output.Markup($"[bold underline]{name}[/]");
                    DisplayTasks(taskList.GetSortedTasks(filterChecked: filterChecked, filterPriority: filterPriority, filterOverdue: filterOverdue), filterChecked, relTaskList);
                    Output.Info("");
                }
            }
            else
            {
                // Filter to specific list
                var todoTaskList = new TodoTaskList(listName);
                DisplayTasks(todoTaskList.GetSortedTasks(filterChecked: filterChecked, filterPriority: filterPriority, filterOverdue: filterOverdue), filterChecked, relTaskList);
            }
        }));
        return listCommand;
    }

    private static void DisplayTasks(List<TodoTask> tasks, bool? filterChecked, TodoTaskList taskList)
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
            var indent = new string(' ', AppConfig.TaskPrefixLength + 4); // +4 for priority indicator
            var displayDesc = TaskDescriptionParser.GetDisplayDescription(td.Description);
            var lines = displayDesc.Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
            var firstLine = $"[bold]{Markup.Escape(lines[0])}[/]";
            var restLines = lines.Length > 1
                ? string.Concat(lines.Skip(1).Select(l => $"\n{indent}[dim]{Markup.Escape(l)}[/]"))
                : "";

            var checkbox = td.Status switch
            {
                TaskStatus.Done => "[green][[x]][/]",
                TaskStatus.InProgress => "[yellow][[-]][/]",
                _ => "[grey][[ ]][/]"
            };
            var taskId = $"[dim]({td.Id})[/]";
            var priority = Output.FormatPriority(td.Priority);
            var dueDate = Output.FormatDueDate(td.DueDate);
            var tags = Output.FormatTags(td.Tags);
            Output.Markup($"{taskId} {priority} {checkbox} {firstLine}{dueDate}{tags}{restLines}");

            // Relationship indicators
            if (td.ParentId != null)
            {
                var parent = taskList.GetTodoTaskById(td.ParentId);
                var parentTitle = parent != null
                    ? Markup.Escape(StringHelpers.Truncate(TaskDescriptionParser.GetDisplayDescription(parent.Description).Split('\n')[0], 40))
                    : "?";
                Output.Markup($"{indent}[dim]↑ Subtask of ({td.ParentId}) {parentTitle}[/]");
            }

            var subtasks = taskList.GetSubtasks(td.Id);
            foreach (var sub in subtasks)
            {
                var subTitle = Markup.Escape(StringHelpers.Truncate(TaskDescriptionParser.GetDisplayDescription(sub.Description).Split('\n')[0], 40));
                Output.Markup($"{indent}[dim]↳ Subtask ({sub.Id}) {subTitle}[/]");
            }

            var blocks = taskList.GetBlocks(td.Id);
            foreach (var b in blocks)
            {
                var bTitle = Markup.Escape(StringHelpers.Truncate(TaskDescriptionParser.GetDisplayDescription(b.Description).Split('\n')[0], 40));
                Output.Markup($"{indent}[yellow dim]⊘ Blocks ({b.Id}) {bTitle}[/]");
            }

            var blockedBy = taskList.GetBlockedBy(td.Id);
            foreach (var bb in blockedBy)
            {
                var bbTitle = Markup.Escape(StringHelpers.Truncate(TaskDescriptionParser.GetDisplayDescription(bb.Description).Split('\n')[0], 40));
                Output.Markup($"{indent}[yellow dim]⊘ Blocked by ({bb.Id}) {bbTitle}[/]");
            }
        }
    }
}
