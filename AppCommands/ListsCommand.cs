namespace cli_tasker;

using System.CommandLine;
using Spectre.Console;
using TaskerCore.Config;
using TaskerCore.Data;
using TaskerCore.Exceptions;

static class ListsCommand
{
    public static Command CreateListsCommand()
    {
        var listsCommand = new Command("lists", "Manage task lists");

        // Default action: show all lists
        listsCommand.SetAction(_ =>
        {
            var lists = ListManager.GetAllListNames();
            var defaultList = AppConfig.GetDefaultList();

            if (lists.Length == 0)
            {
                Output.Info("No lists found. Add a task with: tasker add \"task\" -l <list-name>");
                return;
            }

            Output.Info("Available lists:");
            foreach (var list in lists)
            {
                if (list == defaultList)
                {
                    Output.Markup($"  [bold]{Markup.Escape(list)} (default)[/]");
                }
                else
                {
                    Output.Info($"  {list}");
                }
            }
        });

        // Subcommands
        listsCommand.Add(CreateDeleteCommand());
        listsCommand.Add(CreateRenameCommand());
        listsCommand.Add(CreateSetDefaultCommand());

        return listsCommand;
    }

    private static Command CreateDeleteCommand()
    {
        var deleteCommand = new Command("delete", "Delete a list and all its tasks");
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

            Output.Result(ListManager.DeleteList(name));
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

            Output.Result(ListManager.RenameList(oldName, newName));
        }));

        return renameCommand;
    }

    private static Command CreateSetDefaultCommand()
    {
        var setDefaultCommand = new Command("set-default", "Set the default list for new tasks");
        var nameArg = new Argument<string>("name")
        {
            Description = "The name of the list to set as default"
        };
        setDefaultCommand.Arguments.Add(nameArg);

        setDefaultCommand.SetAction(CommandHelper.WithErrorHandling(parseResult =>
        {
            var name = parseResult.GetValue(nameArg);
            if (name == null)
            {
                Output.Error("List name is required");
                return;
            }

            if (!ListManager.IsValidListName(name))
            {
                throw new InvalidListNameException(name);
            }

            AppConfig.SetDefaultList(name);
            Output.Success($"Default list set to '{name}'");
        }));

        return setDefaultCommand;
    }
}
