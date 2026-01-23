namespace cli_tasker;

using System.CommandLine;

class Program
{
    static int Main(string[] args)
    {
        var rootCommand = new RootCommand("CLI task manager");
        var todoTaskList = new TodoTaskList();

        // initialize subcommands
        rootCommand.Add(AddCommand.CreateAddCommand(todoTaskList));
        rootCommand.Add(ListCommand.CreateListCommand(todoTaskList));
        rootCommand.Add(DeleteCommand.CreateDeleteCommand(todoTaskList));
        var (checkCommand, uncheckCommand) = CheckCommand.CreateCheckCommands(todoTaskList);
        rootCommand.Add(checkCommand);
        rootCommand.Add(uncheckCommand);
        return rootCommand.Parse(args).Invoke();
    }
}