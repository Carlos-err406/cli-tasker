namespace cli_tasker;

using System.CommandLine;
using cli_tasker.Tui;

class Program
{
    static int Main(string[] args)
    {
        // Launch TUI if no arguments and terminal is interactive
        if (args.Length == 0 && !Console.IsInputRedirected)
        {
            var tui = new TuiApp();
            tui.Run();
            return 0;
        }

        var rootCommand = new RootCommand("CLI task manager");

        // Global option for selecting which list to use
        var listOption = new Option<string?>("--list", "-l")
        {
            Description = "The list to use (default: tasks)"
        };
        rootCommand.Options.Add(listOption);

        // Global option to bypass directory auto-detection
        var allOption = new Option<bool>("--all", "-a")
        {
            Description = "Show all lists (bypass directory auto-detection)"
        };
        rootCommand.Options.Add(allOption);

        // Initialize subcommands
        rootCommand.Add(AddCommand.CreateAddCommand(listOption, allOption));
        rootCommand.Add(ListCommand.CreateListCommand(listOption, allOption));
        var (deleteCommand, clearCommand) = DeleteCommand.CreateDeleteCommands(listOption, allOption);
        rootCommand.Add(deleteCommand);
        rootCommand.Add(clearCommand);
        var (checkCommand, uncheckCommand) = CheckCommand.CreateCheckCommands(listOption);
        rootCommand.Add(checkCommand);
        rootCommand.Add(uncheckCommand);
        rootCommand.Add(RenameCommand.CreateRenameCommand(listOption));
        rootCommand.Add(GetCommand.CreateGetCommand());
        rootCommand.Add(MoveCommand.CreateMoveCommand());
        rootCommand.Add(DueCommand.CreateDueCommand());
        rootCommand.Add(PriorityCommand.CreatePriorityCommand());
        rootCommand.Add(ListsCommand.CreateListsCommand());
        rootCommand.Add(TrashCommand.CreateTrashCommand(listOption, allOption));
        rootCommand.Add(SystemCommand.CreateSystemCommand());
        rootCommand.Add(InitCommand.CreateInitCommand());
        rootCommand.Add(BackupCommand.CreateBackupCommand());
        var (undoCmd, redoCmd, historyCmd) = UndoCommand.CreateUndoCommands();
        rootCommand.Add(undoCmd);
        rootCommand.Add(redoCmd);
        rootCommand.Add(historyCmd);

        return rootCommand.Parse(args).Invoke();
    }
}