---
title: In-Progress Task Status
date: 2026-02-06
task: "731"
status: decided
---

# In-Progress Task Status

## What We're Building

Replace the binary checked/unchecked task status with a 3-state enum: **pending**, **in-progress**, **done**. The primary goal is personal awareness — seeing at a glance which tasks are actively being worked on vs. waiting.

## Why This Approach

- **3 states are enough.** Pending, in-progress, done covers the awareness need without workflow overhead. No "blocked" or "review" states — this is a personal task manager, not a project tracker.
- **Generic status command** replaces check/uncheck, making the system extensible without new commands for every status.
- **In-progress sorts first** because active work is what you need to see immediately.

## Key Decisions

### Status Model
- `TaskStatus` enum: `Pending = 0`, `InProgress = 1`, `Done = 2`
- Replaces `bool IsChecked` on `TodoTask` record
- SQLite column: `status INTEGER DEFAULT 0` (replaces `is_checked`)

### CLI Commands
- **New:** `tasker status <status> <...ids>` — generic status command accepting multiple IDs
  - `tasker status in-progress abc def` — mark tasks as in-progress
  - `tasker status done abc` — mark task as done
  - `tasker status pending abc` — reset to pending
- **Aliases kept:** `tasker check <ids>` → `status done`, `tasker uncheck <ids>` → `status pending`
- Both aliases and main command documented

### Visual Treatment
| Status | CLI | Tray |
|--------|-----|------|
| Pending | `[ ]` grey | Grey unchecked checkbox |
| In-progress | `[-]` yellow/amber | Yellow/amber indicator |
| Done | `[x]` green, strikethrough | Green checked, dimmed text |

### Sort Order
1. **In-progress** (active work — top)
2. **Pending** (backlog — middle)
3. **Done** (finished — bottom)

Within each group, existing sort applies: priority, due date, newest first.

### Undo System
- Single `SetStatusCommand` replaces `CheckTaskCommand` + `UncheckTaskCommand`
- Stores `OldStatus` and `NewStatus` for bidirectional undo/redo
- Register with `[JsonDerivedType(typeof(SetStatusCommand), "set-status")]`

### Filtering
- `tasker list -c` → show done only (backward compat alias)
- `tasker list -u` → show pending + in-progress (backward compat alias)
- Consider: `tasker list --status in-progress` for explicit filtering

### Database Migration
- Rename column: `is_checked` → `status`
- Migrate values: `0` (unchecked) → `0` (Pending), `1` (checked) → `2` (Done)
- Update index: `idx_tasks_sort` to use `status` instead of `is_checked`

## Surface Area (files to change)

| Layer | Files | Change |
|-------|-------|--------|
| Model | `TodoTask.cs` | `bool IsChecked` → `TaskStatus Status`, update methods |
| Enum | NEW `TaskStatus.cs` | Enum definition |
| Schema | `TaskerDb.cs` | Column rename + migration |
| Data | `TodoTaskList.cs` | Read/write mapping, sorting, filtering |
| CLI | NEW `StatusCommand.cs` | Generic status command |
| CLI | `CheckCommand.cs` | Become aliases to StatusCommand |
| CLI | `ListCommand.cs` | Update filter options |
| CLI | `Output.cs` | Status display formatting |
| TUI | `TuiRenderer.cs` | Checkbox rendering |
| Tray | `TodoTaskViewModel.cs` | Observable status property |
| Tray | `TaskListPopup.axaml.cs` | Status indicator UI |
| Tray | Converter | Multi-state color converter |
| Undo | NEW `SetStatusCommand.cs` | Replaces Check/Uncheck commands |
| Undo | `IUndoableCommand.cs` | Register new type |
| Stats | `TaskStats.cs` | Add InProgress count |
| Migration | `JsonMigrator.cs` | Map `IsChecked` bool → status int |

## Resolved Questions

### Tray Checkbox UX
- **Click** = toggle done (preserves quick check-off gesture)
- **Right-click** = set in-progress (secondary action, discoverable)
- The checkbox icon reflects current state: `[ ]` grey, `[-]` yellow, `[x]` green

### JSON Output Format
- `tasker get --json` outputs status as **string**: `"pending"`, `"in-progress"`, `"done"`
- Human-readable and self-documenting for scripts and agents

### CLI Shortcuts
- `tasker wip <ids>` — alias for `tasker status in-progress <ids>`
- `tasker check <ids>` — alias for `tasker status done <ids>`
- `tasker uncheck <ids>` — alias for `tasker status pending <ids>`

### Full Command Map
| Command | Effect |
|---------|--------|
| `tasker status pending <ids>` | Set to pending |
| `tasker status in-progress <ids>` | Set to in-progress |
| `tasker status done <ids>` | Set to done |
| `tasker wip <ids>` | Alias → in-progress |
| `tasker check <ids>` | Alias → done |
| `tasker uncheck <ids>` | Alias → pending |
