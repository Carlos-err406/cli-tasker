---
title: Show Linked Task Status in Relationship Lines
type: feat
date: 2026-02-07
task: 03c
brainstorm: docs/brainstorms/2026-02-07-linked-task-status-indicator-brainstorm.md
---

# Show Linked Task Status in Relationship Lines

## Overview

When viewing a task's relationship lines (parent, subtasks, blocks, blocked-by, related), append a colored status label after the linked task's title. All three statuses shown: `[Done]` in green, `[In Progress]` in yellow, nothing for Pending. Dynamic — reads status at display time, no DB/migration changes.

## Acceptance Criteria

- [x] CLI `tasker list`: each relationship line shows linked task status after title
- [x] CLI `tasker get`: each relationship line shows linked task status after title
- [x] CLI `tasker get --json`: each linked task object includes `"status"` field
- [x] TUI: each relationship line shows linked task status after title
- [x] Tray popup: each relationship line shows linked task status after title
- [x] Pending tasks show no label (clean, no clutter)
- [x] Deleted/missing tasks still show "?" (existing behavior unchanged)
- [x] Status label appears AFTER the truncated title, not within truncation budget
- [x] Colors: green for Done, yellow for In Progress across all surfaces
- [x] `dotnet test` — all tests pass
- [x] `dotnet build` — no warnings

## Implementation

### Shared Helper — `Output.cs`

Add a Spectre.Console markup helper for CLI + TUI surfaces:

```csharp
// Helpers/Output.cs
public static string FormatLinkedStatus(TaskStatus status) => status switch
{
    TaskStatus.Done => " [green]Done[/]",
    TaskStatus.InProgress => " [yellow]In Progress[/]",
    _ => ""
};
```

### Phase 1: CLI List — `AppCommands/ListCommand.cs`

**Lines ~144-199** — each relationship line already calls `GetTodoTaskById()` which returns the full `TodoTask`. Append status label after the title.

Current pattern (repeated 5 times):
```csharp
var parent = taskList.GetTodoTaskById(parsed.ParentId);
var parentTitle = parent != null
    ? Markup.Escape(StringHelpers.Truncate(...))
    : "?";
Output.Markup($"{indent}[dim]↑ Subtask of ({parsed.ParentId}) {parentTitle}[/]");
```

New pattern:
```csharp
var parent = taskList.GetTodoTaskById(parsed.ParentId);
var parentTitle = parent != null
    ? Markup.Escape(StringHelpers.Truncate(...))
    : "?";
var parentStatus = parent != null ? Output.FormatLinkedStatus(parent.Status) : "";
Output.Markup($"{indent}[dim]↑ Subtask of ({parsed.ParentId}) {parentTitle}[/]{parentStatus}");
```

Note: status label is OUTSIDE the `[dim]...[/]` tag so it renders in its own color (green/yellow), not dimmed.

Apply to all 5 relationship blocks: parent, subtasks, blocks, blocked-by, related.

### Phase 2: CLI Get — `AppCommands/GetCommand.cs`

**Lines ~141-190** — same pattern as list. Each block calls `GetTodoTaskById()`.

Current:
```csharp
Output.Markup($"               [dim]({subId}) {Spectre.Console.Markup.Escape(subDesc)}[/]");
```

New:
```csharp
var subStatus = sub != null ? Output.FormatLinkedStatus(sub.Status) : "";
Output.Markup($"               [dim]({subId}) {Spectre.Console.Markup.Escape(subDesc)}[/]{subStatus}");
```

Apply to all 5 relationship blocks.

**JSON output** (`OutputJson`, lines ~62-113): Add `status` field to each linked task object:

```csharp
var subtaskObjs = (parsed.HasSubtaskIds ?? []).Select(id =>
{
    var s = taskList.GetTodoTaskById(id);
    return new {
        id,
        description = s != null ? StringHelpers.Truncate(s.Description, 50) : "?",
        status = s?.Status switch
        {
            TaskStatus.Done => "done",
            TaskStatus.InProgress => "in-progress",
            _ => "pending"
        }
    };
}).ToArray();
```

Apply to all 4 linked task selects (subtasks, blocks, blockedBy, related). Also add status for parent:
```csharp
parentId = parsed.ParentId,
parentStatus = parsed.ParentId != null
    ? (taskList.GetTodoTaskById(parsed.ParentId)?.Status switch { ... })
    : null,
```

### Phase 3: TUI — `Tui/TuiRenderer.cs`

**Lines ~190-254** — same pattern. Append status after title.

Current:
```csharp
WriteLineCleared($"{indent}[dim]↑ Subtask of ({parsed.ParentId}) {parentTitle}[/]");
```

New:
```csharp
var parentStatus = parent != null ? Output.FormatLinkedStatus(parent.Status) : "";
WriteLineCleared($"{indent}[dim]↑ Subtask of ({parsed.ParentId}) {parentTitle}[/]{parentStatus}");
```

Apply to all 5 blocks.

**CountTaskLines** (lines 259-275): No change needed — relationship lines are counted as 1 line each, and the added status label doesn't affect line wrapping (these lines aren't word-wrapped).

### Phase 4: Tray ViewModel — `src/TaskerTray/ViewModels/TodoTaskViewModel.cs`

**`LoadRelationships()` lines ~246-310** — include status in the display strings.

Current:
```csharp
return $"Subtask ({subId}) {title}";
```

New:
```csharp
var statusLabel = sub?.Status switch
{
    TaskStatus.Done => " [Done]",
    TaskStatus.InProgress => " [In Progress]",
    _ => ""
};
return $"Subtask ({subId}) {title}{statusLabel}";
```

Apply to all 5 relationship types (parent, subtasks, blocks, blocked-by, related).

### Phase 5: Tray Popup Rendering — `src/TaskerTray/Views/TaskListPopup.axaml.cs`

**Lines ~1304-1331** — the `AddRelationshipLabel` helper renders a single-color TextBlock. To show the status in a different color, modify the rendering to split the label.

**Option A (simple):** Keep the status text in the same TextBlock — it renders as the same color as the relationship line, but the `[Done]`/`[In Progress]` text is still informative even without distinct color.

**Option B (rich):** Create a helper that builds a `TextBlock` with `Inlines` collection for multi-color text:

```csharp
private static void AddRelationshipLabel(StackPanel parent, string text, string color,
    string? statusLabel = null, string? statusColor = null)
{
    var tb = new TextBlock
    {
        FontSize = 10,
        TextTrimming = TextTrimming.CharacterEllipsis,
        Margin = new Thickness(0, 2, 0, 0)
    };
    tb.Inlines!.Add(new Run(text) { Foreground = new SolidColorBrush(Color.Parse(color)) });
    if (!string.IsNullOrEmpty(statusLabel))
    {
        tb.Inlines.Add(new Run($" {statusLabel}")
        {
            Foreground = new SolidColorBrush(Color.Parse(statusColor ?? color))
        });
    }
    parent.Children.Add(tb);
}
```

Then at call sites:
```csharp
// Determine status color based on linked task
var (statusLabel, statusColor) = linkedTask?.Status switch
{
    TaskStatus.Done => ("Done", "#10B981"),
    TaskStatus.InProgress => ("In Progress", "#F59E0B"),
    _ => ((string?)null, (string?)null)
};
AddRelationshipLabel(contentPanel, $"↳ {line}", "#777", statusLabel, statusColor);
```

This requires `LoadRelationships()` to also expose the raw status, or passing the status info alongside the display string. The simplest approach: include status in the ViewModel display strings (Phase 4) and parse it in the popup, OR add parallel status arrays.

**Recommended:** Use **Option A** for simplicity — the `[Done]`/`[In Progress]` text included in the display string from Phase 4 renders in the relationship line color. This is readable and avoids complicating the rendering pipeline. If the user wants distinct colors, upgrade to Option B later.

## Files Changed

| File | Change |
|------|--------|
| `Helpers/Output.cs` | Add `FormatLinkedStatus()` helper |
| `AppCommands/ListCommand.cs` | Append status to 5 relationship line blocks |
| `AppCommands/GetCommand.cs` | Append status to 5 human-readable blocks + 4 JSON selects + parent |
| `Tui/TuiRenderer.cs` | Append status to 5 relationship line blocks |
| `src/TaskerTray/ViewModels/TodoTaskViewModel.cs` | Include status label in 5 display string builders |
| `src/TaskerTray/Views/TaskListPopup.axaml.cs` | No change if Option A; modify `AddRelationshipLabel` if Option B |

## References

- Brainstorm: `docs/brainstorms/2026-02-07-linked-task-status-indicator-brainstorm.md`
- Three-surface consistency: `docs/reference/conventions.md`
- Relationship display: `docs/solutions/feature-implementations/task-dependencies-subtasks-blocking.md`
- TUI line counting sync: `docs/solutions/ui-bugs/tui-buffered-rendering-and-line-wrapping.md`
- Existing relationship rendering: `AppCommands/ListCommand.cs:141-199`, `Tui/TuiRenderer.cs:187-254`
