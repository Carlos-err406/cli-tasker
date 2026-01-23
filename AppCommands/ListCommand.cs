namespace cli_tasker;

using System.CommandLine;

class ListCommand
{
    public static Command CreateListCommand(TodoTaskList todoTaskList)
    {

        var listCommand = new Command("list", "List all tasks");
        listCommand.SetAction((_) => todoTaskList.Print());
        return listCommand;
    }
}