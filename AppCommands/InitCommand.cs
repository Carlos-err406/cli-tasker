namespace cli_tasker;

using System.CommandLine;
using TaskerCore.Data;
using TaskerCore.Exceptions;

static class InitCommand
{
    public static Command CreateInitCommand()
    {
        var initCommand = new Command("init", "Initialize tasker for the current directory (creates a list named after the directory)");

        initCommand.SetAction(CommandHelper.WithErrorHandling(_ =>
        {
            var dirName = Path.GetFileName(Directory.GetCurrentDirectory());
            if (string.IsNullOrEmpty(dirName))
            {
                Output.Error("Cannot determine directory name");
                return;
            }

            if (!ListManager.IsValidListName(dirName))
            {
                Output.Error($"Directory name '{dirName}' is not a valid list name (only letters, numbers, hyphens, underscores)");
                return;
            }

            if (ListManager.ListExists(dirName))
            {
                Output.Info($"List '{dirName}' already exists. Auto-detection is active in this directory.");
                return;
            }

            ListManager.CreateList(dirName);
            Output.Success($"Created list '{dirName}'. Commands in this directory will auto-filter to it.");
        }));

        return initCommand;
    }
}
