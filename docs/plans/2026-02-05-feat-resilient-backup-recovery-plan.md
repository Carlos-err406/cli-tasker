---
title: feat: Resilient Backup and Recovery System
type: feat
date: 2026-02-05
---

# Resilient Backup and Recovery System

## Overview

Implement an automatic backup and recovery system that protects against data loss from accidental overwrites (tests, bugs, user error) and file corruption (crashes mid-write, disk errors). Backups are created silently on every save, with CLI commands for listing and restoring.

## Problem Statement / Motivation

On 2026-02-05, tests accidentally wiped production task data due to a test isolation bug. While that bug was fixed, the incident exposed critical gaps:

1. **No atomic writes** - Direct `File.WriteAllText()` can corrupt data on mid-write crashes
2. **No backups** - Backup infrastructure exists (`StoragePaths.BackupDirectory`) but is unused
3. **No recovery path** - Data was recovered from conversation transcripts and old format files

## Proposed Solution

**Atomic Writes + Rolling Backups** (chosen from brainstorm alternatives)

1. **Atomic writes**: Every save writes to temp file, then renames (atomic on most filesystems)
2. **Rolling backups**: Keep last 10 versions + 7 daily backups
3. **CLI commands**: `tasker backup list` and `tasker backup restore`

## Technical Approach

### Architecture

```
TodoTaskList.Save()
    │
    ├─► BackupManager.CreateBackupIfNeeded()
    │       ├─► Copy current files to backups/
    │       └─► RotateBackups() (cleanup old)
    │
    └─► Atomic write (temp → rename)
```

### Files to Create

| File | Purpose |
|------|---------|
| `src/TaskerCore/Backup/BackupManager.cs` | Core backup/restore logic |
| `src/TaskerCore/Backup/BackupConfig.cs` | Configuration constants |
| `src/TaskerCore/Backup/BackupInfo.cs` | Backup metadata record |
| `AppCommands/BackupCommand.cs` | CLI commands |
| `tests/TaskerCore.Tests/Backup/BackupManagerTests.cs` | Unit tests |

### Files to Modify

| File | Changes |
|------|---------|
| `src/TaskerCore/Data/TodoTaskList.cs` | Add atomic write + backup call in `Save()` |
| `src/TaskerCore/StoragePaths.cs` | Add `GetBackupPath(timestamp)` helper |
| `src/TaskerCore/Exceptions/TaskerException.cs` | Add `BackupNotFoundException` |
| `Program.cs` | Register backup command |

### Implementation Phases

#### Phase 1: Core Backup Infrastructure

**BackupConfig.cs**
```csharp
namespace TaskerCore.Backup;

public static class BackupConfig
{
    public const int MaxVersionBackups = 10;
    public const int MaxDailyBackupDays = 7;
    public const string BackupExtension = ".backup.json";
    public const string DailyBackupPrefix = "daily.";
}
```

**BackupInfo.cs**
```csharp
namespace TaskerCore.Backup;

public record BackupInfo(
    string FilePath,
    DateTime Timestamp,
    bool IsDaily,
    long FileSize
);
```

**BackupManager.cs** (core methods)
```csharp
namespace TaskerCore.Backup;

public static class BackupManager
{
    private static readonly object BackupLock = new();

    /// <summary>
    /// Creates a backup of current task files. Called before each save.
    /// </summary>
    public static void CreateBackup()
    {
        lock (BackupLock)
        {
            StoragePaths.Current.EnsureBackupDirectory();
            var timestamp = DateTime.Now;

            // Copy tasks file
            var tasksBackupPath = GetVersionBackupPath(timestamp, "all-tasks");
            if (File.Exists(StoragePaths.Current.AllTasksPath))
                File.Copy(StoragePaths.Current.AllTasksPath, tasksBackupPath, overwrite: true);

            // Copy trash file
            var trashBackupPath = GetVersionBackupPath(timestamp, "all-tasks.trash");
            if (File.Exists(StoragePaths.Current.AllTrashPath))
                File.Copy(StoragePaths.Current.AllTrashPath, trashBackupPath, overwrite: true);

            // Create/update daily backup
            CreateDailyBackupIfNeeded(timestamp);

            // Cleanup old backups
            RotateBackups();
        }
    }

    /// <summary>
    /// Lists available backups, newest first.
    /// </summary>
    public static IReadOnlyList<BackupInfo> ListBackups() { ... }

    /// <summary>
    /// Restores from a specific backup timestamp.
    /// Creates a pre-restore safety backup first.
    /// </summary>
    public static void RestoreBackup(DateTime timestamp) { ... }

    private static void RotateBackups() { ... }
    private static void CreateDailyBackupIfNeeded(DateTime timestamp) { ... }
    private static string GetVersionBackupPath(DateTime timestamp, string baseName) { ... }
}
```

#### Phase 2: Atomic Writes

**TodoTaskList.Save() modification**
```csharp
private void Save()
{
    StoragePaths.Current.EnsureDirectory();

    // Create backup BEFORE writing (captures current state)
    try { BackupManager.CreateBackup(); }
    catch { /* Backup failure should not block save */ }

    lock (SaveLock)
    {
        // Atomic write for tasks
        var tasksTempPath = StoragePaths.Current.AllTasksPath + ".tmp";
        var tasksJson = JsonSerializer.Serialize(TaskLists);
        File.WriteAllText(tasksTempPath, tasksJson);
        File.Move(tasksTempPath, StoragePaths.Current.AllTasksPath, overwrite: true);

        // Atomic write for trash
        var trashTempPath = StoragePaths.Current.AllTrashPath + ".tmp";
        var trashJson = JsonSerializer.Serialize(TrashLists);
        File.WriteAllText(trashTempPath, trashJson);
        File.Move(trashTempPath, StoragePaths.Current.AllTrashPath, overwrite: true);
    }
}
```

#### Phase 3: CLI Commands

**BackupCommand.cs**
```csharp
public static class BackupCommand
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
            for (int i = 0; i < backups.Count; i++)
            {
                var b = backups[i];
                var age = GetRelativeTime(b.Timestamp);
                var type = b.IsDaily ? "[dim](daily)[/]" : "";
                Output.Markup($"  {i + 1}. {age,-12} ({b.Timestamp:yyyy-MM-dd HH:mm:ss}) {type}\n");
            }
        }));
        return listCommand;
    }

    private static Command CreateRestoreCommand()
    {
        var restoreCommand = new Command("restore", "Restore from a backup");
        var indexArg = new Argument<int?>("index", () => null,
            "Backup number from 'backup list' (1 = most recent)");
        var forceOption = new Option<bool>("--force", "Skip confirmation prompt");

        restoreCommand.Add(indexArg);
        restoreCommand.Add(forceOption);

        restoreCommand.SetAction(CommandHelper.WithErrorHandling(parseResult =>
        {
            var index = parseResult.GetValue(indexArg) ?? 1;
            var force = parseResult.GetValue(forceOption);

            var backups = BackupManager.ListBackups();
            if (index < 1 || index > backups.Count)
                throw new BackupNotFoundException($"Backup #{index} not found. Use 'tasker backup list' to see available backups.");

            var backup = backups[index - 1];

            if (!force)
            {
                Output.Warning($"This will restore from backup dated {backup.Timestamp:yyyy-MM-dd HH:mm:ss}");
                Output.Warning("Current tasks will be backed up before restore.");
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
}
```

### Backup File Naming

```
backups/
├── all-tasks.2026-02-05T14-30-45.backup.json      # Version backup (tasks)
├── all-tasks.trash.2026-02-05T14-30-45.backup.json # Version backup (trash)
├── all-tasks.daily.2026-02-05.backup.json          # Daily backup (tasks)
├── all-tasks.trash.daily.2026-02-05.backup.json    # Daily backup (trash)
└── all-tasks.pre-restore.2026-02-05T15-00-00.json  # Pre-restore safety backup
```

**Naming format:** `{baseName}.{timestamp}.backup.json` where timestamp is `yyyy-MM-ddTHH-mm-ss` (filesystem-safe).

## Acceptance Criteria

### Functional Requirements

- [x] Every `Save()` creates atomic write (temp file → rename)
- [x] Every `Save()` creates a version backup before writing
- [x] Daily backup created/updated once per calendar day
- [x] `tasker backup list` shows available backups (newest first)
- [x] `tasker backup restore [N]` restores from backup #N (default: most recent)
- [x] Restore prompts for confirmation unless `--force` is used
- [x] Pre-restore safety backup is created before restoring
- [x] Backup rotation keeps max 10 versions + 7 daily backups
- [x] Both tasks and trash files are backed up together

### Non-Functional Requirements

- [x] Backup failure does NOT block the user's save operation
- [x] Backup operations use locking to prevent race conditions
- [x] Works correctly when TaskerTray and CLI run concurrently
- [x] Undo history is cleared after restore (checksums won't match)

### Quality Gates

- [x] Unit tests for BackupManager (create, list, restore, rotate)
- [x] Integration tests for atomic write behavior
- [x] Tests use isolated storage fixture (no production data affected)

## Success Metrics

- No data loss possible from mid-write crashes (atomic writes)
- Can recover from any of last 10 saves
- Can recover from any of last 7 days
- Zero user effort for protection (automatic)
- Simple restore via CLI command

## Dependencies & Risks

| Risk | Mitigation |
|------|------------|
| Disk full during backup | Backup failure is non-blocking; save proceeds |
| Corrupt backup file | Backups are just file copies; use try/catch on restore |
| Concurrent CLI + Tray access | Use existing `SaveLock` pattern; backup has own lock |
| User restores wrong backup | Confirmation prompt + pre-restore safety backup |

## Open Questions (Resolved)

| Question | Decision |
|----------|----------|
| Backup trash file? | Yes - complete restore needs both |
| Backup config file? | No - trivial setting, rarely changes |
| Undo history after restore? | Clear it - checksums won't match anyway |
| Restore confirmation? | Yes, with `--force` to skip |
| If backup fails? | Save proceeds anyway - user operation > backup |

## References

### Internal References
- Brainstorm: `docs/brainstorms/2026-02-05-resilient-backup-recovery-brainstorm.md`
- Current save pattern: `src/TaskerCore/Data/TodoTaskList.cs:933-944`
- Backup directory: `src/TaskerCore/StoragePaths.cs:54-82`
- Command pattern: `AppCommands/TrashCommand.cs`
- Config pattern: `src/TaskerCore/Undo/UndoConfig.cs`

### Similar Patterns
- Undo system uses checksum validation: `src/TaskerCore/Undo/UndoManager.cs:194-198`
- Thread safety with SaveLock: `src/TaskerCore/Data/TodoTaskList.cs:12`
