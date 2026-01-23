namespace cli_tasker;

using System.CommandLine;

static class ListCommand
{
    public static Command CreateListCommand(TodoTaskList todoTaskList)
    {

        var listCommand = new Command("list", "List all tasks");
        listCommand.SetAction((_) => todoTaskList.ListTodoTasks());
        return listCommand;
    }
}