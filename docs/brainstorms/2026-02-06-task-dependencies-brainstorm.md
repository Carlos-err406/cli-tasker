---
title: "Task dependencies: subtasks and blocking"
date: 2026-02-06
task: 61d
tags: [feature, dependencies, subtasks, blocking]
---

# Task Dependencies Brainstorm

## What We're Building

Two relationship types between tasks, integrated into the existing inline metadata parsing system:

1. **Subtasks** (`^parentId`) — hierarchical parent-child. Unlimited nesting. Checking a parent cascades completion to all descendants.

2. **Blocking** (`!blockedId`) — directional dependency. Task A blocks task B. Visual-only (no enforcement). Informational to help decide what to work on.

Both features span all three surfaces: CLI, TUI, and Tray.

## Why This Approach

**Inline parsing** leverages the existing `TaskDescriptionParser` pattern where the last line of a description contains metadata tokens (`p1`, `@friday`, `#urgent`). Adding `^abc` and `!def` to this system means:

- Zero new CLI flags needed for task creation
- Consistent UX: all metadata lives in the same place
- `SyncMetadataToDescription()` already handles updating metadata lines
- Parser already has regex infrastructure for new token types

**Data model: parent_id column + join table**

- `parent_id TEXT REFERENCES tasks(id)` on the `tasks` table for subtask hierarchy
- `task_dependencies(task_id, blocks_task_id)` join table for blocking relationships
- Inline tokens (`^abc`, `!def`) are parsed and stored as structured data (like `#tag` → `tags` column)
- The metadata line in the description is the source of truth during creation; DB columns are the source of truth after

## Key Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Inline syntax | `^abc` = subtask of abc, `!def` = blocks def | Consistent with `#tag`, `p1`, `@date` parsing pattern |
| Nesting depth | Unlimited | Full tree structure |
| Parent completion | Cascade down | Checking parent auto-checks all subtasks |
| Blocking enforcement | Visual only | Show indicators, don't prevent actions |
| Subtask sorting | Sort independently | Subtasks sort by their own priority/due date like any task |
| Cross-list subtasks | Prevented | Subtasks must be in same list as parent |
| Cascade move | Yes | Moving parent moves all subtasks to new list |
| Display style | Due-date style lines | Separate dim text line below description for each relationship |
| Context in display | ID + truncated title | "Subtask of (abc) build mobile app" — shows what the reference is about |
| CLI management | Inline + deps subcommands | Inline for creation, `tasker deps` for managing existing tasks |
| Parent cardinality | Single parent only | Tree structure, not DAG. Simpler model. |
| Blocking cardinality | Many-to-many | A task can block many and be blocked by many |
| Relationship display | Multi-line lists | `Blocked by: - (id) title` with one line per reference |
| `!` direction | `!h67` = "I block h67" | Active/assertive — consistent with `^abc` = "I am child of abc" |
| Circular blocking | Prevent but don't lose task | Check on insert, warn but keep the task. Never discard user text. |
| Undo cascade-check | Single batch undo | Uses existing `BeginBatch()`/`EndBatch()` |
| Delete parent | Cascade delete subtasks | FK `ON DELETE CASCADE`, consistent with list cascade |
| `tasker get` output | Shows all relationships | No separate `deps show` command. Get is the single detail view. |
| Display cap | Show all, no cap | Always render every relationship line |

## Inline Parsing Design

### New regex tokens in TaskDescriptionParser

```
^(\w{3})   → parent_id (subtask of)
!(\w{3})   → blocks task_id
```

Example task creation:

```bash
tasker add "research frameworks
^abc !h67 #feature p2"
```

Parsed result:
- Description: "research frameworks\n^abc !h67 #feature p2"
- Priority: Medium (p2)
- Tags: ["feature"]
- Parent: "abc" (^abc)
- Blocks: ["h67"] (!h67)

The last line contains only metadata tokens → hidden from display description.

### SyncMetadataToDescription updates

When relationships change via `tasker deps` commands, the metadata line is updated to reflect the new state, just like how priority/tags/due date sync works today.

## CLI Interface

### Inline creation

```bash
# Create a subtask of abc
tasker add "research frameworks ^abc"

# Create a task that blocks h67
tasker add "set up database !h67"

# Both at once
tasker add "build API ^abc !h67 #feature p1"
```

### Deps subcommand group (for existing tasks)

```bash
# Set parent (make de1 a subtask of abc)
tasker deps set-parent de1 abc

# Remove parent (make de1 a top-level task again)
tasker deps unset-parent de1

# Add blocking relationship (abc blocks h67)
tasker deps add-blocker abc h67

# Remove blocking relationship
tasker deps remove-blocker abc h67

# View all relationships for a task
tasker deps show abc
# Output:
#   Subtasks:
#     (de1) research frameworks
#     (f23) build prototype
#   Blocks:
#     (h67) deploy to production
#   Blocked by:
#     (g45) write documentation
```

## Cardinality Rules

| Relationship | Cardinality | Notes |
|-------------|-------------|-------|
| Parent | A task has **at most 1** parent | Tree structure, not DAG |
| Subtasks | A task can have **many** subtasks | Each subtask has exactly 1 parent |
| Blocks | A task can block **many** tasks | Stored in join table |
| Blocked by | A task can be blocked by **many** tasks | Inverse of blocks |

## Display Design

### Multi-line relationship indicators

When a task has multiple relationships, each is listed on its own line:

**TUI (flat list with relationship lines)**

```
(abc) >>> [ ] build mobile app          #feature
(de1)  >  [ ] research frameworks       #feature
              Subtask of (abc) build mobile app
(f23)  >  [ ] build prototype
              Subtask of (abc) build mobile app
(h67)  >  [ ] deploy to production
              Blocked by: - (abc) build mobile app
                          - (g45) write documentation
(j89) >>> [-] set up infrastructure
              Subtasks: - (k12) provision servers
                        - (m34) configure CI/CD
                        - (n56) set up monitoring
              Blocks: - (h67) deploy to production
```

- Single relationship: `Subtask of (id) title...` or `Blocked by (id) title...` (no list prefix)
- Multiple relationships: `Blocked by: - (id1) title...` / `              - (id2) title...`
- Subtasks shown on the parent: `Subtasks: - (id1) title...` / `           - (id2) title...`
- All relationship lines are dim text at the same indent as continuation lines
- Each line counted in line height for viewport budget

### Tray (due-date style indicator lines)

Following the existing indicator pattern:
- Priority: colored text before title (>>>, >>, >)
- Due date: separate dim line below description
- Tags: colored pills in WrapPanel

**Single relationship:**

```
┌────────────────────────────────────┐
│ ☐ >>> research frameworks  #feature│
│     Subtask of (abc) build mob...  │  ← dim gray, 10pt
│     Due: Friday                    │
└────────────────────────────────────┘
```

**Multiple relationships (multi-line):**

```
┌────────────────────────────────────┐
│ ☐  >  deploy to production         │
│     Blocked by:                    │  ← dim orange, 10pt
│       - (abc) build mobile app     │
│       - (g45) write documentation  │
│     Due: Tomorrow                  │
└────────────────────────────────────┘
```

```
┌────────────────────────────────────┐
│ ☐ >>> set up infrastructure        │
│     Subtasks:                      │  ← dim gray, 10pt
│       - (k12) provision servers    │
│       - (m34) configure CI/CD      │
│     Blocks:                        │  ← dim orange, 10pt
│       - (h67) deploy to production │
└────────────────────────────────────┘
```

- **Subtask of**: dim gray (child → parent reference)
- **Subtasks**: dim gray (parent → children list)
- **Blocked by**: dim orange/yellow (shows what's blocking this task)
- **Blocks**: dim orange/yellow (shows what this task blocks)
- Position: below description, above due date
- Truncate referenced task title to fit available width

### TUI keybindings

- `s` on a task: create subtask (opens add mode with `^parentId` pre-filled in metadata)
- When checking a parent: auto-check all subtasks, show "Checked (abc) and 3 subtasks"

## Schema Changes

```sql
-- Add to tasks table
ALTER TABLE tasks ADD COLUMN parent_id TEXT REFERENCES tasks(id) ON DELETE CASCADE;
CREATE INDEX idx_tasks_parent_id ON tasks(parent_id);

-- New table for blocking relationships
CREATE TABLE IF NOT EXISTS task_dependencies (
    task_id TEXT NOT NULL REFERENCES tasks(id) ON DELETE CASCADE,
    blocks_task_id TEXT NOT NULL REFERENCES tasks(id) ON DELETE CASCADE,
    PRIMARY KEY (task_id, blocks_task_id),
    CHECK (task_id != blocks_task_id)
);
```

## Model Changes

```csharp
// TodoTask record gains:
public record TodoTask(
    string Id, string Description, TaskStatus Status, DateTime CreatedAt,
    string ListName, DateOnly? DueDate = null, Priority? Priority = null,
    string[]? Tags = null, DateTime? CompletedAt = null,
    string? ParentId = null  // NEW
);

// ParsedTask gains:
public record ParsedTask(
    string Description, Priority? Priority, DateOnly? DueDate,
    string[] Tags, bool LastLineIsMetadataOnly,
    string? ParentId = null,      // NEW: from ^abc
    string[]? BlocksIds = null    // NEW: from !def
);
```

## Resolved Questions

1. **Circular blocking**: Prevent cycles with a check on insert. But if the task is being created via `tasker add`, don't discard the task — create it successfully, just skip the invalid blocking relationship and show a warning. The user's text should never be lost due to a relationship error.

2. **Undo cascade**: Cascade-check (checking parent + all subtasks) is undoable as a single batch operation using the existing `BeginBatch()`/`EndBatch()` pattern.

3. **Deleting a parent**: FK `ON DELETE CASCADE` — deleting a parent deletes all subtasks. Clean and consistent with existing list cascade behavior.

4. **Get vs deps show**: Merge relationship display into `tasker get`. No separate `deps show` command needed. `tasker get abc` shows subtasks, blocks, and blocked-by sections alongside existing task info.

5. **Blocking direction**: `!h67` on task A means "A blocks h67". The `!` token is active/assertive — "I block this task." Consistent: `^abc` = "I am a child of abc", `!h67` = "I block h67".

6. **Display threshold**: Show all relationship lines always. No cap. Accurate representation even if it takes vertical space.
