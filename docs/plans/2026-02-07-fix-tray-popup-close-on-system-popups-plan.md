---
title: "fix: Tray popup closes when opening system popups"
type: fix
date: 2026-02-07
task: 8cb
brainstorm: docs/brainstorms/2026-02-07-tray-popup-close-behavior-brainstorm.md
---

# Fix: Tray Popup Closes When Opening System Popups

## Overview

The tray popup closes whenever any system popup appears (emoji picker, Raycast, Spotlight) because the `Deactivated` event fires unconditionally on focus loss. Fix: remove the `Deactivated` handler entirely. The popup should only close via Escape key or tray icon click — both already work.

## Acceptance Criteria

- [x] Opening emoji picker (Ctrl+Cmd+Space) does NOT close the tray popup
- [x] Opening Raycast/Spotlight does NOT close the tray popup
- [x] Clicking another app window does NOT close the tray popup
- [x] Pressing Escape closes the popup (existing behavior preserved)
- [x] Clicking the tray icon toggles the popup (existing behavior preserved)
- [x] Pending inline adds are saved when popup closes via Escape or tray icon
- [x] Pending inline edits are cancelled when popup closes
- [x] `dotnet build` — no errors

## Implementation

### `src/TaskerTray/Views/TaskListPopup.axaml.cs`

**Step 1: Remove the `Deactivated` handler** (lines 54-62)

Delete:
```csharp
// Close when clicking outside or pressing Escape
Deactivated += async (_, _) =>
{
    SavePendingInlineAdd();
    CancelInlineEdit();
    CancelDrag();
    await HideWithAnimation();
};
```

**Step 2: Move cleanup into `HideWithAnimation()`**

The `Deactivated` handler was the only place that called `SavePendingInlineAdd()`, `CancelInlineEdit()`, and `CancelDrag()` before hiding. Move these into `HideWithAnimation()` so they run regardless of close trigger (Escape or tray icon).

Current:
```csharp
private async Task HideWithAnimation()
{
    PopupContent.Classes.Remove("visible");
    await Task.Delay(150);
    Hide();
    // ... HideFromDock ...
}
```

New:
```csharp
private async Task HideWithAnimation()
{
    SavePendingInlineAdd();
    CancelInlineEdit();
    CancelDrag();
    PopupContent.Classes.Remove("visible");
    await Task.Delay(150);
    Hide();
    // ... HideFromDock ...
}
```

### `src/TaskerTray/App.axaml.cs`

**Step 3: Use `HideWithAnimation()` in tray icon toggle**

The tray icon toggle currently calls `_popup.Hide()` directly (line 135), skipping animation and cleanup. Change to use `HideWithAnimation()`:

Current:
```csharp
if (_popup.IsVisible)
{
    _popup.Hide();
}
```

New:
```csharp
if (_popup.IsVisible)
{
    _ = _popup.HideWithAnimation();
}
```

This requires making `HideWithAnimation()` public (currently private).

## Files Changed

| File | Change |
|------|--------|
| `src/TaskerTray/Views/TaskListPopup.axaml.cs` | Remove `Deactivated` handler, move cleanup to `HideWithAnimation()`, make it public |
| `src/TaskerTray/App.axaml.cs` | Call `HideWithAnimation()` instead of `Hide()` in tray icon toggle |

## References

- Brainstorm: `docs/brainstorms/2026-02-07-tray-popup-close-behavior-brainstorm.md`
- Existing Escape handler: `TaskListPopup.axaml.cs:161-177`
- Tray icon toggle: `App.axaml.cs:133-136`
