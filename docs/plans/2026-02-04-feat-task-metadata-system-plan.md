---
title: "feat: Task Metadata System - Due Dates and Priority"
type: feat
date: 2026-02-04
deepened: 2026-02-04
reviewed: 2026-02-04
---

# Task Metadata System (Phase 1: Due Dates & Priority)

## Review Summary

**Reviewed by:** DHH Rails Reviewer, Kieran Rails Reviewer, Code Simplicity Reviewer
**Review date:** 2026-02-04

### Critical Bugs Fixed (from Kieran)
1. **Regex replace bug** - Was finding LAST match but removing FIRST; now consistent
2. **Circular method call** - `GetFilteredTasks()` ↔ `GetSortedTasks()` loop; renamed methods
3. **Tag validation in wrong place** - Moved from TodoTask record to TagHelper utility
4. **Phase ordering** - Data Layer now comes before CLI Commands

### Design Simplifications (from DHH & Simplicity)
1. **Tags deferred to Phase 2** - Ship due dates + priority first, add tags if users request
2. **Inline parsing is optional** - Commands-only approach works; inline parsing in Phase 2
3. **TUI/Tray editing simplified** - Display metadata everywhere, edit via CLI initially
4. **Undo simplified** - Single `TaskMetadataChangedCommand` instead of 4 separate commands

### Key Technical Decisions (from Research)
1. **Use `DateOnly` instead of `DateTime`** - Avoids timezone issues for due dates
2. **Use `[GeneratedRegex]` for AOT compatibility** - Source-generated regex patterns
3. **Apply epoch counter pattern** from learnings for TaskerTray race conditions
4. **Keep TodoTask mutations simple** - One-liner `with` expressions, validation in helpers

---

## Overview

Extend cli-tasker with task metadata in two phases:
- **Phase 1 (this plan):** Due dates and priority levels with CLI commands
- **Phase 2 (if requested):** Tags and inline parsing

Metadata is displayed consistently across CLI, TUI, and TaskerTray interfaces.

## Problem Statement / Motivation

Currently tasks only have a description and checked status. Users need to:
- Set deadlines and see overdue tasks
- Prioritize tasks visually

This feature transforms cli-tasker from a simple checklist into a proper task management system while maintaining its lightweight, fast-add ergonomics.

## Proposed Solution

**Phase 1: Commands-only approach** (simpler, ship first)

```bash
# Add task, then set metadata via commands
tasker add "Fix critical bug"
tasker due abc friday
tasker priority abc high

# Filter and view
tasker list --overdue
tasker list --priority high
```

**Phase 2 (optional): Inline parsing**

```bash
# Quick add with inline metadata (if users request)
tasker add "Fix critical bug !!! @friday"
```

**Data model changes** (backwards-compatible):
```csharp
record TodoTask(
    string Id,
    string Description,
    bool IsChecked,
    DateTime CreatedAt,
    string ListName,
    // New fields with defaults:
    DateOnly? DueDate = null,
    Priority? Priority = null
    // Tags deferred to Phase 2
)
```

## Technical Approach

### Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                        Interfaces                           │
├─────────────────┬─────────────────┬─────────────────────────┤
│      CLI        │       TUI       │      TaskerTray         │
│  (commands)     │   (keyboard)    │      (Avalonia)         │
└────────┬────────┴────────┬────────┴────────────┬────────────┘
         │                 │                      │
         ▼                 ▼                      ▼
┌─────────────────────────────────────────────────────────────┐
│                     TaskerCore                              │
├─────────────────────────────────────────────────────────────┤
│  DateParser.cs      │  TodoTask.cs  │  TodoTaskList.cs      │
│  - Parse()          │  - Priority   │  - SetTaskDueDate()   │
│  - TryParseRelative │  - DueDate    │  - SetTaskPriority()  │
│  - TryParseDayOfWeek│  - IsOverdue  │  - GetSortedTasks()   │
├─────────────────────┴───────────────┴───────────────────────┤
│                     Undo Commands                           │
│            TaskMetadataChangedCommand (unified)             │
└─────────────────────────────────────────────────────────────┘
```

### Research Insights: Architecture

**Best Practices:**
- Parser should ONLY parse and extract metadata, NOT create or modify tasks (Single Responsibility)
- Keep formatting logic in presentation layers (CLI/TUI/Tray), NOT in TaskerCore
- Provide raw data methods in TodoTask: `IsOverdue`, `IsDueToday`, `IsDueSoon`

**Performance Considerations:**
- O(n log n) sorting is acceptable for 10,000+ tasks (<20ms on modern hardware)
- Parse metadata ONLY at creation time, store in structured fields
- Use `[GeneratedRegex]` for compile-time validation and AOT compatibility

**Implementation Details:**
```csharp
// Good: Raw data in core, formatting in presentation
public bool IsOverdue => DueDate.HasValue && DueDate.Value < DateOnly.FromDateTime(DateTime.Today);
public bool IsDueToday => DueDate.HasValue && DueDate.Value == DateOnly.FromDateTime(DateTime.Today);

// CLI formats this way:
var dueDateText = task.IsOverdue ? "[red]OVERDUE[/]" : task.DueDate?.ToString("MMM d");

// Tray formats differently:
public Brush DueDateForeground => _task.IsOverdue ? Brushes.Red : Brushes.Gray;
```

---

### Implementation Phases

#### Phase 1: Core Data Model

**Goal**: Extend TodoTask with metadata fields, ensure backwards compatibility.

**Files to modify**:
- `src/TaskerCore/Models/TodoTask.cs` - Add fields and mutation methods
- `src/TaskerCore/Models/Priority.cs` (new) - Priority enum

**Tasks**:
- [ ] Add `Priority` enum with explicit values and JSON string converter
- [ ] Add `DateOnly? DueDate` field with default `null` (not DateTime - avoids timezone issues)
- [ ] Add `Priority? Priority` field with default `null`
- [ ] Add simple mutation methods (one-liners with `this with {}`)
- [ ] Verify JSON deserialization handles missing fields (test with old format)

**Deferred to Phase 2**: Tags field and tag mutation methods

### Research Insights: Data Model

**Simplified implementation (Phase 1 - no tags):**
```csharp
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Priority
{
    High = 1,    // Explicit values prevent reorder issues
    Medium = 2,
    Low = 3
}

public sealed record TodoTask(
    string Id,
    string Description,
    bool IsChecked,
    DateTime CreatedAt,
    string ListName,
    DateOnly? DueDate = null,      // DateOnly avoids timezone issues
    Priority? Priority = null
    // Tags deferred to Phase 2
)
{
    // Existing methods...

    // Computed properties for display logic
    public bool IsOverdue => DueDate.HasValue && DueDate.Value < DateOnly.FromDateTime(DateTime.Today);
    public bool IsDueToday => DueDate.HasValue && DueDate.Value == DateOnly.FromDateTime(DateTime.Today);
    public bool IsDueSoon => DueDate.HasValue && DueDate.Value <= DateOnly.FromDateTime(DateTime.Today.AddDays(3));

    // Simple one-liner mutations (validation in helpers if needed)
    public TodoTask SetDueDate(DateOnly date) => this with { DueDate = date };
    public TodoTask ClearDueDate() => this with { DueDate = null };
    public TodoTask SetPriority(Priority priority) => this with { Priority = priority };
    public TodoTask ClearPriority() => this with { Priority = null };
}
```

**Why DateOnly instead of DateTime:**
- Due dates are date-only concepts (no time component needed)
- Avoids timezone bugs: `DateTime.Today` involves timezone calculations
- Simpler comparisons: `dueDate < DateOnly.FromDateTime(DateTime.Today)`
- JSON serializes cleanly as `"2026-02-15"` without time component

**Acceptance criteria**:
- [ ] Old JSON files load without error (null defaults)
- [ ] New tasks serialize with metadata fields
- [ ] Immutable methods return new instances
- [ ] Priority serializes as string in JSON (not integer)

---

#### Phase 2: Metadata Parser (OPTIONAL - implement if users request)

**Goal**: Parse inline metadata from task descriptions.

**Note**: This phase is optional. Commands-only approach from Phase 3 may be sufficient.
Consider skipping and revisiting after user feedback.

**Files to create**:
- `src/TaskerCore/Parsing/MetadataParser.cs`
- `src/TaskerCore/Parsing/DateParser.cs`

**Parsing rules**:

| Pattern | Meaning | Example |
|---------|---------|---------|
| `!!!` or `p1` | High priority | `"Fix bug !!!"` |
| `!!` or `p2` | Medium priority | `"Review PR !!"` |
| `!` or `p3` | Low priority | `"Update docs !"` |
| `#word` | Tag | `"Task #work #urgent"` |
| `@dateexpr` | Due date | `"Meeting @tomorrow"` |

**Tag rules**:
- Must start with `#` followed by letter (not digit - avoids GitHub issue refs)
- Valid characters: `[a-zA-Z][a-zA-Z0-9_-]*`
- Max length: 30 characters
- Stored lowercase, deduplicated

**Date expressions** (custom parser, no external dependencies):
| Expression | Meaning |
|------------|---------|
| `@today` | Today |
| `@tomorrow` | Tomorrow |
| `@monday`..`@sunday` | Next occurrence of weekday |
| `@jan15`, `@feb3` | Next occurrence of date |
| `@+3d`, `@+1w` | Relative (days/weeks) |

### Research Insights: Parser

**Best Practices (from Best Practices Researcher):**

Use `[GeneratedRegex]` for AOT compatibility and performance:

```csharp
public static partial class MetadataParser
{
    // Priority: !!! or !! or ! (standalone, not adjacent to letters)
    [GeneratedRegex(@"(?<![!\w])(!{1,3})(?![!\w])")]
    private static partial Regex PriorityBangRegex();

    // Priority: p1, p2, p3 (standalone)
    [GeneratedRegex(@"(?<=\s|^)p([1-3])(?=\s|$)", RegexOptions.IgnoreCase)]
    private static partial Regex PriorityPnRegex();

    // Tags: #word (must start with letter, not #123)
    [GeneratedRegex(@"(?<=\s|^)#([a-zA-Z][a-zA-Z0-9_-]{0,29})(?=\s|$)")]
    private static partial Regex TagRegex();

    // Date: @word (not user@email - must have space before)
    [GeneratedRegex(@"(?<=\s|^)@([a-zA-Z0-9+]+)(?=\s|$)")]
    private static partial Regex DateRegex();

    public record ParseResult(
        string Description,
        DateOnly? DueDate,
        string[] Tags,
        Priority? Priority
    );

    public static ParseResult Parse(string input)
    {
        var description = input;
        Priority? priority = null;
        DateOnly? dueDate = null;
        var tags = new List<string>();

        // Extract priority (rightmost wins)
        var bangMatch = PriorityBangRegex().Matches(description).LastOrDefault();
        if (bangMatch != null)
        {
            priority = bangMatch.Groups[1].Value.Length switch
            {
                3 => Priority.High,
                2 => Priority.Medium,
                1 => Priority.Low,
                _ => null
            };
            description = PriorityBangRegex().Replace(description, "", 1);
        }
        else
        {
            var pnMatch = PriorityPnRegex().Matches(description).LastOrDefault();
            if (pnMatch != null)
            {
                priority = int.Parse(pnMatch.Groups[1].Value) switch
                {
                    1 => Priority.High,
                    2 => Priority.Medium,
                    3 => Priority.Low,
                    _ => null
                };
                description = PriorityPnRegex().Replace(description, "", 1);
            }
        }

        // Extract date (rightmost wins)
        var dateMatch = DateRegex().Matches(description).LastOrDefault();
        if (dateMatch != null)
        {
            dueDate = DateParser.Parse(dateMatch.Groups[1].Value);
            description = DateRegex().Replace(description, "", 1);
        }

        // Extract all tags
        foreach (Match match in TagRegex().Matches(description))
        {
            tags.Add(match.Groups[1].Value.ToLowerInvariant());
        }
        description = TagRegex().Replace(description, "");

        // Clean up whitespace
        description = Regex.Replace(description.Trim(), @"\s+", " ");

        if (string.IsNullOrWhiteSpace(description))
            throw new InvalidTaskDescriptionException("Task description cannot be empty after metadata extraction");

        return new ParseResult(description, dueDate, tags.Distinct().ToArray(), priority);
    }
}
```

**Date Parser (simple, no external dependencies):**

```csharp
public static class DateParser
{
    public static DateOnly? Parse(string input)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var normalized = input.Trim().ToLowerInvariant();

        return normalized switch
        {
            "today" => today,
            "tomorrow" => today.AddDays(1),
            "yesterday" => today.AddDays(-1),
            _ => TryParseRelative(normalized, today)
                 ?? TryParseDayOfWeek(normalized, today)
                 ?? TryParseMonthDay(normalized, today)
                 ?? TryParseStandard(input)
        };
    }

    private static DateOnly? TryParseRelative(string input, DateOnly today)
    {
        // +3d, +1w, +2m
        var match = Regex.Match(input, @"^\+(\d+)([dwm])$");
        if (!match.Success) return null;

        var count = int.Parse(match.Groups[1].Value);
        return match.Groups[2].Value switch
        {
            "d" => today.AddDays(count),
            "w" => today.AddDays(count * 7),
            "m" => today.AddMonths(count),
            _ => null
        };
    }

    private static DateOnly? TryParseDayOfWeek(string input, DateOnly today)
    {
        var dayMap = new Dictionary<string, DayOfWeek>
        {
            ["mon"] = DayOfWeek.Monday, ["monday"] = DayOfWeek.Monday,
            ["tue"] = DayOfWeek.Tuesday, ["tuesday"] = DayOfWeek.Tuesday,
            ["wed"] = DayOfWeek.Wednesday, ["wednesday"] = DayOfWeek.Wednesday,
            ["thu"] = DayOfWeek.Thursday, ["thursday"] = DayOfWeek.Thursday,
            ["fri"] = DayOfWeek.Friday, ["friday"] = DayOfWeek.Friday,
            ["sat"] = DayOfWeek.Saturday, ["saturday"] = DayOfWeek.Saturday,
            ["sun"] = DayOfWeek.Sunday, ["sunday"] = DayOfWeek.Sunday
        };

        if (!dayMap.TryGetValue(input, out var targetDay))
            return null;

        var daysUntil = ((int)targetDay - (int)today.DayOfWeek + 7) % 7;
        if (daysUntil == 0) daysUntil = 7; // Next week if today
        return today.AddDays(daysUntil);
    }

    private static DateOnly? TryParseMonthDay(string input, DateOnly today)
    {
        // jan15, feb3, etc.
        var match = Regex.Match(input, @"^(jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec)(\d{1,2})$");
        if (!match.Success) return null;

        var monthMap = new Dictionary<string, int>
        {
            ["jan"] = 1, ["feb"] = 2, ["mar"] = 3, ["apr"] = 4,
            ["may"] = 5, ["jun"] = 6, ["jul"] = 7, ["aug"] = 8,
            ["sep"] = 9, ["oct"] = 10, ["nov"] = 11, ["dec"] = 12
        };

        var month = monthMap[match.Groups[1].Value];
        var day = int.Parse(match.Groups[2].Value);

        try
        {
            var result = new DateOnly(today.Year, month, day);
            if (result < today) result = result.AddYears(1);
            return result;
        }
        catch { return null; }
    }

    private static DateOnly? TryParseStandard(string input)
    {
        // ISO format: 2026-02-15
        if (DateOnly.TryParse(input, out var date))
            return date;
        return null;
    }
}
```

**Edge Cases:**
- `user@email.com` - NOT parsed (@ must have space before it)
- `issue #123` - NOT parsed as tag (digit after #)
- `Don't!` - NOT parsed as priority (! adjacent to letter)

**Acceptance criteria**:
- [ ] `"Fix bug !!! #work @friday"` → description="Fix bug", priority=High, tags=["work"], due=next Friday
- [ ] `"Email user@company.com"` → description="Email user@company.com", no metadata
- [ ] `"Fix #123 on GitHub"` → description="Fix #123 on GitHub", no tags
- [ ] `"!!! #work @tomorrow"` → error (empty description)

---

#### Phase 3: CLI Commands

**Goal**: Add commands to edit metadata on existing tasks.

**Files to create**:
- `AppCommands/DueCommand.cs`
- `AppCommands/PriorityCommand.cs`

**Files to modify**:
- `AppCommands/ListCommand.cs` - Add filter options
- `Program.cs` - Register new commands

**Deferred to Phase 2**: TagCommand.cs, inline parser integration in AddCommand/RenameCommand

### Research Insights: CLI Commands

**System.CommandLine Patterns (from Context7):**

```csharp
// DueCommand.cs
public static Command CreateDueCommand()
{
    var dueCommand = new Command("due", "Set task due date");

    var taskIdArg = new Argument<string>("id") { Description = "Task ID" };
    var dateArg = new Argument<string>("date") { Description = "Due date (tomorrow, friday, jan15, clear)" };

    dueCommand.Arguments.Add(taskIdArg);
    dueCommand.Arguments.Add(dateArg);

    dueCommand.SetAction(CommandHelper.WithErrorHandling(parseResult =>
    {
        var id = parseResult.GetValue(taskIdArg);
        var dateStr = parseResult.GetValue(dateArg);

        DateOnly? dueDate = dateStr?.ToLower() == "clear"
            ? null
            : DateParser.Parse(dateStr!);

        var taskList = new TodoTaskList();
        var result = taskList.SetTaskDueDate(id!, dueDate);
        Output.Result(result);
    }));

    return dueCommand;
}

// PriorityCommand.cs with restricted values
public static Command CreatePriorityCommand()
{
    var priorityCommand = new Command("priority", "Set task priority");

    var taskIdArg = new Argument<string>("id") { Description = "Task ID" };
    var levelArg = new Argument<string>("level") { Description = "Priority level" };
    levelArg.AcceptOnlyFromAmong("high", "medium", "low", "clear", "1", "2", "3", "p1", "p2", "p3");

    priorityCommand.Arguments.Add(taskIdArg);
    priorityCommand.Arguments.Add(levelArg);

    priorityCommand.SetAction(CommandHelper.WithErrorHandling(parseResult =>
    {
        var id = parseResult.GetValue(taskIdArg);
        var level = parseResult.GetValue(levelArg);

        Priority? priority = level?.ToLower() switch
        {
            "high" or "1" or "p1" => Priority.High,
            "medium" or "2" or "p2" => Priority.Medium,
            "low" or "3" or "p3" => Priority.Low,
            "clear" => null,
            _ => null
        };

        var taskList = new TodoTaskList();
        var result = taskList.SetTaskPriority(id!, priority);
        Output.Result(result);
    }));

    return priorityCommand;
}
```

**Filter options for `list` command:**

```csharp
// In ListCommand.cs
var priorityOption = new Option<string?>("--priority", "-p") { Description = "Filter by priority" };
priorityOption.AcceptOnlyFromAmong("high", "medium", "low", "1", "2", "3");
var overdueOption = new Option<bool>("--overdue") { Description = "Show only overdue tasks" };
var dueOption = new Option<bool>("--due") { Description = "Show only tasks with due dates" };
// Tag filtering deferred to Phase 2
```

**Acceptance criteria**:
- [ ] `tasker due abc tomorrow` sets due date
- [ ] `tasker due abc clear` removes due date
- [ ] `tasker priority abc high` sets priority
- [ ] `tasker priority abc clear` removes priority
- [ ] `tasker list --overdue` shows only overdue tasks
- [ ] `tasker list --priority high` shows only high priority tasks

---

#### Phase 2b: Data Layer & Sorting

**Goal**: Add methods to TodoTaskList for metadata operations and update sorting.

**Note**: This must be implemented BEFORE CLI Commands (Phase 3).

**Files to modify**:
- `src/TaskerCore/Data/TodoTaskList.cs`

### Research Insights: Data Layer

**CRITICAL BUG FIX (from Kieran review):**

The original code had a circular call:
```csharp
// BUG: GetFilteredTasks() called GetSortedTasks() which called GetFilteredTasks()
public List<TodoTask> GetFilteredTasks(...) => GetSortedTasks().Where(...)
public List<TodoTask> GetSortedTasks() => GetFilteredTasks().OrderBy(...)
```

**Corrected implementation** - separate raw data access from filtering:

```csharp
// Step 1: Get raw tasks (no filtering, no sorting)
private IEnumerable<TodoTask> GetAllTasksRaw() => TaskLists.SelectMany(l => l.Tasks);

// Step 2: Apply sorting to raw tasks
public List<TodoTask> GetSortedTasks()
{
    var today = DateOnly.FromDateTime(DateTime.Today);

    return GetAllTasksRaw()
        .OrderBy(t => t.IsChecked)                              // Unchecked first
        .ThenBy(t => t.Priority.HasValue ? (int)t.Priority : 99) // High=1 < Med=2 < Low=3 < None=99
        .ThenBy(t => GetDueDateSortOrder(t.DueDate, today))     // Overdue < Today < Soon < Future < None
        .ThenByDescending(t => t.CreatedAt)                     // Newest first
        .ToList();
}

// Step 3: Apply filters to sorted tasks
public List<TodoTask> GetFilteredTasks(Priority? priority = null, bool? overdueOnly = null)
{
    IEnumerable<TodoTask> result = GetSortedTasks();
    var today = DateOnly.FromDateTime(DateTime.Today);

    if (priority != null)
        result = result.Where(t => t.Priority == priority);

    if (overdueOnly == true)
        result = result.Where(t => t.DueDate.HasValue && t.DueDate < today);

    return result.ToList();
}

private static int GetDueDateSortOrder(DateOnly? dueDate, DateOnly today)
{
    if (!dueDate.HasValue) return 99;
    var days = dueDate.Value.DayNumber - today.DayNumber;
    return days < 0 ? 0 : days; // Overdue = 0, today = 0, tomorrow = 1, etc.
}
```

**Acceptance criteria**:
- [ ] High priority tasks appear before low priority
- [ ] Overdue tasks appear before tasks due today
- [ ] Tasks without priority appear after low priority tasks
- [ ] Tasks without due date appear after tasks with due dates (within same priority)
- [ ] No circular method calls between GetFilteredTasks and GetSortedTasks

---

#### Phase 4: Undo Support

**Goal**: Make metadata operations undoable with a single unified command.

**Files to create**:
- `src/TaskerCore/Undo/Commands/TaskMetadataChangedCommand.cs` (single command for all metadata changes)

**Files to modify**:
- `src/TaskerCore/Undo/IUndoableCommand.cs` - Add JsonDerivedType attribute

### Research Insights: Undo System

**Simplified approach (from DHH & Simplicity reviews):**

Instead of 4 separate commands, use ONE generic metadata change command:

```csharp
// In IUndoableCommand.cs - only ONE new attribute needed
[JsonDerivedType(typeof(TaskMetadataChangedCommand), "metadata")]
public interface IUndoableCommand

// Single unified command handles all metadata changes
public sealed record TaskMetadataChangedCommand : IUndoableCommand
{
    public required string TaskId { get; init; }
    public DateOnly? OldDueDate { get; init; }
    public DateOnly? NewDueDate { get; init; }
    public Priority? OldPriority { get; init; }
    public Priority? NewPriority { get; init; }
    public DateTime ExecutedAt { get; init; } = DateTime.Now;

    public string Description
    {
        get
        {
            var changes = new List<string>();
            if (OldDueDate != NewDueDate)
                changes.Add(NewDueDate.HasValue ? $"due → {NewDueDate:MMM d}" : "due → cleared");
            if (OldPriority != NewPriority)
                changes.Add(NewPriority.HasValue ? $"priority → {NewPriority}" : "priority → cleared");
            return $"Changed {TaskId}: {string.Join(", ", changes)}";
        }
    }

    public void Execute()
    {
        var taskList = new TodoTaskList();
        if (OldDueDate != NewDueDate)
            taskList.SetTaskDueDate(TaskId, NewDueDate, recordUndo: false);
        if (OldPriority != NewPriority)
            taskList.SetTaskPriority(TaskId, NewPriority, recordUndo: false);
    }

    public void Undo()
    {
        var taskList = new TodoTaskList();
        if (OldDueDate != NewDueDate)
            taskList.SetTaskDueDate(TaskId, OldDueDate, recordUndo: false);
        if (OldPriority != NewPriority)
            taskList.SetTaskPriority(TaskId, OldPriority, recordUndo: false);
    }
}
```

**Acceptance criteria**:
- [ ] `tasker due abc tomorrow` → `tasker undo` restores previous due date
- [ ] `tasker priority abc high` → `tasker undo` restores previous priority
- [ ] Undo history shows descriptive messages
- [ ] Single new command type registered in `IUndoableCommand`

---

#### Phase 5: CLI Display

**Goal**: Update CLI output to show metadata visually.

**Files to modify**:
- `AppCommands/ListCommand.cs`
- `Output.cs`

### Research Insights: CLI Display

**Spectre.Console patterns (from Context7):**

```csharp
// Output.cs additions
public static string FormatPriority(Priority? priority) => priority switch
{
    Priority.High => "[red]![/]",
    Priority.Medium => "[yellow]·[/]",
    Priority.Low => "[blue]·[/]",
    _ => "[dim]·[/]"
};

public static string FormatDueDate(DateOnly? dueDate)
{
    if (!dueDate.HasValue) return "";
    var today = DateOnly.FromDateTime(DateTime.Today);
    var diff = dueDate.Value.DayNumber - today.DayNumber;

    return diff switch
    {
        < 0 => $"[red]OVERDUE ({-diff}d)[/]",
        0 => "[yellow]Due: Today[/]",
        1 => "[dim]Due: Tomorrow[/]",
        < 7 => $"[dim]Due: {dueDate.Value:dddd}[/]",
        _ => $"[dim]Due: {dueDate.Value:MMM d}[/]"
    };
}

// FormatTags deferred to Phase 2
```

**Display format:**
```
tasks
(abc) [!] [ ] Fix critical bug                   OVERDUE (3d)
(def) [·] [ ] Review PR                          Due: Today
(ghi) [·] [x] Update docs
(jkl) [·] [ ] Buy groceries
```

**Acceptance criteria**:
- [ ] Priority indicator shows with correct color
- [ ] Overdue tasks show red "OVERDUE" label with days count
- [ ] Today's tasks show yellow "Due: Today"
- [ ] Output is aligned and readable

---

#### Phase 6: TUI Display & Interaction

**Goal**: Show metadata in TUI and add keyboard shortcuts.

**Files to modify**:
- `Tui/TuiRenderer.cs`
- `Tui/TuiKeyHandler.cs`
- `Tui/TuiState.cs`

### Research Insights: TUI

**Simplification (from Code Simplicity Reviewer):**

Instead of 3 new TUI modes, use ONE generic text input mode:

```csharp
// Simplified: Reuse existing InputAdd pattern
public enum TuiMode
{
    Normal,
    Search,
    MultiSelect,
    InputAdd,
    InputRename,
    InputMove,      // Existing
    InputMetadata   // NEW: Generic metadata input (date or tag)
}

// Track what we're editing
public enum MetadataInputType { DueDate, Tag }
```

**Keyboard shortcuts:**
| Key | Action |
|-----|--------|
| `1` | Set priority high |
| `2` | Set priority medium |
| `3` | Set priority low |
| `0` | Clear priority |
| `d` | Set due date (enter input mode) |
| `D` | Clear due date |

**Deferred to Phase 2:** Tag shortcuts (`t`, `T`)

**Acceptance criteria**:
- [ ] Tasks display with priority and due date
- [ ] Pressing `1` on task sets high priority
- [ ] Pressing `d` enters date input mode
- [ ] All operations are undoable with `z`

---

#### Phase 7: TaskerTray Display & Interaction

**Goal**: Show metadata in TaskerTray popup.

**Files to modify**:
- `src/TaskerTray/ViewModels/TodoTaskViewModel.cs`
- `src/TaskerTray/Views/TaskListPopup.axaml.cs`

### Research Insights: TaskerTray

**Race Condition Prevention (from Learnings):**

Apply the `_showCount` epoch counter pattern from the collapsible-lists fix:

```csharp
// In TaskListPopup.axaml.cs
private int _showCount;

// When opening popup
protected override void OnOpened(EventArgs e)
{
    _showCount++;
    // ... rest of open logic
}

// In any metadata editor event handlers
private void CreateDueDatePicker(TodoTaskViewModel task)
{
    var capturedShowCount = _showCount;

    datePicker.LostFocus += (_, _) =>
    {
        if (capturedShowCount != _showCount)
            return; // Ignore stale event

        // Save due date change
    };
}
```

**ViewModel additions:**
```csharp
public Priority? Priority => _task.Priority;
public bool IsOverdue => _task.DueDate.HasValue && _task.DueDate.Value < DateOnly.FromDateTime(DateTime.Today);
public string DueDateDisplay => _task.DueDate?.ToString("MMM d") ?? "";
// TagsDisplay deferred to Phase 2

public IBrush PriorityColor => Priority switch
{
    Models.Priority.High => Brushes.Red,
    Models.Priority.Medium => Brushes.Orange,
    Models.Priority.Low => Brushes.DodgerBlue,
    _ => Brushes.Transparent
};

public IBrush DueDateColor => IsOverdue ? Brushes.Red : Brushes.Gray;
```

**Acceptance criteria**:
- [ ] Priority shown as colored dot
- [ ] Overdue indicator visible
- [ ] Can set priority via context menu
- [ ] Can set due date via context menu
- [ ] Race conditions prevented with epoch counter

---

## Alternative Approaches Considered

1. **Inline parsing only** - Simpler but editing metadata requires rewriting full description
2. **Commands only** - **Chosen for Phase 1** - More verbose but simpler implementation
3. **Metadata in separate file** - Unnecessary complexity, breaks single-file simplicity

### Phase 2 (if users request)

- Tags with `#hashtag` syntax
- Inline parsing (`tasker add "task !!! @friday"`)
- Tag filtering (`--tag work`)

## Acceptance Criteria

### Functional Requirements (Phase 1)
- [ ] `tasker due <id> <date>` sets due date
- [ ] `tasker priority <id> <level>` sets priority
- [ ] Tasks sorted by priority then due date
- [ ] Overdue tasks visually highlighted in all interfaces
- [ ] Can filter by priority and overdue status
- [ ] Metadata changes are undoable
- [ ] All three interfaces display metadata consistently

### Deferred to Phase 2
- [ ] Can add task with inline date, tags, priority
- [ ] Tag filtering with `--tag`
- [ ] Tags are case-insensitive

### Non-Functional Requirements
- [ ] Old task files load without migration errors
- [ ] No external dependencies for date parsing

### Quality Gates
- [ ] Manual testing across CLI, TUI, TaskerTray
- [ ] Undo/redo works for due date and priority changes
- [ ] JSON round-trip preserves all metadata
- [ ] Single new undo command type registered in IUndoableCommand

## Dependencies & Prerequisites

- No external dependencies required
- Builds on existing undo system
- Builds on existing command patterns

## Risk Analysis & Mitigation

| Risk | Impact | Mitigation |
|------|--------|------------|
| JSON schema change | Low | Nullable fields with defaults ensure backwards compatibility |
| Complex sorting | Low | Clear specification with defined null handling |
| TUI/Tray complexity | Medium | Phase implementation, test each interface independently |
| DateTime timezone bugs | High | Use DateOnly instead of DateTime for due dates |
| Undo deserialization | Low | Single new command type - easy to verify |

## Success Metrics

- Tasks with metadata created in <2 seconds (same as current)
- Overdue tasks immediately visible without filtering
- Zero data loss on upgrade from old format

## References & Research

### Internal References
- TodoTask model: `src/TaskerCore/Models/TodoTask.cs`
- Command pattern: `AppCommands/MoveCommand.cs`
- Undo system: `src/TaskerCore/Undo/Commands/RenameTaskCommand.cs`
- Backwards-compatible properties: `docs/solutions/feature-implementations/collapsible-lists-tray.md`
- Race condition fix: `docs/solutions/ui-bugs/list-duplication-on-inline-add.md`

### External References
- System.CommandLine patterns: https://github.com/dotnet/command-line-api
- Spectre.Console markup: https://spectreconsole.net/markup
- Todoist inline syntax: https://todoist.com/help/articles/use-task-quick-add

### Brainstorm
- `docs/brainstorms/2026-02-04-task-metadata-brainstorm.md`
