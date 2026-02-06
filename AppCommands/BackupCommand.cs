namespace cli_tasker;

using System.CommandLine;
using TaskerCore.Backup;
using TaskerCore.Exceptions;

static class BackupCommand
{
    public static Command CreateBackupCommand()
    {
        var backupCommand = new Command("backup", "Manage task backups");

        backupCommand.Add(CreateListCommand());
        backupCommand.Add(CreateRestoreCommand());

        return backupCommand;
    }

    private static Command CreateListCommand()
    {
        var listCommand = new Command("list", "List available backups");

        listCommand.SetAction(CommandHelper.WithErrorHandling(_ =>
        {
            var backups = BackupManager.ListBackups();
            if (backups.Count == 0)
            {
                Output.Info("No backups available.");
                return;
            }

            Output.Markup("[bold]Available backups:[/]\n");
            for (var i = 0; i < backups.Count; i++)
            {
                var b = backups[i];
                var age = GetRelativeTime(b.Timestamp);
                var type = b.IsDaily ? " [dim](daily)[/]" : "";
                Output.Markup($"  {i + 1,2}. {age,-14} ({b.Timestamp:yyyy-MM-dd HH:mm:ss}){type}\n");
            }
        }));

        return listCommand;
    }

    private static Command CreateRestoreCommand()
    {
        var restoreCommand = new Command("restore", "Restore from a backup");
        var indexArg = new Argument<int>("index")
        {
            Description = "Backup number from 'backup list' (1 = most recent)",
            Arity = ArgumentArity.ZeroOrOne
        };
        var forceOption = new Option<bool>("--force")
        {
            Description = "Skip confirmation prompt"
        };

        restoreCommand.Add(indexArg);
        restoreCommand.Add(forceOption);

        restoreCommand.SetAction(CommandHelper.WithErrorHandling(parseResult =>
        {
            var index = parseResult.GetValue(indexArg);
            if (index == 0) index = 1; // Default to most recent
            var force = parseResult.GetValue(forceOption);

            var backups = BackupManager.ListBackups();
            if (backups.Count == 0)
                throw new BackupNotFoundException("No backups available. Backups are created automatically when you modify tasks.");

            if (index < 1 || index > backups.Count)
                throw new BackupNotFoundException($"Backup #{index} not found. Use 'tasker backup list' to see available backups (1-{backups.Count}).");

            var backup = backups[index - 1];

            if (!force)
            {
                Output.Warning($"This will restore from backup dated {backup.Timestamp:yyyy-MM-dd HH:mm:ss}");
                Output.Info("Current tasks will be backed up before restore.");
                Console.Write("Continue? [y/N] ");
                var response = Console.ReadLine();
                if (response?.ToLower() != "y")
                {
                    Output.Info("Restore cancelled.");
                    return;
                }
            }

            BackupManager.RestoreBackup(backup.Timestamp);
            Output.Success($"Restored from backup dated {backup.Timestamp:yyyy-MM-dd HH:mm:ss}");
        }));

        return restoreCommand;
    }

    private static string GetRelativeTime(DateTime timestamp)
    {
        var diff = DateTime.Now - timestamp;

        if (diff.TotalMinutes < 1)
            return "just now";
        if (diff.TotalMinutes < 60)
            return $"{(int)diff.TotalMinutes}m ago";
        if (diff.TotalHours < 24)
            return $"{(int)diff.TotalHours}h ago";
        if (diff.TotalDays < 7)
            return $"{(int)diff.TotalDays}d ago";

        return timestamp.ToString("MMM dd");
    }
}
