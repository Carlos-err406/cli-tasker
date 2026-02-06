---
title: "Atomic Writes and Rolling Backups"
date: 2026-02-05
category: data-safety
tags:
  - backup
  - recovery
  - atomic-writes
  - data-persistence
  - disaster-recovery
module: TaskerCore
severity: critical
status: resolved
symptoms:
  - data-loss-after-crash
  - no-recovery-mechanism
  - test-isolation-failure
  - production-data-wiped
root_cause: missing-backup-infrastructure-and-non-atomic-writes
---

# Atomic Writes and Rolling Backups

## Problem

On 2026-02-05, tests accidentally wiped production task data. Investigation revealed critical gaps:

1. **Non-atomic writes** - `File.WriteAllText()` could corrupt data on mid-write crash
2. **No backups** - Backup directory was defined in `StoragePaths.cs` but never used
3. **No recovery path** - Data recovered only from conversation transcripts and old format files

### Symptoms

- All tasks suddenly gone or replaced with test data
- `all-tasks.json` contains empty or test state
- No way to recover previous state

## Solution

Implemented a three-layer defense system:

### Layer 1: Atomic Writes

Write to temp file, then rename (atomic on most filesystems):

```csharp
// src/TaskerCore/Data/TodoTaskList.cs
private void Save()
{
    StoragePaths.Current.EnsureDirectory();

    // Create backup BEFORE writing
    try { BackupManager.CreateBackup(); }
    catch { /* Backup failure should not block save */ }

    lock (SaveLock)
    {
        // Atomic write: temp file → rename
        var tasksTempPath = StoragePaths.Current.AllTasksPath + ".tmp";
        var tasksJson = JsonSerializer.Serialize(TaskLists);
        File.WriteAllText(tasksTempPath, tasksJson);
        File.Move(tasksTempPath, StoragePaths.Current.AllTasksPath, overwrite: true);

        // Same for trash file
        var trashTempPath = StoragePaths.Current.AllTrashPath + ".tmp";
        var trashJson = JsonSerializer.Serialize(TrashLists);
        File.WriteAllText(trashTempPath, trashJson);
        File.Move(trashTempPath, StoragePaths.Current.AllTrashPath, overwrite: true);
    }
}
```

**Why this works:**
- If crash occurs during write, temp file is incomplete but main file is untouched
- `File.Move` is atomic on POSIX and Windows filesystems
- `SaveLock` prevents concurrent writes

### Layer 2: Rolling Backups

Automatic backups on every save with retention limits:

```csharp
// src/TaskerCore/Backup/BackupConfig.cs
public static class BackupConfig
{
    public const int MaxVersionBackups = 10;      // Granular recovery
    public const int MaxDailyBackupDays = 7;      // Long-term safety net
    public const string BackupExtension = ".backup.json";
    public const string TimestampFormat = "yyyy-MM-ddTHH-mm-ss";
}
```

**Backup types:**
- **Version backups**: Created every save, keeps 10 most recent
- **Daily backups**: One per calendar day, keeps 7 days
- **Pre-restore backups**: Created before any restore operation

**File structure:**
```
backups/
├── all-tasks.2026-02-05T14-30-45.backup.json      # Version
├── all-tasks.trash.2026-02-05T14-30-45.backup.json
├── all-tasks.daily.2026-02-05.backup.json          # Daily
├── all-tasks.trash.daily.2026-02-05.backup.json
└── all-tasks.pre-restore.2026-02-05T15-00-00.json  # Safety
```

### Layer 3: CLI Recovery Commands

```bash
# List available backups (newest first)
tasker backup list

# Restore from most recent backup
tasker backup restore

# Restore from specific backup
tasker backup restore 3

# Skip confirmation prompt
tasker backup restore 1 --force
```

**Restore safety:**
1. Creates pre-restore backup first (can undo restore)
2. Prompts for confirmation (unless `--force`)
3. Restores both tasks and trash files
4. Clears undo history (checksums won't match)

## Implementation Files

| File | Purpose |
|------|---------|
| `src/TaskerCore/Backup/BackupManager.cs` | Core backup/restore/rotate logic |
| `src/TaskerCore/Backup/BackupConfig.cs` | Configuration constants |
| `src/TaskerCore/Backup/BackupInfo.cs` | Backup metadata record |
| `AppCommands/BackupCommand.cs` | CLI `backup list` and `backup restore` |
| `src/TaskerCore/Data/TodoTaskList.cs` | Atomic `Save()` method |

## Key Design Decisions

| Decision | Rationale |
|----------|-----------|
| Backup before save | Captures state before modification |
| Non-blocking backup | Backup failure doesn't prevent user's save |
| Both files backed up | Tasks + trash must be consistent |
| Pre-restore backup | Allows recovery if wrong backup restored |
| Undo history cleared | Checksums won't match after restore |
| 10 versions + 7 daily | Balances granularity with disk usage |

## Prevention Strategies

### Test Isolation

The root cause was tests writing to production paths. Fixed via:

```csharp
// Tests create isolated storage
public BackupManagerTests()
{
    _testDir = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid()}");
    Directory.CreateDirectory(_testDir);
    StoragePaths.SetDirectory(_testDir);  // Critical: isolate storage
}
```

### Verification Checklist

- [ ] Backup created on every save
- [ ] Daily backup only once per day
- [ ] Rotation deletes old backups
- [ ] Restore creates pre-restore backup
- [ ] No `.tmp` files left after save
- [ ] Backup failure doesn't block save

## Testing

11 unit tests in `tests/TaskerCore.Tests/Backup/BackupManagerTests.cs`:

- `CreateBackup_WhenNoTasksFile_DoesNotCreateBackup`
- `CreateBackup_WhenTasksFileExists_CreatesVersionBackup`
- `CreateBackup_CreatesDailyBackupOnce`
- `CreateBackup_BacksUpTrashFile`
- `ListBackups_ReturnsNewestFirst`
- `RotateBackups_DeletesOldestWhenOverLimit`
- `RestoreBackup_RestoresTasksFile`
- `RestoreBackup_CreatesPreRestoreBackup`
- `RestoreBackup_WithInvalidTimestamp_ThrowsBackupNotFoundException`
- `RestoreBackup_AlsoRestoresTrash`
- `AtomicWrite_Integration_SavesViaTempFile`

## Related Documentation

- [Brainstorm: Resilient Backup Recovery](../../brainstorms/2026-02-05-resilient-backup-recovery-brainstorm.md)
- [Plan: Resilient Backup Recovery](../../plans/2026-02-05-feat-resilient-backup-recovery-plan.md)
- [Test Infrastructure Setup](../testing/unit-test-infrastructure-setup.md)

## References

- MEMORY.md: Test Safety section documents the incident and recovery
- CLAUDE.md: Architecture v2.3 describes storage patterns
- `StoragePaths.cs`: Centralized path management with backup directory
