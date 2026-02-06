# Resilient Backup & Recovery Mechanism

**Date:** 2026-02-05
**Status:** Ready for planning

## Context

On 2026-02-05, tests accidentally wiped production task data due to a test isolation bug. While the bug was fixed, it exposed the lack of automatic backup/recovery mechanisms. Data was partially recovered from old format files and conversation transcripts.

## What We're Building

An automatic backup and recovery system that protects against:
1. **Accidental overwrites** - Tests, bugs, or user error wiping the main file
2. **File corruption** - Crashes mid-write, disk errors, or malformed JSON

## Why This Approach

**Chosen:** Atomic Writes + Rolling Backups

**Rejected alternatives:**
- *Git-Style Snapshots* - Overkill complexity, deduplication not needed for small JSON files
- *Write-Ahead Log (WAL)* - Database-level reliability unnecessary for task manager

**Reasoning:** Simple, automatic, covers both scenarios. Uses existing `backups/` directory infrastructure already defined in `StoragePaths.cs`.

## Key Decisions

### 1. Atomic Writes
- Every save writes to temp file first, then renames (atomic on most filesystems)
- Prevents partial writes from corrupting main file
- Pattern: `Write to .tmp` → `Rename .tmp to .json`

### 2. Rolling Backups
- Keep last **10 versions** for recent granularity
- Keep **7 daily backups** for longer-term safety net
- Stored in `~/Library/Application Support/cli-tasker/backups/`
- Naming: `all-tasks.2026-02-05T14-30-00.json` (version) and `all-tasks.daily.2026-02-05.json` (daily)

### 3. Automatic Operation
- Backups created silently on every save
- No user intervention required for normal operation
- Restore via explicit command: `tasker backup restore [timestamp]`

### 4. Backup Rotation
- Delete version backups beyond 10 most recent
- Delete daily backups older than 7 days
- Run cleanup after each backup creation

### 5. Restore Command
```
tasker backup list              # Show available backups
tasker backup restore           # Restore most recent backup
tasker backup restore <timestamp>  # Restore specific backup
```

## Implementation Scope

### Files to Modify
- `src/TaskerCore/Data/TodoTaskList.cs` - Add atomic write + backup before save
- `src/TaskerCore/StoragePaths.cs` - Add backup file path helpers

### New Files
- `src/TaskerCore/Backup/BackupManager.cs` - Backup creation, rotation, restore logic
- `AppCommands/BackupCommand.cs` - CLI commands for backup operations

### Existing Infrastructure to Use
- `StoragePaths.BackupDirectory` - Already defined
- `StoragePaths.EnsureBackupDirectory()` - Already defined
- `SaveLock` pattern - For thread-safe backup operations

## Open Questions

1. Should trash file (`all-tasks.trash.json`) also be backed up?
2. Should config file be backed up? (probably not - it's just default list setting)
3. Should backup restore also restore undo history, or clear it?

## Success Criteria

- [ ] No data loss possible from mid-write crashes (atomic writes)
- [ ] Can recover from any of last 10 saves
- [ ] Can recover from any of last 7 days
- [ ] Zero user effort for protection (automatic)
- [ ] Simple restore via CLI command
- [ ] Backup disk usage stays bounded (rotation works)
