---
title: "feat: Add Interactive TUI Mode"
type: feat
date: 2026-01-30
---

# Add Interactive TUI Mode

## Overview

Transform cli-tasker into a more interactive application by adding a TUI (Text User Interface) mode that launches when running `tasker` with no arguments. This provides keyboard-driven task management with real-time feedback, eliminating the need to remember command syntax for common operations.

## Problem Statement / Motivation

Currently, cli-tasker requires users to type full commands for every operation:
- `tasker check abc` to complete a task
- `tasker rename abc "New description"` to rename
- `tasker list` to see tasks, then another command to act

This creates friction for power users who want to quickly manage tasks. A TUI mode enables:
- Visual task selection with keyboard navigation
- Single-key actions (space to toggle, x to delete)
- Immediate feedback without re-running commands
- Bulk operations through multi-select

## Proposed Solution

Implement an interactive TUI using Spectre.Console (already in project) with a custom main loop pattern. When users run `tasker` with no arguments, they enter TUI mode instead of seeing help text.

### Key Design Decisions

1. **Entry Point**: `tasker` (no args) launches TUI; existing commands unchanged
2. **Library**: Spectre.Console with custom main loop (not Terminal.Gui)
3. **Architecture**: Mode-based state machine with clean separation

## Technical Approach

### Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                        Program.cs                           │
│  (Detects no args → launches TuiApp instead of commands)   │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                         TuiApp.cs                           │
│  Main loop: Render() → HandleInput() → repeat until quit   │
└─────────────────────────────────────────────────────────────┘
                              │
              ┌───────────────┼───────────────┐
              ▼               ▼               ▼
┌─────────────────┐ ┌─────────────────┐ ┌─────────────────┐
│  TuiState.cs    │ │  TuiRenderer.cs │ │ TuiKeyHandler.cs│
│  State record   │ │  Display logic  │ │  Key bindings   │
│  Mode enum      │ │  Spectre.Console│ │  Mode switching │
└─────────────────┘ └─────────────────┘ └─────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│              Existing Data Layer (unchanged)                │
│         TodoTaskList, TodoTask, ListManager                 │
└─────────────────────────────────────────────────────────────┘
```

### State Machine

```
                         ┌──────────────┐
                    ┌────│    NORMAL    │────┐
                    │    └──────────────┘    │
                    │           │            │
         ┌──────────┼───────────┼────────────┼──────────┐
         ▼          ▼           ▼            ▼          ▼
    ┌────────┐ ┌────────┐ ┌──────────┐ ┌────────┐ ┌─────────┐
    │  EDIT  │ │ SEARCH │ │LIST_PICK │ │  MOVE  │ │ MULTI   │
    │ (r, a) │ │  (/)   │ │   (l)    │ │  (m)   │ │SELECT(v)│
    └────────┘ └────────┘ └──────────┘ └────────┘ └─────────┘
         │          │           │            │          │
         └──────────┴───────────┴────────────┴──────────┘
                         │ Esc/Enter │
                         ▼
                    Back to NORMAL
```

### Implementation Phases

#### Phase 1: Foundation (Core TUI Loop)

**Files to create:**
- `Tui/TuiApp.cs` - Main application loop
- `Tui/TuiState.cs` - State management
- `Tui/TuiRenderer.cs` - Screen rendering
- `Tui/TuiKeyHandler.cs` - Input handling

**Deliverables:**
- Basic main loop with clear/render cycle
- Task list display with cursor selection
- Navigation (j/k, up/down arrows)
- Exit with q/Escape
- Status bar with key hints

**TuiState.cs**
```csharp
namespace cli_tasker.Tui;

public enum TuiMode
{
    Normal,
    Edit,
    Search,
    ListPicker,
    MovePicker,
    MultiSelect
}

public record TuiState
{
    public TuiMode Mode { get; init; } = TuiMode.Normal;
    public int CursorIndex { get; init; } = 0;
    public string? CurrentList { get; init; } = null; // null = all lists
    public string? SearchQuery { get; init; } = null;
    public HashSet<string> SelectedTaskIds { get; init; } = new();
    public string? EditBuffer { get; init; } = null;
    public string? StatusMessage { get; init; } = null;
}
```

**TuiApp.cs (skeleton)**
```csharp
namespace cli_tasker.Tui;

public class TuiApp
{
    private TuiState _state = new();
    private readonly TuiRenderer _renderer = new();
    private readonly TuiKeyHandler _keyHandler;
    private bool _running = true;

    public TuiApp(string? initialList = null)
    {
        _state = _state with { CurrentList = initialList };
        _keyHandler = new TuiKeyHandler(this);
    }

    public void Run()
    {
        Console.CursorVisible = false;
        Console.Clear();

        while (_running)
        {
            var tasks = LoadTasks();
            _renderer.Render(_state, tasks);

            var key = Console.ReadKey(intercept: true);
            _state = _keyHandler.Handle(key, _state, tasks);
        }

        Console.CursorVisible = true;
        Console.Clear();
    }

    public void Quit() => _running = false;

    private List<TodoTask> LoadTasks()
    {
        var taskList = new TodoTaskList(_state.CurrentList);
        return taskList.GetFilteredTasks()
            .Where(t => string.IsNullOrEmpty(_state.SearchQuery) ||
                        t.Description.Contains(_state.SearchQuery,
                            StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}
```

#### Phase 2: Core Operations

**Deliverables:**
- Toggle check/uncheck (Space/Enter)
- Delete with confirmation (x/Delete)
- Cursor follows task after reorder

**Key behaviors:**
- Space/Enter on task → toggle IsChecked → task re-inserts at top → cursor follows
- x/Delete → show ConfirmationPrompt → soft delete → cursor moves to next task
- After delete of last task → show empty state

#### Phase 3: Edit Operations

**Deliverables:**
- Rename task (r) with TextPrompt pre-filled
- Add new task (a) with empty TextPrompt
- Escape cancels, Enter saves

**Edit flow:**
```csharp
// In TuiKeyHandler for 'r' key
if (_state.Mode == TuiMode.Normal && currentTask != null)
{
    Console.Clear();
    var newDesc = AnsiConsole.Prompt(
        new TextPrompt<string>("Rename task:")
            .DefaultValue(currentTask.Description)
            .Validate(d => string.IsNullOrWhiteSpace(d)
                ? ValidationResult.Error("Cannot be empty")
                : ValidationResult.Success()));

    if (newDesc != currentTask.Description)
    {
        var taskList = new TodoTaskList();
        taskList.RenameTask(currentTask.Id, newDesc);
    }
}
```

#### Phase 4: List Management

**Deliverables:**
- Switch lists (l) with SelectionPrompt
- Move task to list (m) with SelectionPrompt
- Show "all lists" option
- List name in status bar

**List picker:**
```csharp
var lists = TodoTaskList.GetAllListNames().ToList();
lists.Insert(0, "<All Lists>");

var selected = AnsiConsole.Prompt(
    new SelectionPrompt<string>()
        .Title("Switch to list:")
        .AddChoices(lists));

_state = _state with {
    CurrentList = selected == "<All Lists>" ? null : selected,
    CursorIndex = 0
};
```

#### Phase 5: Search & Filter

**Deliverables:**
- Enter search mode (/)
- Live filtering as user types
- Clear search (Escape in search mode)
- Highlight matches in display

**Search state:**
```csharp
// '/' key enters search mode
_state = _state with { Mode = TuiMode.Search, SearchQuery = "" };

// In search mode, keypresses append to SearchQuery
// Escape clears and exits, Enter exits keeping filter
```

#### Phase 6: Multi-Select & Bulk Operations

**Deliverables:**
- Toggle multi-select mode (v)
- Toggle individual selection (Ctrl+Space in normal mode)
- Visual selection indicators [*]
- Bulk check/uncheck/delete
- Confirmation for bulk delete

**Selection UI:**
```
tasks
[*](abc) [ ] - Selected task 1
[ ](def) [ ] - Unselected task
[*](ghi) [x] - Selected completed task

Status: 2 selected | Space:toggle x:delete Esc:clear
```

### File Structure

```
cli-tasker/
├── Tui/
│   ├── TuiApp.cs           # Main loop, coordination
│   ├── TuiState.cs         # State record, TuiMode enum
│   ├── TuiRenderer.cs      # All Spectre.Console rendering
│   └── TuiKeyHandler.cs    # Key → state transition logic
├── Program.cs              # Modified to detect no-args
└── ... (existing files unchanged)
```

### Program.cs Modification

```csharp
static int Main(string[] args)
{
    // NEW: Launch TUI if no arguments
    if (args.Length == 0 && Console.IsInputRedirected == false)
    {
        var tui = new TuiApp();
        tui.Run();
        return 0;
    }

    // Existing command handling...
    var rootCommand = new RootCommand("CLI task manager");
    // ...
}
```

## Acceptance Criteria

### Functional Requirements

- [x] `tasker` with no args launches TUI mode
- [x] `tasker <command>` still works as before (no regression)
- [x] Navigate tasks with j/k or arrow keys
- [x] Toggle task completion with Space or Enter
- [x] Delete task with x or Delete key (with confirmation)
- [x] Rename task with r key
- [x] Add new task with a key
- [x] Switch between lists with l key
- [x] Move task to different list with m key
- [x] Search/filter tasks with / key
- [x] Multi-select with v key, bulk operations work
- [x] Exit with q or Escape
- [x] Status bar shows current list, task count, and key hints
- [x] Cursor follows task after check/uncheck (task re-inserts at top)

### Non-Functional Requirements

- [x] Responsive with 1000+ tasks (virtualization if needed)
- [x] Works in terminals 40+ columns wide, 10+ rows tall
- [x] No data loss - all operations save immediately
- [x] Graceful handling of non-TTY environments (show error)

### Quality Gates

- [x] All existing tests pass (no regression)
- [x] Manual testing of all keyboard shortcuts
- [x] Test edge cases: empty list, single task, multi-line descriptions
- [x] Test terminal resize behavior

## Keyboard Shortcuts Summary

| Key | Normal Mode | Search Mode | Multi-Select |
|-----|-------------|-------------|--------------|
| j / ↓ | Move cursor down | - | Move cursor down |
| k / ↑ | Move cursor up | - | Move cursor up |
| Space | Toggle check | - | Toggle selection |
| Enter | Toggle check | Exit search | Execute bulk action |
| x / Del | Delete task | - | Delete selected |
| r | Rename task | - | - |
| a | Add task | - | - |
| m | Move task | - | Move selected |
| l | Switch list | - | - |
| / | Enter search | - | - |
| v | Enter multi-select | - | Exit multi-select |
| Esc | - | Clear & exit | Clear selection |
| q | Quit TUI | - | - |
| ? | Show help | - | - |

## Dependencies & Prerequisites

- Spectre.Console 0.54.0 (already installed)
- No new dependencies required
- .NET 10.0 (already required)

## Risk Analysis & Mitigation

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| Spectre.Console limitations | High | Medium | Custom main loop, not relying on Live() |
| Terminal compatibility | Medium | Low | Test on macOS, Linux, Windows Terminal |
| Data loss during TUI | High | Low | Save after each operation, use existing save mechanism |
| Performance with many tasks | Medium | Medium | Virtualization/pagination if > 100 visible |

## Future Considerations

1. **Mouse support** - Spectre.Console supports mouse, could add click-to-select
2. **Configurable keybindings** - Store in config.json
3. **Themes** - Color customization for accessibility
4. **Task details panel** - Show full description, timestamps in side panel
5. **Undo support** - In-memory undo stack for session

## References

### Internal References
- Command structure: `AppCommands/*.cs`
- Data layer: `TodoTask/TodoTaskList.cs:GetFilteredTasks()`
- Output patterns: `Output.cs`
- Configuration: `Config/AppConfig.cs`

### External References
- [Spectre.Console Documentation](https://spectreconsole.net/)
- [Interactive Prompts Tutorial](https://spectreconsole.net/console/tutorials/interactive-prompts-tutorial)
- [SelectionPrompt API](https://spectreconsole.net/prompts/selection)
- [TextPrompt API](https://spectreconsole.net/prompts/text)
