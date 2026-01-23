namespace cli_tasker;

using System.CommandLine;

class Program
{
    static int Main(string[] args)
    {
        var rootCommand = new RootCommand("CLI task manager");
        var todoTaskList = new TodoTaskList();

        // initialize subcommands
        rootCommand.Subcommands.Add(AddCommand.CreateAddCommand(todoTaskList));
        rootCommand.Subcommands.Add(ListCommand.CreateListCommand(todoTaskList));
        rootCommand.Subcommands.Add(CompleteCommand.CreateCompleteCommand(todoTaskList));
        rootCommand.Subcommands.Add(DeleteCommand.CreateDeleteCommand(todoTaskList));

        return rootCommand.Parse(args).Invoke();
    }
}