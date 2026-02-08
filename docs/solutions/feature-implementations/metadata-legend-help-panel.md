---
title: Metadata legend and shortcuts help panel for TUI and Tray
category: feature-implementations
module: Tui, TaskerTray
tags: [help, shortcuts, metadata, legend, tui, tray, keychar, console-key]
symptoms:
  - Users can't remember all 9 metadata prefix types
  - TUI status bar hints are truncated and can't show all shortcuts
  - Tray has zero discoverability for keyboard shortcuts
  - No in-app reference for date format syntax (@today, @+3d, @jan15, etc.)
date: 2026-02-08
---

# Metadata Legend and Shortcuts Help Panel

## Problem

The inline metadata system has 9 marker types (p1/p2/p3, @date, #tag, ^id, !id, -^id, -!id, ~id) plus multiple date format variants. The TUI status bar can only show a truncated subset of shortcuts, and the Tray popup had no help at all. Users had to consult external docs to remember syntax.

## Solution

Added a togglable help panel to both TUI and Tray showing three sections: metadata prefixes, date formats, and keyboard shortcuts.

**TUI (`Tui/TuiKeyHandler.cs`, `Tui/TuiRenderer.cs`, `Tui/TuiState.cs`):**

- `bool ShowHelp` flag on `TuiState` (not a new `TuiMode`)
- `?` key toggles help; `Esc` or `?` dismisses
- Help panel replaces the task list area (full-area overlay)
- Help intercept at the top of `Handle()` blocks all other keys while help is shown
- `?:help` hint added to Normal mode status bar

**Tray (`TaskListPopup.axaml`, `TaskListPopup.axaml.cs`):**

- `?` button in header (4th column in the Grid)
- `Cmd+?` keyboard shortcut
- `HelpPanel` ScrollViewer in Grid.Row 2, toggled with `TaskListScrollViewer`
- Esc priority: cancel editor > close help > clear search > hide popup
- Help resets to hidden on each `ShowAtPosition()` call

## Key Insight — KeyChar vs ConsoleKey on macOS

The most important gotcha: **on macOS terminals, `ConsoleKey` values for shifted punctuation are unreliable.** Pressing `?` (Shift+/) does NOT produce `ConsoleKey.Oem2` with `ConsoleModifiers.Shift`. The key may not even map to `ConsoleKey.Oem2` at all.

```csharp
// BROKEN — never triggers on macOS
case ConsoleKey.Oem2 when (key.Modifiers & ConsoleModifiers.Shift) != 0:
    return state with { ShowHelp = true };

// ALSO BROKEN — ? may not map to ConsoleKey.Oem2
case ConsoleKey.Oem2:
    return key.KeyChar == '?' ? helpState : searchState;
```

**Working approach** — check `KeyChar` before the switch:

```csharp
private TuiState HandleNormalMode(ConsoleKeyInfo key, TuiState state, ...)
{
    // KeyChar is always reliable regardless of terminal/OS key mapping
    if (key.KeyChar == '?')
        return state with { ShowHelp = true };

    switch (key.Key)
    {
        case ConsoleKey.Oem2: // '/' still works for search
            return state with { Mode = TuiMode.Search, SearchQuery = "" };
        ...
    }
}
```

The `Handle()` top-level help dismiss also uses `KeyChar`:

```csharp
if (state.ShowHelp)
{
    if (key.Key == ConsoleKey.Escape || key.KeyChar == '?')
        return state with { ShowHelp = false };
    return state; // Block all other keys
}
```

## Prevention

- **Always use `key.KeyChar` for punctuation detection in TUI key handlers.** Reserve `key.Key` (`ConsoleKey` enum) for non-printable keys (arrows, Escape, Enter, function keys).
- When adding new single-character TUI shortcuts, check `KeyChar` before the `switch (key.Key)` block.
- The `ConsoleKey.Oem*` values are keyboard-layout and terminal-dependent. They work for `/` but not reliably for shifted variants like `?`, `!`, `@`, `#`.

## Related

- `docs/solutions/ui-bugs/avalonia-textbox-keydown-event-bubbling.md` — Similar key handling gotcha in Avalonia
- `docs/solutions/feature-implementations/tray-search-filter.md` — Tray search and Esc priority chain
- `docs/reference/inline-metadata.md` — Full metadata reference
- `docs/reference/commands.md` — CLI commands reference
- PR #29
