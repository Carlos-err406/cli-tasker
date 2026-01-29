namespace cli_tasker;

using System.CommandLine;

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

        listCommand.Options.Add(checkedOption);
        listCommand.Options.Add(uncheckedOption);

        listCommand.SetAction(CommandHelper.WithErrorHandling(parseResult =>
        {
            var listName = parseResult.GetValue(listOption);
            var todoTaskList = ListManager.GetTaskList(listName);

            var showChecked = parseResult.GetValue(checkedOption);
            var showUnchecked = parseResult.GetValue(uncheckedOption);

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

            todoTaskList.ListTodoTasks(filterChecked);
        }));
        return listCommand;
    }
}