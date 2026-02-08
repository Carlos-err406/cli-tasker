---
title: Metadata Legend / Shortcuts Menu
task: 6bb
date: 2026-02-08
---

# Metadata Legend / Shortcuts Menu

## What We're Building

A help/legend panel accessible from both TUI and Tray that shows keyboard shortcuts, metadata prefixes, and date format reference. Triggered by `?` in the TUI and a `?` button (or Cmd+?) in the Tray popup.

**Surfaces:** TUI + Tray (not CLI)

## Why This Approach

- Metadata prefixes are getting crowded (9 marker types: p1/p2/p3, @date, #tag, ^id, !id, -^id, -!id, ~id)
- TUI status bar hints are already truncated — can't fit all shortcuts
- Tray has zero discoverability for keyboard shortcuts (Cmd+K, Cmd+W, Cmd+R, Cmd+Z, etc.)
- The `?` key is the established convention for help overlays (vim, htop, less)

## Key Decisions

1. **Compact panel, not full-screen overlay** — shows as a sidebar/panel on the right side of the TUI, doesn't fully replace the task list. Less disruptive. In the Tray, shows as a compact card/panel replacing the task list area.

2. **Three content sections:**
   - Keyboard shortcuts (grouped by mode: normal, search, multi-select, input, date input)
   - Metadata prefixes (p1, @date, #tag, ^id, !id, -^id, -!id, ~id)
   - Date format examples (@today, @tomorrow, @friday, @+3d, @jan15, @2026-02-15)

3. **Static content** — always shows the same content regardless of current mode. Simpler to implement, predictable for users.

4. **TUI trigger:** `?` key toggles the panel. Esc or `?` dismisses.

5. **Tray trigger:** `?` button in the popup header. Cmd+? also works. Esc dismisses.

## Open Questions

- Exact layout/sizing of the compact panel in TUI — how wide should the sidebar be?
- Should the `?` hint be added to the TUI status bar (e.g., `?:help` at the end)?
- Tray panel: replace task list area entirely, or overlay on top?

## References

- TUI status bar hints: `Tui/TuiRenderer.cs` (RenderStatusBar, ~line 421)
- TUI keyboard handling: `Tui/TuiApp.cs`
- Tray keyboard handling: `src/TaskerTray/Views/TaskListPopup.axaml.cs` (~line 150)
- Inline metadata reference: `docs/reference/inline-metadata.md`
- Date parser: `src/TaskerCore/Parsing/DateParser.cs`
