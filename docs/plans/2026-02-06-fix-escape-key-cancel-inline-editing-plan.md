---
title: "fix: Escape key should cancel renames and adds in TaskerTray"
type: fix
date: 2026-02-06
---

# fix: Escape key should cancel renames and adds in TaskerTray

Pressing Escape while adding or renaming a task in the tray popup doesn't properly cancel the operation. The TextBox remains visible, the event bubbles up and may close the popup, and for task renames, Escape can actually *submit* the edit via LostFocus.

## Acceptance Criteria

- [x] Escape in inline add dismisses the TextBox and returns to normal view
- [x] Escape in inline task rename dismisses the TextBox without saving changes
- [x] Escape does NOT bubble up to close the popup or clear search
- [x] No duplicate submissions from LostFocus firing after Escape
- [x] Stale LostFocus events from previous popup shows are ignored for inline edit

## Context

The codebase already has the correct pattern in two places:
- `CreateInlineListRenameField()` (line ~700)
- `CreateInlineListNameField()` (line ~952)

Both use: `e.Handled = true` + `submitted = true` + `CancelInlineEdit()` + `BuildTaskList()`

Two inline fields are missing parts of this pattern:

### `CreateInlineAddField()` (line ~772) — missing `BuildTaskList()` and `e.Handled`

```csharp
// BEFORE (buggy)
else if (e.Key == Key.Escape)
{
    submitted = true;
    CancelInlineEdit();
}

// AFTER (fixed)
else if (e.Key == Key.Escape)
{
    e.Handled = true;
    submitted = true;
    CancelInlineEdit();
    BuildTaskList();
}
```

### `CreateInlineEditField()` (line ~848) — missing everything

```csharp
// BEFORE (buggy — no submitted guard, no BuildTaskList, no e.Handled)
else if (e.Key == Key.Escape)
{
    CancelInlineEdit();
}

// AFTER (fixed)
else if (e.Key == Key.Escape)
{
    e.Handled = true;
    submitted = true;
    CancelInlineEdit();
    BuildTaskList();
}
```

Additionally, `CreateInlineEditField()` needs:
- A `var submitted = false;` local variable (currently missing entirely)
- `submitted` guards on both Enter and Escape handlers
- `capturedShowCount` guard on LostFocus (currently missing)

### Also fix the two "correct" handlers that are missing `e.Handled`

`CreateInlineListRenameField()` and `CreateInlineListNameField()` both have `BuildTaskList()` but are missing `e.Handled = true` — the event still bubbles and may close the popup.

## References

- Institutional learning: `docs/solutions/ui-bugs/task-duplication-on-blur-outside-window.md`
- Institutional learning: `docs/solutions/ui-bugs/list-duplication-on-inline-add.md`
- Source: `src/TaskerTray/Views/TaskListPopup.axaml.cs`
