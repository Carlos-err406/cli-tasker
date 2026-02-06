---
title: "feat: Auto-filter tasks by project directory name"
type: feat
date: 2026-02-06
task: 9f5
brainstorm: docs/brainstorms/2026-02-06-project-integration-brainstorm.md
---

# feat: Auto-filter tasks by project directory name

## Overview

When running `tasker` in a directory whose name matches an existing list, auto-filter all commands to that list. No init command or config files needed — pure convention.

## Motivation

Users working on a project want to see only that project's tasks without typing `-l project-name` every time. If the directory is named `my-app` and a `my-app` list exists, just use it.

## Proposed Solution

Add `ResolveListFilter` to `ListManager` (where list resolution already lives) and a `--all` global option. Commands call the resolver instead of reading `listOption` directly. Priority: **`-l` > auto-detect (unless `--all`) > null**.

## Acceptance Criteria

- [x] When cwd name matches an existing list AND no `-l` is provided, commands auto-filter to that list
- [x] `--all` / `-a` global flag bypasses auto-detect and shows all lists
- [x] `-l <name>` always overrides auto-detect
- [x] Subtle indicator printed when auto-filter is active (e.g. `[dim](auto: list-name)[/]`)
- [x] `tasker add "task"` in auto-detected directory adds to the auto-detected list
- [x] Commands that don't use list filter (get, move, check, uncheck by ID) are unaffected
- [x] No files created in project directories
- [x] Tests cover resolution priority chain

## Implementation

### Step 1: Add `--all` global option

**File: `Program.cs`**

```csharp
var allOption = new Option<bool>("--all", "-a")
{
    Description = "Show all lists (bypass directory auto-detection)"
};
rootCommand.Options.Add(allOption);
```

Thread `allOption` through command factories the same way `listOption` is already threaded.

### Step 2: Add `ResolveListFilter` to `ListManager`

**File: `src/TaskerCore/Data/ListManager.cs`** (extend existing class — no new files)

```csharp
/// <summary>
/// Resolves effective list filter. Priority: explicitList > auto-detect (unless showAll) > null.
/// </summary>
public static string? ResolveListFilter(
    TaskerServices services,
    string? explicitList,
    bool showAll,
    string? workingDirectory = null)
{
    if (explicitList != null) return explicitList;
    if (showAll) return null;

    workingDirectory ??= Directory.GetCurrentDirectory();
    var dirName = Path.GetFileName(workingDirectory);
    return !string.IsNullOrEmpty(dirName) && ListExists(services, dirName) ? dirName : null;
}

public static string? ResolveListFilter(string? explicitList, bool showAll, string? workingDirectory = null)
    => ResolveListFilter(TaskerServices.Default, explicitList, showAll, workingDirectory);
```

Key design decisions from review:
- Lives in `ListManager` (not a new class) — this is list resolution logic
- `workingDirectory` parameter makes it testable without process-global state
- TaskerServices overload follows existing pattern
- No "project" naming — uses list terminology

### Step 3: Update commands that use `listOption`

**Only commands that actually read `listOption`** (don't touch ID-based commands):

| Command | Current | Change |
|---------|---------|--------|
| `AddCommand.cs` | `parseResult.GetValue(listOption) ?? GetDefaultList()` | `ResolveListFilter(explicit, showAll) ?? GetDefaultList()` |
| `ListCommand.cs` | `parseResult.GetValue(listOption)` | `ResolveListFilter(explicit, showAll)` |
| `DeleteCommand.cs` (clear only) | `parseResult.GetValue(listOption)` | `ResolveListFilter(explicit, showAll)` |
| `TrashCommand.cs` (list + clear) | `parseResult.GetValue(listOption)` | `ResolveListFilter(explicit, showAll)` |

**Commands NOT touched** (work by ID globally): `CheckCommand`, `RenameCommand`, `GetCommand`, `MoveCommand`.

Pattern in each updated command:

```csharp
var explicitList = parseResult.GetValue(listOption);
var showAll = parseResult.GetValue(allOption);
var listName = ListManager.ResolveListFilter(explicitList, showAll);
```

### Step 4: Add indicator on `list` command

When auto-detect is active, show a dim indicator before the task list:

```csharp
if (explicitList == null && !showAll && listName != null)
{
    Output.Info($"[dim](auto: {listName})[/]");
}
```

### Step 5: Tests

**File: `tests/TaskerCore.Tests/Data/ListManagerResolveTests.cs`**

- `ResolveListFilter_ExplicitListWins_OverAutoDetect`
- `ResolveListFilter_ShowAll_ReturnsNull`
- `ResolveListFilter_AutoDetects_WhenDirectoryMatchesList`
- `ResolveListFilter_ReturnsNull_WhenNoMatchingList`
- `ResolveListFilter_ExplicitList_IgnoresShowAll`

Use `workingDirectory` parameter for testability — no need to change process cwd.

## References

- `Program.cs` — command registration and global options
- `ListManager.cs` — list resolution and existence checks
- `ListCommand.cs:43` — where listOption is read
- `AddCommand.cs:22` — where listOption falls back to default
- Brainstorm: `docs/brainstorms/2026-02-06-project-integration-brainstorm.md`
