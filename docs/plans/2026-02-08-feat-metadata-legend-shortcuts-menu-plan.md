---
title: "feat: Metadata legend / shortcuts menu"
type: feat
date: 2026-02-08
task: 6bb
brainstorm: docs/brainstorms/2026-02-08-metadata-legend-shortcuts-menu-brainstorm.md
---

# feat: Metadata legend / shortcuts menu

## Overview

Add a help/legend panel to TUI and Tray showing keyboard shortcuts, metadata prefixes, and date format reference. Triggered by `?` in the TUI and a `?` button (or Cmd+?) in the Tray popup.

## Problem Statement

- 9 metadata marker types (p1/p2/p3, @date, #tag, ^id, !id, -^id, -!id, ~id) are hard to remember
- TUI status bar hints are already truncated — can't fit all shortcuts
- Tray has zero discoverability for keyboard shortcuts (Cmd+K, Cmd+W, Cmd+R, etc.)
- No in-app reference exists for date format syntax (@today, @+3d, @jan15, etc.)

## Acceptance Criteria

- [x] TUI: `?` key in Normal mode toggles a help panel that replaces the task list area
- [x] TUI: `?` or `Esc` dismisses the help panel, returning to Normal mode
- [x] TUI: Help panel shows shortcuts (grouped by mode), metadata prefixes, date formats
- [x] TUI: `?:help` hint added to Normal mode status bar
- [x] TUI: `?` key distinguished from `/` (Search) via Shift modifier check
- [x] Tray: `?` button in header toggles help panel replacing Grid.Row 2 content
- [x] Tray: Cmd+? keyboard shortcut also toggles help panel
- [x] Tray: Esc dismisses help panel (priority: cancel editor > close help > clear search > hide popup)
- [x] Tray: Help panel resets to hidden on each popup show
- [x] Content is surface-specific (TUI shows TUI keys, Tray shows Tray keys); metadata + dates are shared
- [x] `dotnet build` — no errors
- [x] Tests pass

## Implementation

### Architectural Decision: TUI Panel Approach

The TUI renderer is line-by-line with no concept of horizontal panels. A true sidebar would require significant refactoring. Instead, use a **full-area overlay** that replaces the task list area (same approach as Tray). This is simpler, gives plenty of room for content, and follows the existing modal pattern (Search, MultiSelect, etc.).

The help state is a **boolean flag** (`ShowHelp`) on `TuiState`, not a new `TuiMode`. This means:
- The user stays in Normal mode while help is visible
- `?` and `Esc` are the only keys handled differently (toggle help off)
- All other keys are ignored while help is shown (modal behavior without a dedicated mode)

### Step 1: Add `ShowHelp` flag to TuiState

**File:** `Tui/TuiState.cs`

Add `bool ShowHelp` property to the `TuiState` record:

```csharp
public bool ShowHelp { get; init; } = false;
```

### Step 2: Handle `?` key in TuiKeyHandler

**File:** `Tui/TuiKeyHandler.cs`

**2a.** In `Handle()` (top-level dispatch), check `ShowHelp` before the mode switch. If help is shown, only `?` and `Esc` dismiss it; all other keys are ignored:

```csharp
public TuiState Handle(ConsoleKeyInfo key, TuiState state, IReadOnlyList<TodoTask> tasks)
{
    // Help overlay intercepts all keys
    if (state.ShowHelp)
    {
        if (key.Key == ConsoleKey.Escape ||
            (key.Key == ConsoleKey.Oem2 && (key.Modifiers & ConsoleModifiers.Shift) != 0))
            return state with { ShowHelp = false };
        return state; // Ignore all other keys
    }

    return state.Mode switch { ... };
}
```

**2b.** In `HandleNormalMode()`, distinguish `?` (Shift+/) from `/` (Search). The existing `case ConsoleKey.Oem2` at line 90 needs a Shift check:

```csharp
// Help legend (Shift+/ = ?)
case ConsoleKey.Oem2 when (key.Modifiers & ConsoleModifiers.Shift) != 0:
    return state with { ShowHelp = true };

// Search (plain /)
case ConsoleKey.Oem2:
    return state with { Mode = TuiMode.Search, SearchQuery = "" };
```

### Step 3: Render help panel in TuiRenderer

**File:** `Tui/TuiRenderer.cs`

**3a.** In `Render()`, check `state.ShowHelp` and call `RenderHelpPanel()` instead of `RenderTasks()`:

```csharp
RenderHeader(state, tasks.Count);
if (state.ShowHelp)
    RenderHelpPanel(state);
else
    RenderTasks(state, tasks);
RenderStatusBar(state, tasks.Count);
```

**3b.** Add `RenderHelpPanel()` method. Uses the same `WriteLineCleared()` / `ClearLine()` helpers. Content fits the available terminal height (same viewport calculation as `RenderTasks`). Sections:

1. **Metadata Prefixes** — p1/p2/p3, @date, #tag, ^id, !id, ~id (user-facing only, skip -^id and -!id which are system-generated)
2. **Date Formats** — @today, @tomorrow, @yesterday, @monday..@sunday, @jan15, @+3d, @+2w, @+1m, @2026-02-15
3. **Keyboard Shortcuts** — grouped: Normal, Search, Multi-select, Input, Due Date

Each line rendered as dim key + description, e.g.: `[dim]p1[/] [dim]p2[/] [dim]p3[/]  High / Medium / Low priority`

Fill remaining available lines with `ClearLine()` to avoid ghost content from the task list.

**3c.** Update status bar hints: when `state.ShowHelp` is true, show `[dim]?[/]:close [dim]esc[/]:close` instead of the normal hints.

**3d.** Add `[dim]?[/]:help` to the Normal mode hints string at line 423.

### Step 4: Tray — Add `?` button to header

**File:** `src/TaskerTray/Views/TaskListPopup.axaml`

Add a fourth column to the header Grid and a `?` button:

```xml
<Grid ColumnDefinitions="*,Auto,Auto,Auto">
    <!-- ... existing columns 0-2 ... -->
    <Button Grid.Column="3"
            x:Name="HelpButton"
            Content="?"
            Width="28"
            Height="28"
            FontSize="16"
            Padding="0"
            Margin="6,0,0,0"
            Background="Transparent"
            Foreground="#888"
            CornerRadius="6"
            HorizontalContentAlignment="Center"
            VerticalContentAlignment="Center"
            ToolTip.Tip="Shortcuts &amp; metadata help (⌘?)"
            Click="OnHelpClick"/>
</Grid>
```

### Step 5: Tray — Add help panel to AXAML

**File:** `src/TaskerTray/Views/TaskListPopup.axaml`

Add a hidden ScrollViewer in Grid.Row 2 alongside the existing task list ScrollViewer:

```xml
<!-- Help Panel - toggled visibility, same Grid.Row as task list -->
<ScrollViewer Grid.Row="2"
              x:Name="HelpPanel"
              IsVisible="False"
              HorizontalScrollBarVisibility="Disabled"
              VerticalScrollBarVisibility="Auto">
    <StackPanel Margin="12" Spacing="12">
        <!-- Content built programmatically in code-behind -->
    </StackPanel>
</ScrollViewer>
```

### Step 6: Tray — Implement help toggle logic

**File:** `src/TaskerTray/Views/TaskListPopup.axaml.cs`

**6a.** Add field and toggle method:

```csharp
private bool _showHelp = false;

private void ToggleHelp()
{
    _showHelp = !_showHelp;
    HelpPanel.IsVisible = _showHelp;
    // Hide task list when help is shown
    // (find the task list ScrollViewer parent and toggle visibility)
    TaskListPanel.Parent!.IsVisible = !_showHelp;

    if (_showHelp)
        BuildHelpPanel();
}
```

**6b.** `OnHelpClick` handler calls `ToggleHelp()`.

**6c.** `BuildHelpPanel()` creates TextBlock children in HelpPanel's StackPanel. Three sections with headers:

- **Shortcuts**: Cmd+K (search), Cmd+W (close), Cmd+R (refresh), Cmd+Z (undo), Cmd+Shift+Z (redo), Esc (close/cancel), Cmd+Q (quit)
- **Metadata Prefixes**: same as TUI
- **Date Formats**: same as TUI

Style: section headers in white bold, items in #AAA with #666 descriptions.

**6d.** Add Cmd+? to `OnKeyDown()`:

```csharp
else if (e.Key == Key.Oem2 && e.KeyModifiers.HasFlag(KeyModifiers.Meta))
{
    e.Handled = true;
    ToggleHelp();
}
```

Note: `?` is `Shift+/` so `Key.Oem2` with Meta is sufficient (Shift is implicit when typing `?`). Test on macOS to confirm — may need `Key.OemQuestion` instead.

**6e.** Update Esc handler priority (line 152 area):

```csharp
if (e.Key == Key.Escape)
{
    e.Handled = true;
    if (_isInlineEditing) { CancelInlineEdit(); BuildTaskList(); }
    else if (_showHelp) { ToggleHelp(); }
    else if (!string.IsNullOrWhiteSpace(SearchTextBox.Text)) { SearchTextBox.Text = ""; }
    else { _ = HideWithAnimation(); }
}
```

**6f.** Reset help on popup show — in `ShowAtPosition()`, set `_showHelp = false` and update visibility.

### Step 7: Add tests

**File:** `tests/TaskerCore.Tests/Tui/HelpPanelTests.cs` (new)

Test the TUI state transitions:
1. `?` in Normal mode sets `ShowHelp = true`
2. `?` while `ShowHelp` sets `ShowHelp = false`
3. `Esc` while `ShowHelp` sets `ShowHelp = false`
4. `/` (plain, no Shift) still enters Search mode (not help)
5. Other keys while `ShowHelp` are ignored (state unchanged)
6. `?` in non-Normal modes (Search, MultiSelect) does not open help

Note: Tray help panel is UI-only, tested manually.

## Files Changed

| File | Change |
|------|--------|
| `Tui/TuiState.cs` | Add `ShowHelp` bool property |
| `Tui/TuiKeyHandler.cs` | Handle `?` toggle, distinguish from `/` |
| `Tui/TuiRenderer.cs` | Add `RenderHelpPanel()`, update status bar hints |
| `src/TaskerTray/Views/TaskListPopup.axaml` | Add `?` button + HelpPanel ScrollViewer |
| `src/TaskerTray/Views/TaskListPopup.axaml.cs` | Toggle logic, Cmd+? shortcut, Esc priority, BuildHelpPanel() |
| `tests/TaskerCore.Tests/Tui/HelpPanelTests.cs` | New test file for TUI help state transitions |

## References

- Task: 6bb
- Brainstorm: `docs/brainstorms/2026-02-08-metadata-legend-shortcuts-menu-brainstorm.md`
- TUI key handler: `Tui/TuiKeyHandler.cs:90` (existing `/` binding)
- TUI renderer status bar: `Tui/TuiRenderer.cs:421-429`
- TUI state: `Tui/TuiState.cs:15-116`
- Tray header AXAML: `src/TaskerTray/Views/TaskListPopup.axaml:148-187`
- Tray keyboard handler: `src/TaskerTray/Views/TaskListPopup.axaml.cs:150-202`
- Inline metadata reference: `docs/reference/inline-metadata.md`
- Date parser: `src/TaskerCore/Parsing/DateParser.cs`
- Learnings — Avalonia TextBox key bubbling: `docs/solutions/ui-bugs/avalonia-textbox-keydown-event-bubbling.md`
- Learnings — Tray animations: `docs/solutions/feature-implementations/tray-animations-transitions.md`
