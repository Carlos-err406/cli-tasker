---
title: "feat: Add --recursive flag to tasker get"
type: feat
date: 2026-02-07
task: 800
brainstorm: docs/brainstorms/2026-02-07-recursive-get-brainstorm.md
---

# Feat: Add --recursive flag to tasker get

## Overview

Add a `--recursive` flag to `tasker get` that traverses the full relationship graph and displays a nested tree with full task details at each node. Supports both human-readable (Spectre.Console `Tree`) and `--json` output. CLI surface only.

## Acceptance Criteria

- [x] `tasker get <id> --recursive` shows nested tree with full task details
- [x] `tasker get <id> --recursive --json` shows nested JSON with full task objects
- [x] All 5 relationship types traversed: parent, subtasks, blocks, blockedBy, related
- [x] Cycle-safe: already-visited tasks show `(see above)` marker instead of recursing
- [x] Missing tasks show `(task not found)` inline
- [x] No depth limit
- [x] Without `--recursive`, behavior is unchanged
- [x] `dotnet build` — no errors
- [x] Tests pass

## Implementation

### Step 1: Add `--recursive` option to `GetCommand.cs`

**File:** `AppCommands/GetCommand.cs`

Add the option (after line 27):

```csharp
var recursiveOption = new Option<bool>("--recursive", "-r")
{
    Description = "Recursively show all related tasks in a tree"
};
getCommand.Options.Add(recursiveOption);
```

Parse it in the handler and pass to output methods:

```csharp
var recursive = parseResult.GetValue(recursiveOption);

if (asJson)
    OutputJson(task, taskList, recursive);
else
    OutputHumanReadable(task, taskList, recursive);
```

### Step 2: Add recursive human-readable output using Spectre.Console `Tree`

**File:** `AppCommands/GetCommand.cs`

When `recursive` is true, use Spectre.Console's `Tree` widget instead of flat output.

Create a helper method:

```csharp
private static void OutputRecursiveTree(TodoTask rootTask, TodoTaskList taskList)
{
    var visited = new HashSet<string>();
    var tree = new Tree(FormatTaskNode(rootTask));
    AddRelationshipNodes(tree, rootTask, taskList, visited);
    AnsiConsole.Write(tree);
}
```

`FormatTaskNode(TodoTask task)` returns a markup string with the same fields as the current flat output (ID, status checkbox, priority, due, tags, list, description) — but compact, multi-line within a single tree node.

`AddRelationshipNodes(IHasTreeNodes parent, TodoTask task, TodoTaskList taskList, HashSet<string> visited)` parses the task's relationships, and for each:
- If visited → add a node with `[dim](id) (see above)[/]`
- If task not found → add a node with `[dim](id) (task not found)[/]`
- Otherwise → mark visited, add full node, recurse

**Traversal order:** Parent, Subtasks, Blocks, Blocked by, Related — matches current display order in `OutputHumanReadable()`.

**Relationship labels as section nodes:** Each relationship type gets a labeled intermediate node (e.g., `[bold]Subtasks:[/]`) with children underneath, matching the current flat output structure. Only show sections that have items.

### Step 3: Add recursive JSON output

**File:** `AppCommands/GetCommand.cs`

When `recursive` is true in `OutputJson()`, produce nested structure:

```json
{
  "id": "abc",
  "description": "task text",
  "status": "pending",
  "priority": "high",
  "dueDate": "2026-03-01",
  "tags": ["feature"],
  "listName": "work",
  "createdAt": "2026-02-07T10:00:00",
  "completedAt": null,
  "parent": { ...full task object with own relationships... } | null,
  "subtasks": [ { ...full task objects... } ],
  "blocks": [ { ...full task objects... } ],
  "blockedBy": [ { ...full task objects... } ],
  "related": [ { ...full task objects... } ]
}
```

Already-visited tasks appear as: `{ "id": "abc", "$ref": true }` — just the ID and a ref marker, no recursion.

Missing tasks appear as: `{ "id": "xyz", "error": "task not found" }`.

Create a helper:

```csharp
private static object BuildJsonTree(TodoTask task, TodoTaskList taskList, HashSet<string> visited)
```

Returns an anonymous object with full fields + recursed relationships. Uses the same visited set pattern.

### Step 4: Add tests

**File:** `tests/TaskerCore.Tests/Commands/GetCommandRecursiveTests.cs` (new file)

Test cases:
1. **No relationships** — recursive output matches non-recursive (single task)
2. **Simple chain** — A has subtask B, B has subtask C → 3-level tree
3. **Cycle detection** — A related to B, B related to A → B shows `(see above)` on second encounter
4. **Missing task reference** — A has subtask marker for non-existent ID → shows `(task not found)`
5. **Mixed relationship types** — task with parent + subtasks + blocks + related
6. **Cross-list** — relationships spanning different lists
7. **JSON recursive** — verify nested JSON structure

Since `GetCommand` writes directly to console, tests should verify the underlying tree-building logic. Extract `BuildJsonTree()` and the traversal helpers as `internal` methods testable via `InternalsVisibleTo`.

### Step 5: Update documentation

**File:** `docs/reference/commands.md`

Update the get command row to mention `--recursive` flag.

## Files Changed

| File | Change |
|------|--------|
| `AppCommands/GetCommand.cs` | Add `--recursive` option, recursive tree output, recursive JSON output |
| `tests/TaskerCore.Tests/Commands/GetCommandRecursiveTests.cs` | New test file |
| `docs/reference/commands.md` | Document `--recursive` flag |

## Design Decisions

1. **Spectre.Console `Tree`** — built-in tree widget handles indentation, box-drawing characters, and terminal width. No need to hand-roll tree formatting.
2. **Visited set (not depth limit)** — cycle-safe without arbitrary limits. Task graphs are small.
3. **`$ref` pattern in JSON** — prevents circular references while keeping output parseable. Agents can detect `$ref: true` and look up the task by ID from an earlier node.
4. **Relationship sections as tree nodes** — keeps the visual grouping (Parent/Subtasks/Blocks/etc.) consistent with current flat output.
5. **Full task details** — each node shows all fields (ID, status, priority, due, tags, description). Agents need complete context.

## References

- Brainstorm: `docs/brainstorms/2026-02-07-recursive-get-brainstorm.md`
- Current GetCommand: `AppCommands/GetCommand.cs:13-204`
- Spectre.Console Tree: built into Spectre.Console 0.54.0 (already a dependency)
- Existing cycle detection pattern: `TodoTaskList.HasCircularBlocking()` at `src/TaskerCore/Data/TodoTaskList.cs`
