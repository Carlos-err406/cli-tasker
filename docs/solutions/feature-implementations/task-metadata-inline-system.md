---
title: Task Metadata System with Inline Parsing
category: feature-implementations
tags:
  - parsing
  - metadata
  - priority
  - due-dates
  - tags
  - display-formatting
  - shell-escaping
module: TaskerCore.Parsing
symptom: |
  Multiple issues with task metadata handling:
  1. Using !! for priority caused shell history expansion in bash
  2. Metadata markers were stripped from description text, losing data when editing
  3. Commands that set priority/due date didn't sync changes back to description text
  4. Priority indicators used ambiguous dots (·/·/·) across CLI, TUI, and Tray
  5. Tags displayed as plain text in TaskerTray instead of styled badges
root_cause: |
  1. Shell expansion: The !! token triggers bash history expansion before reaching CLI
  2. Metadata stripping: Parser extracted AND removed metadata, but editing needs raw text
  3. No sync mechanism: SetTaskPriority/SetTaskDueDate only updated model fields
  4. Inconsistent design: Priority formatting implemented independently in each UI
  5. Plain rendering: TaskerTray rendered tags as simple TextBlock elements
date_solved: 2026-02-04
files_changed:
  - src/TaskerCore/Parsing/TaskDescriptionParser.cs
  - src/TaskerCore/Parsing/DateParser.cs
  - src/TaskerCore/Models/TodoTask.cs
  - src/TaskerCore/Data/TodoTaskList.cs
  - Output.cs
  - Tui/TuiRenderer.cs
  - src/TaskerTray/Views/TaskListPopup.axaml.cs
---

# Task Metadata System with Inline Parsing

## Overview

A complete task metadata system that enables inline metadata in task descriptions with bidirectional synchronization between description text and structured fields.

**Key features:**
- Inline metadata syntax: `p1`/`p2`/`p3` (priority), `@date` (due dates), `#tag` (tags)
- Only parses last line if it's metadata-only
- Display vs storage separation (metadata hidden in UI but kept for editing)
- Commands sync changes back to description text
- Consistent priority display: `>>>` / `>>` / `>` across all UIs
- Colored tag pill badges in TaskerTray

## Solution

### 1. Shell-Safe Priority Syntax

**Problem:** `!!` triggered bash history expansion.

**Solution:** Changed to `p1`/`p2`/`p3` syntax.

```bash
# Before (broken)
tasker add "urgent task !!"  # Shell expands !! to last command

# After (works)
tasker add "urgent task
p1"
```

### 2. Metadata-Only Last Line Parsing

**Problem:** Metadata was stripped from description, losing it when editing.

**Solution:** Only parse last line if it contains ONLY metadata markers. Keep original text intact.

```csharp
// src/TaskerCore/Parsing/TaskDescriptionParser.cs
public static ParsedTask Parse(string input)
{
    var lines = input.Split('\n');
    var lastLine = lines[^1];

    // Check if last line is metadata-only
    var strippedLine = lastLine;
    strippedLine = PriorityRegex().Replace(strippedLine, " ");
    strippedLine = DueDateRegex().Replace(strippedLine, " ");
    strippedLine = TagRegex().Replace(strippedLine, " ");
    var isMetadataOnly = string.IsNullOrWhiteSpace(strippedLine);

    // Only parse if metadata-only, keep original description intact
    if (!isMetadataOnly)
        return new ParsedTask(input, null, null, [], false);

    // Extract priority, due date, tags from last line...
}
```

**Regex patterns:**
```csharp
[GeneratedRegex(@"(?:^|\s)p([123])(?:\s|$)", RegexOptions.IgnoreCase)]
private static partial Regex PriorityRegex();

[GeneratedRegex(@"@(\S+)")]
private static partial Regex DueDateRegex();

[GeneratedRegex(@"#(\w+)")]
private static partial Regex TagRegex();
```

### 3. Display vs Storage Separation

**Problem:** Users see metadata markers in task display.

**Solution:** `GetDisplayDescription()` hides metadata-only last line for UI, but keeps it in storage.

```csharp
public static string GetDisplayDescription(string description)
{
    var lines = description.Split('\n');
    if (lines.Length == 1) return description; // Single line: always show

    // Check if last line is metadata-only
    var lastLine = lines[^1];
    var stripped = PriorityRegex().Replace(lastLine, " ");
    stripped = DueDateRegex().Replace(stripped, " ");
    stripped = TagRegex().Replace(stripped, " ");

    if (string.IsNullOrWhiteSpace(stripped))
    {
        // Hide metadata-only last line
        return string.Join("\n", lines.Take(lines.Length - 1)).TrimEnd();
    }

    return description.TrimEnd();
}
```

### 4. Metadata Sync to Description

**Problem:** Running `tasker priority abc 1` didn't update the description text.

**Solution:** `SyncMetadataToDescription()` updates description when metadata changes.

```csharp
// src/TaskerCore/Data/TodoTaskList.cs
public TaskResult SetTaskPriority(string taskId, Priority? priority, bool recordUndo = true)
{
    var todoTask = GetTodoTaskById(taskId);
    // ... validation ...

    var updatedTask = priority.HasValue
        ? todoTask.SetPriority(priority.Value)
        : todoTask.ClearPriority();

    // Sync metadata back to description
    var syncedDescription = TaskDescriptionParser.SyncMetadataToDescription(
        updatedTask.Description, updatedTask.Priority, updatedTask.DueDate, updatedTask.Tags);
    updatedTask = updatedTask.Rename(syncedDescription);

    AddTaskToList(updatedTask);
    Save();
}
```

**Sync logic:**
```csharp
public static string SyncMetadataToDescription(string description, Priority? priority,
    DateOnly? dueDate, string[]? tags)
{
    var lines = description.Split('\n').ToList();
    var hasMetadataLine = /* check if last line is metadata-only */;

    // Build new metadata line
    var metaParts = new List<string>();
    if (priority.HasValue) metaParts.Add($"p{(int)priority}");
    if (dueDate.HasValue) metaParts.Add($"@{dueDate.Value:yyyy-MM-dd}");
    if (tags?.Length > 0) metaParts.AddRange(tags.Select(t => $"#{t}"));

    var newMetaLine = string.Join(" ", metaParts);

    if (hasMetadataLine)
        lines[^1] = newMetaLine; // Replace existing
    else if (!string.IsNullOrEmpty(newMetaLine))
        lines.Add(newMetaLine); // Append new

    return string.Join("\n", lines);
}
```

### 5. Consistent Priority Display

**Problem:** Priority showed as `·`/`·`/`·` - indistinguishable.

**Solution:** `>>>` / `>>` / `>` with consistent colors.

```csharp
// Output.cs (CLI)
public static string FormatPriority(Priority? priority) => priority switch
{
    Priority.High => "[red bold]>>>[/]",
    Priority.Medium => "[yellow]>> [/]",
    Priority.Low => "[blue]>  [/]",
    _ => "[dim]·  [/]"
};
```

| Priority | Symbol | CLI Color | TaskerTray Color |
|----------|--------|-----------|------------------|
| High (P1) | `>>>` | Red bold | Red |
| Medium (P2) | `>>` | Yellow | Orange |
| Low (P3) | `>` | Blue | DodgerBlue |
| None | `·` | Dim | (hidden) |

### 6. Tag Pill Badges

**Problem:** Tags displayed as plain text `#tag1 #tag2`.

**Solution:** Colored pill badges with hash-based colors.

```csharp
// src/TaskerTray/Views/TaskListPopup.axaml.cs
if (task.HasTags)
{
    var tagsPanel = new WrapPanel { Orientation = Horizontal, Margin = new(0, 4, 0, 0) };

    foreach (var tag in task.Tags!)
    {
        var tagPill = new Border
        {
            Background = new SolidColorBrush(GetTagColor(tag)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(6, 2),
            Margin = new Thickness(0, 0, 4, 2)
        };
        tagPill.Child = new TextBlock
        {
            Text = $"#{tag}",
            FontSize = 10,
            FontWeight = FontWeight.Medium,
            Foreground = Brushes.White
        };
        tagsPanel.Children.Add(tagPill);
    }
    contentPanel.Children.Add(tagsPanel);
}

private static Color GetTagColor(string tag)
{
    var colors = new[] {
        "#3B82F6", "#10B981", "#F59E0B", "#EF4444", "#8B5CF6",
        "#EC4899", "#06B6D4", "#84CC16", "#F97316", "#6366F1"
    };
    return Color.Parse(colors[Math.Abs(tag.GetHashCode()) % colors.Length]);
}
```

### 7. Natural Date Parsing

`DateParser` supports multiple formats:

| Format | Example | Description |
|--------|---------|-------------|
| Keywords | `@today`, `@tomorrow` | Relative to current day |
| Day names | `@monday`, `@friday` | Next occurrence |
| Month+day | `@jan15`, `@dec25` | Next occurrence |
| Relative | `@+3d`, `@+1w`, `@+2m` | Days, weeks, months |
| ISO | `@2026-01-15` | Standard format |

## Prevention

1. **Shell safety:** Avoid special shell characters (`!`, `$`, backticks) in inline syntax
2. **Test editing workflows:** Ensure metadata survives create → edit → save cycle
3. **Bidirectional sync:** Commands that modify metadata must update description text
4. **UI consistency:** Define formatting once, reuse across all UIs

## Related Documentation

- [Task Metadata System Plan](../../plans/2026-02-04-feat-task-metadata-system-plan.md)
- [Collapsible Lists Feature](./collapsible-lists-tray.md)
- [Tray Animations & Transitions](./tray-animations-transitions.md)
