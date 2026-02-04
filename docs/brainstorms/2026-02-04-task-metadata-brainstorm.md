# Task Metadata Brainstorm

**Date**: 2026-02-04
**Status**: Ready for planning

## What We're Building

Extend the TodoTask model with three metadata fields:
1. **Due dates** - Visual deadlines with overdue highlighting
2. **Tags** - Cross-list categorization via inline hashtags
3. **Priority** - Three levels (high/medium/low) with visual indicators

All three use inline syntax for quick entry, with additional commands for editing.

## Why This Approach

### Hybrid: Inline Parsing + Commands

**Inline parsing** on task creation extracts metadata from natural text:
```
tasker add "Fix critical bug !!! #backend @tomorrow"
```
Parses to:
- Description: "Fix critical bug"
- Priority: High (!!!)
- Tags: ["backend"]
- Due: 2026-02-05

**Explicit commands** allow editing metadata without rewriting the task:
```
tasker due abc "next friday"
tasker tag abc add #urgent
tasker tag abc remove #backend
tasker priority abc medium
```

This balances quick-add ergonomics with fine-grained control.

### Alternatives Considered

1. **Inline Only** - Simpler but editing requires full rename
2. **Separate Fields Only** - More verbose, breaks quick-add flow

## Key Decisions

### Due Dates
- **Input**: Natural language ("tomorrow", "friday", "jan 15", "in 3 days")
- **Display**: Visual indicator with overdue highlighting (red for overdue, yellow for today/soon)
- **No reminders**: Just visual - no notification system needed

### Tags
- **Syntax**: Inline hashtags (`#work`, `#urgent`, `#home`)
- **Storage**: Extracted to array field, preserved in display
- **Filtering**: `tasker list -t urgent` or `tasker list --tag work`
- **Cross-list**: Tags work across all lists

### Priority
- **Levels**: High, Medium, Low (plus None as default)
- **Syntax**: `!!!` or `p1` (high), `!!` or `p2` (medium), `!` or `p3` (low)
- **Display**: Color-coded indicator (red/yellow/blue or similar)
- **Sorting**: Priority first, then due date, then creation date

### Sorting Order
Tasks display in this order (within unchecked/checked groups):
1. High priority → Medium → Low → None
2. Within each priority: Overdue → Due today → Due soon → No date → Future
3. Within each date group: Newest first (current behavior)

### Data Model Changes
```csharp
record TodoTask(
    string Id,
    string Description,
    bool IsChecked,
    DateTime CreatedAt,
    string ListName,
    // New fields:
    DateTime? DueDate,
    string[] Tags,
    Priority? Priority  // enum: High, Medium, Low
)
```

### New Commands
| Command | Description |
|---------|-------------|
| `tasker due <id> "<date>"` | Set or update due date |
| `tasker due <id> clear` | Remove due date |
| `tasker tag <id> add <tag>` | Add tag to task |
| `tasker tag <id> remove <tag>` | Remove tag from task |
| `tasker priority <id> <level>` | Set priority (high/medium/low/none) |

### Filter Options
| Option | Description |
|--------|-------------|
| `--tag <name>` / `-t` | Filter by tag |
| `--priority <level>` / `-p` | Filter by priority |
| `--due` | Show only tasks with due dates |
| `--overdue` | Show only overdue tasks |

## Open Questions

1. **Date parsing library**: Use existing .NET library (Chronic.NET, NodaTime) or roll simple patterns?
2. **Tag character rules**: Allow only alphanumeric, or support dashes/underscores?
3. **Priority in TUI/Tray**: How to set priority? Keyboard shortcut? Context menu?
4. **Undo support**: Should metadata changes be undoable? (Probably yes, new command types)

## UI/UX Mockups

### CLI List Output
```
tasks
(abc) [!] [ ] Fix critical bug          #backend    [OVERDUE: Jan 30]
(def) [ ] [ ] Review PR                 #work       [Due: Today]
(ghi) [ ] [ ] Buy groceries             #home
```

### TUI Display
- Priority indicator before checkbox: `[!]` red, `[!]` yellow, `[!]` blue
- Due date right-aligned or below task
- Tags displayed inline or as small badges

### TaskerTray
- Priority dot or border color
- Due date below task text in smaller font
- Tag chips below task

## Implementation Phases (Rough)

1. **Core**: Extend TodoTask model, JSON migration
2. **Parsing**: Natural language date parser, hashtag/priority extraction
3. **Commands**: due, tag, priority commands
4. **Display**: CLI formatting with metadata
5. **TUI**: Keyboard shortcuts and display
6. **Tray**: UI elements for metadata
7. **Undo**: New undoable command types

## Success Criteria

- [ ] Can add task with inline date, tags, priority
- [ ] Metadata extracted correctly and stored
- [ ] Tasks sorted by priority then due date
- [ ] Overdue tasks visually highlighted in all interfaces
- [ ] Can filter by tag and priority
- [ ] Can edit metadata via commands
- [ ] Metadata changes are undoable
- [ ] All three interfaces display metadata consistently
