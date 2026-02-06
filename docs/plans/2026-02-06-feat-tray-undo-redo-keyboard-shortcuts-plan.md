---
title: "feat: Tray undo/redo keyboard shortcuts"
type: feat
date: 2026-02-06
---

# feat: Tray Undo/Redo Keyboard Shortcuts

Add Cmd+Z (undo) and Cmd+Shift+Z (redo) keyboard shortcuts to the TaskerTray popup, following the same pattern as existing Cmd+W/Cmd+R/Cmd+Q shortcuts.

## Acceptance Criteria

- [x] Cmd+Z undoes the last task operation and refreshes the UI
- [x] Cmd+Shift+Z redoes the last undone operation and refreshes the UI
- [x] StatusText shows "Undone: {description}" or "Redone: {description}" after the operation
- [x] "Nothing to undo" / "Nothing to redo" shown when stacks are empty
- [x] Cmd+Z inside an active inline editor (add/rename TextBox) is left to native text undo — event NOT marked as handled, so TextBox gets it

## Key Design Decisions

1. **TextBox gets priority for Cmd+Z** — When `_activeInlineEditor != null`, return without setting `e.Handled = true` so Avalonia's native TextBox undo fires. This matches standard macOS behavior.

2. **Reload undo history on popup open** — Add `TaskerServices.Default.Undo.ReloadHistory()` in `ShowAtPosition()` so the tray's in-memory undo stack is current with CLI changes. Single SQLite read, negligible cost.

3. **Set status AFTER RefreshTasks()** — `RefreshTasks()` → `DoRefreshTasks()` → `UpdateStatus()` overwrites StatusText. Set the undo/redo feedback message after refresh completes.

4. **Block during drag** — Undo mutates the DB immediately but `RefreshTasks()` is queued during drag. Skip undo/redo when `_state != PopupState.Idle || _dragStartPoint.HasValue`.

5. **No visual undo button** — Keyboard shortcut only. Popup footer is already compact.

6. **Use `HasFlag` for modifier matching** — Existing Cmd+W/R/Q shortcuts use `HasFlag(KeyModifiers.Meta)`, not exact equality. Follow the convention to avoid silent failures with CapsLock or phantom modifier bits.

7. **Extract `HandleUndoRedo` helper** — Keeps `OnKeyDown` as a clean dispatch table. The two branches (undo/redo) differ only in the method called and the label strings.

## Implementation

### `src/TaskerTray/Views/TaskListPopup.axaml.cs`

**New private method:**

```csharp
private void HandleUndoRedo(bool isRedo)
{
    if (_activeInlineEditor != null) return; // Let native TextBox undo handle it
    if (_state != PopupState.Idle || _dragStartPoint.HasValue) return;

    var undo = TaskerServices.Default.Undo;
    try
    {
        var desc = isRedo ? undo.Redo() : undo.Undo();
        RefreshTasks();
        var verb = isRedo ? "Redone" : "Undone";
        var empty = isRedo ? "Nothing to redo" : "Nothing to undo";
        StatusText.Text = desc != null ? $"{verb}: {desc}" : empty;
    }
    catch (Exception ex)
    {
        RefreshTasks();
        StatusText.Text = $"{(isRedo ? "Redo" : "Undo")} failed: {ex.Message}";
    }
}
```

**In `OnKeyDown()` — add after Cmd+R handler, before Cmd+Q:**

```csharp
// Cmd+Shift+Z = Redo (check BEFORE plain Cmd+Z)
else if (e.Key == Key.Z && e.KeyModifiers.HasFlag(KeyModifiers.Meta) && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
{
    HandleUndoRedo(isRedo: true);
    e.Handled = true;
}
// Cmd+Z = Undo
else if (e.Key == Key.Z && e.KeyModifiers.HasFlag(KeyModifiers.Meta) && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
{
    HandleUndoRedo(isRedo: false);
    e.Handled = true;
}
```

**In `ShowAtPosition()` — add before `RefreshTasks()`:**

```csharp
TaskerServices.Default.Undo.ReloadHistory();
```

### Follow-up: Clean up unused AppViewModel undo/redo

`AppViewModel.cs` lines 68-98 have `Undo()` and `Redo()` relay commands that are not wired to the popup. These should be removed in a separate cleanup pass to avoid two parallel undo paths drifting apart.

## References

- Existing keyboard handler pattern: `src/TaskerTray/Views/TaskListPopup.axaml.cs:154`
- TUI undo/redo reference: `Tui/TuiKeyHandler.cs:168-184`
- UndoManager API: `src/TaskerCore/Undo/UndoManager.cs:76-107`
- AppViewModel (unused, mark for removal): `src/TaskerTray/ViewModels/AppViewModel.cs:68-98`
- Undo learnings: `docs/solutions/undo-system/undo-support-for-reorder-operations.md`
