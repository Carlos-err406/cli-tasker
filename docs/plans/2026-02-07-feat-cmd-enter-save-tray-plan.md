---
title: "feat: Cmd+Enter to save in tray inline editors"
type: feat
date: 2026-02-07
---

# feat: Cmd+Enter to save in tray inline editors

## Overview

Add Cmd+Enter as the confirm/save shortcut for all text inputs in the TaskerTray popup. For multiline inputs (task add, task edit), change Enter from "submit" to "insert newline" so users can naturally write multi-line descriptions. This matches macOS conventions (Slack, Discord, iMessage).

## Problem Statement

Currently in the tray popup:
- **Task add/edit** (`AcceptsReturn=true`): Enter submits immediately, Shift+Enter inserts a newline. This makes it awkward to write multi-line descriptions — users must remember to hold Shift for every line break.
- **List rename/new list** (`AcceptsReturn=false`): Enter submits. This is fine for single-line inputs.
- There is no Cmd+Enter shortcut anywhere.

## Proposed Solution

| Input Type | Enter (current) | Enter (new) | Cmd+Enter (new) |
|---|---|---|---|
| **Task add** (multiline) | Submits | Inserts newline | Submits |
| **Task edit** (multiline) | Submits | Inserts newline | Submits |
| **List rename** (single-line) | Submits | Submits (unchanged) | Submits (alias) |
| **New list** (single-line) | Submits | Submits (unchanged) | Submits (alias) |
| **Search** | N/A | N/A | No-op (ignored) |

## Technical Approach

### Files to modify

Only one file: `src/TaskerTray/Views/TaskListPopup.axaml.cs`

### Phase 1: Change multiline inputs — Enter inserts newline, Cmd+Enter submits

**`CreateInlineAddField()`** (line 764-782):

```csharp
// BEFORE:
if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
{
    e.Handled = true;
    if (!submitted) { submitted = true; SubmitInlineAdd(...); }
}

// AFTER:
if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Meta))
{
    e.Handled = true;
    if (!submitted) { submitted = true; SubmitInlineAdd(textBox.Text, listName); }
}
else if (e.Key == Key.Escape)
{
    // ... unchanged
}
// Enter without Meta falls through → TextBox inserts newline (AcceptsReturn=true)
```

**`CreateInlineEditField()`** (line 845-857): Same pattern — replace `!KeyModifiers.Shift` check with `KeyModifiers.Meta` check.

### Phase 2: Add Cmd+Enter as alias for single-line inputs

**`CreateInlineListRenameField()`** (line 689-707): Add `KeyModifiers.Meta` as an additional Enter trigger:

```csharp
if (e.Key == Key.Enter) // Cmd+Enter or plain Enter both submit
{
    e.Handled = true;
    if (!submitted) { submitted = true; SubmitListRename(...); }
}
```

No change needed — Enter already works, and Cmd+Enter is also `Key.Enter` with a modifier. The existing `if (e.Key == Key.Enter)` check already catches both.

**`CreateInlineListNameField()`** (line 950-968): Same — no change needed, Enter already covers Cmd+Enter.

### Phase 3: Verify no conflicts

- Cmd+Enter does NOT conflict with existing window-level shortcuts (Cmd+K/W/R/Z/Q)
- The window-level `OnKeyDown` (line 158-210) checks `_activeInlineEditor != null` and returns early for undo/redo. Cmd+Enter would be handled by the TextBox's KeyDown handler first (tunneling/bubbling), so no window-level handler needed.

## Edge Cases

| Scenario | Behavior |
|---|---|
| Cmd+Enter on empty text | Same as current: cancel/close editor (empty text detection in submit methods) |
| Cmd+Enter with no active editor | No-op (no TextBox has focus, so no KeyDown fires) |
| Cmd+Enter in search box | No-op (search has no submit concept) |
| Double Cmd+Enter press | Guarded by `submitted` flag — second press is ignored |
| Cmd+Enter then immediate popup close | `_inlineAddSubmitted` flag prevents double-save in `SavePendingInlineAdd()` |
| LostFocus after Cmd+Enter | Guarded by `submitted` flag and `_editingTaskId == null` check |

## Acceptance Criteria

- [x] Cmd+Enter submits task add (multiline)
- [x] Cmd+Enter submits task edit/rename (multiline)
- [x] Cmd+Enter submits list rename (single-line)
- [x] Cmd+Enter submits new list name (single-line)
- [x] Enter inserts newline in task add (no longer submits)
- [x] Enter inserts newline in task edit (no longer submits)
- [x] Enter still submits list rename (single-line, unchanged)
- [x] Enter still submits new list name (single-line, unchanged)
- [x] Escape still cancels all editors
- [x] LostFocus still submits (unchanged)
- [x] No double-submission issues
- [x] No UI hint text added (clean UI)

## Gotchas from Institutional Knowledge

From `docs/solutions/ui-bugs/avalonia-textbox-keydown-event-bubbling.md`:
- Always set `e.Handled = true` BEFORE clearing shared state
- Guard LostFocus handlers against already-cancelled state
- Avalonia uses bubbling event routing — unhandled events propagate to parent

From `docs/solutions/ui-bugs/list-duplication-on-inline-add.md`:
- LostFocus fires asynchronously — use `submitted` flag and `_showCount` counter
- Capture generation counters in closures to detect stale events

## References

- `src/TaskerTray/Views/TaskListPopup.axaml.cs` — all changes in this file
  - `CreateInlineAddField()`: line 734
  - `CreateInlineEditField()`: line 817
  - `CreateInlineListRenameField()`: line 659
  - `CreateInlineListNameField()`: line 920
  - Window-level keybindings: line 158-210
