namespace cli_tasker;

using System.CommandLine;
using Spectre.Console;

static class ListsCommand
{
    public static Command CreateListsCommand()
    {
        var listsCommand = new Command("lists", "Manage task lists");

        // Default action: show all lists
        listsCommand.SetAction(_ =>
        {
            var lists = ListManager.GetAllListNames();
            var selected = AppConfig.GetSelectedList();

            if (lists.Length == 0)
            {
                Output.Info("No lists found. Use 'tasker lists create <name>' to create one.");
                return;
            }

            Output.Info("Available lists:");
            foreach (var list in lists)
            {
                if (list == selected)
                {
                    Output.Markup($"  [bold]{Markup.Escape(list)} (selected)[/]");
                }
                else
                {
                    Output.Info($"  {list}");
                }
            }
        });

        // Subcommands
        listsCommand.Add(CreateCreateCommand());
        listsCommand.Add(CreateDeleteCommand());
        listsCommand.Add(CreateRenameCommand());
        listsCommand.Add(CreateSelectCommand());

        return listsCommand;
    }

    private static Command CreateCreateCommand()
    {
        var createCommand = new Command("create", "Create a new list");
        var nameArg = new Argument<string>("name")
        {
            Description = "The name of the list to create"
        };
        createCommand.Arguments.Add(nameArg);

        createCommand.SetAction(CommandHelper.WithErrorHandling(parseResult =>
        {
            var name = parseResult.GetValue(nameArg);
            if (name == null)
            {
                Output.Error("List name is required");
                return;
            }

            ListManager.CreateList(name);
            Output.Success($"Created list '{name}'");
        }));

        return createCommand;
    }

    private static Command CreateDeleteCommand()
    {
        var deleteCommand = new Command("delete", "Delete a list");
        var nameArg = new Argument<string>("name")
        {
            Description = "The name of the list to delete"
        };
        deleteCommand.Arguments.Add(nameArg);

        deleteCommand.SetAction(CommandHelper.WithErrorHandling(parseResult =>
        {
            var name = parseResult.GetValue(nameArg);
            if (name == null)
            {
                Output.Error("List name is required");
                return;
            }

            ListManager.DeleteList(name);
            Output.Success($"Deleted list '{name}'");
        }));

        return deleteCommand;
    }

    private static Command CreateRenameCommand()
    {
        var renameCommand = new Command("rename", "Rename a list");
        var oldNameArg = new Argument<string>("oldName")
        {
            Description = "The current name of the list"
        };
        var newNameArg = new Argument<string>("newName")
        {
            Description = "The new name for the list"
        };
        renameCommand.Arguments.Add(oldNameArg);
        renameCommand.Arguments.Add(newNameArg);

        renameCommand.SetAction(CommandHelper.WithErrorHandling(parseResult =>
        {
            var oldName = parseResult.GetValue(oldNameArg);
            var newName = parseResult.GetValue(newNameArg);
            if (oldName == null || newName == null)
            {
                Output.Error("Both old and new list names are required");
                return;
            }

            ListManager.RenameList(oldName, newName);
            Output.Success($"Renamed list '{oldName}' to '{newName}'");
        }));

        return renameCommand;
    }

    private static Command CreateSelectCommand()
    {
        var selectCommand = new Command("select", "Select a list as the default");
        var nameArg = new Argument<string>("name")
        {
            Description = "The name of the list to select"
        };
        selectCommand.Arguments.Add(nameArg);

        selectCommand.SetAction(CommandHelper.WithErrorHandling(parseResult =>
        {
            var name = parseResult.GetValue(nameArg);
            if (name == null)
            {
                Output.Error("List name is required");
                return;
            }

            if (!ListManager.ListExists(name))
            {
                throw new ListNotFoundException(name);
            }

            AppConfig.SetSelectedList(name);
            Output.Success($"Selected list '{name}'");
        }));

        return selectCommand;
    }
}
