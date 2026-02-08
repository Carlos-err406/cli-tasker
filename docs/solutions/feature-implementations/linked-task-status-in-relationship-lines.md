---
title: Show Linked Task Status in Relationship Lines Across All Surfaces
category: feature-implementations
tags:
  - relationships
  - status
  - display
  - three-surface
  - spectre-console
  - avalonia
  - inlines
module: CLI, TUI, TaskerTray
symptoms:
  - No way to tell if a linked task is done or in-progress from the relationship line
  - Must open each linked task individually to check its status
date_solved: 2026-02-07
files_changed:
  - Output.cs
  - AppCommands/ListCommand.cs
  - AppCommands/GetCommand.cs
  - Tui/TuiRenderer.cs
  - src/TaskerTray/ViewModels/TodoTaskViewModel.cs
  - src/TaskerTray/Views/TaskListPopup.axaml.cs
---

# Show Linked Task Status in Relationship Lines

## Problem

When viewing task relationship lines (parent, subtasks, blocks, blocked-by, related), there was no indication of the linked task's current status. Users had to navigate to each linked task individually to check if it was done, in-progress, or pending.

## Solution

Append a colored status label after the linked task's title in every relationship line across all three surfaces. The status is read dynamically at display time from the existing `GetTodoTaskById()` call — no extra DB queries needed.

**Status labels:**
- **Done** → green "Done"
- **In Progress** → yellow "In Progress"
- **Pending** → nothing (default, no clutter)

### CLI + TUI: Spectre.Console Markup

Shared helper in `Output.cs`:

```csharp
public static string FormatLinkedStatus(TaskStatus status) => status switch
{
    TaskStatus.Done => " [green]Done[/]",
    TaskStatus.InProgress => " [yellow]In Progress[/]",
    _ => ""
};
```

Status is placed **outside** the `[dim]` tag so it renders in its own color:

```csharp
var parentStatus = parent != null ? Output.FormatLinkedStatus(parent.Status) : "";
Output.Markup($"{indent}[dim]↑ Subtask of ({parsed.ParentId}) {parentTitle}[/]{parentStatus}");
```

### Tray: Avalonia Inlines with Colored Run Elements

The Tray uses `TextBlock.Inlines` to render multi-colored text within a single TextBlock:

```csharp
private static void AddRelationshipLabel(StackPanel parent, string text, string color,
    TaskStatus? linkedStatus = null)
{
    var tb = new TextBlock
    {
        FontSize = 10,
        TextTrimming = TextTrimming.CharacterEllipsis,
        Margin = new Thickness(0, 2, 0, 0)
    };
    tb.Inlines!.Add(new Run(text) { Foreground = new SolidColorBrush(Color.Parse(color)) });

    var (statusLabel, statusColor) = linkedStatus switch
    {
        TaskStatus.Done => ("Done", "#10B981"),
        TaskStatus.InProgress => ("In Progress", "#F59E0B"),
        _ => ((string?)null, (string?)null)
    };

    if (statusLabel != null)
    {
        tb.Inlines.Add(new Run($" {statusLabel}")
        {
            Foreground = new SolidColorBrush(Color.Parse(statusColor!))
        });
    }

    parent.Children.Add(tb);
}
```

Requires `using Avalonia.Controls.Documents;` for the `Run` class.

### ViewModel: Parallel Status Arrays

The `TodoTaskViewModel` exposes status alongside display strings via parallel arrays:

```csharp
public TaskStatus? ParentStatus { get; private set; }
public TaskStatus[]? SubtasksStatuses { get; private set; }
public TaskStatus[]? BlocksStatuses { get; private set; }
public TaskStatus[]? BlockedByStatuses { get; private set; }
public TaskStatus[]? RelatedStatuses { get; private set; }
```

Call sites use index-based `for` loops instead of `foreach` to access both arrays:

```csharp
for (var i = 0; i < task.SubtasksDisplay!.Length; i++)
    AddRelationshipLabel(contentPanel, $"↳ {task.SubtasksDisplay[i]}", "#777",
        task.SubtasksStatuses?[i]);
```

## Key Insights

1. **Dynamic over static**: Reading status at display time avoids cascade logic on status changes and prevents desync.

2. **Spectre markup placement matters**: Status must go OUTSIDE `[dim]...[/]` tags to render in its own color. Placing it inside would dim the green/yellow.

3. **Avalonia multi-color text**: Use `TextBlock.Inlines` with multiple `Run` elements for different colors within one line. A single `TextBlock.Text` property only supports one color.

4. **No TUI CountTaskLines change needed**: Relationship lines are counted as 1 line each (no wrapping). The short status label doesn't affect viewport calculations.

## Cross-References

- [Bidirectional Relationship Markers](./bidirectional-relationship-markers-and-related.md) — the relationship system this builds on
- [Task Dependencies and Subtasks](./task-dependencies-subtasks-blocking.md) — three-surface display patterns
- [Avalonia CheckBox Centering](../ui-bugs/avalonia-checkbox-internal-padding-centering.md) — another Avalonia rendering gotcha
