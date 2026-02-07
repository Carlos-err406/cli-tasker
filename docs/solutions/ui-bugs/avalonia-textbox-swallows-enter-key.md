---
title: Avalonia TextBox Swallows Enter Key — Use Tunnel Routing for Cmd+Enter
category: ui-bugs
tags:
  - avalonia
  - keyboard
  - textbox
  - event-routing
  - tunneling
  - tray
module: TaskerTray
symptoms:
  - Cmd+Enter does nothing in TextBox with AcceptsReturn=true
  - KeyDown handler never fires for Enter key in multiline TextBox
  - Custom keyboard shortcuts on Enter key are silently ignored
date_solved: 2026-02-07
files_changed:
  - src/TaskerTray/Views/TaskListPopup.axaml.cs
---

# Avalonia TextBox Swallows Enter Key — Use Tunnel Routing for Cmd+Enter

## Problem

After adding Cmd+Enter as a save shortcut for multiline TextBox inputs in the tray popup, the shortcut did nothing. The `KeyDown` handler was never called for `Key.Enter`.

## Root Cause

Avalonia's `TextBox` control internally handles `Key.Enter` in its own `OnKeyDown` override when `AcceptsReturn=true`. It marks the event as `e.Handled = true` and inserts a newline **before** any external `KeyDown` event handler fires.

The default `textBox.KeyDown += ...` syntax uses **bubbling** routing (`RoutingStrategies.Bubble`). In Avalonia's event system:

1. **Tunnel** (parent → child) — fires first
2. **Direct** (on the element itself)
3. **Bubble** (child → parent) — fires last

The TextBox's internal handler runs at the Direct phase. By the time the event bubbles to our handler, it's already been handled and consumed.

## Solution

Use `AddHandler` with `RoutingStrategies.Tunnel` to intercept the key event **before** the TextBox processes it:

```csharp
// BROKEN — bubbling handler never sees Enter because TextBox consumes it
textBox.KeyDown += (_, e) =>
{
    if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Meta))
    {
        e.Handled = true;
        SubmitInlineAdd(textBox.Text, listName);
    }
};

// WORKING — tunnel handler fires before TextBox's internal OnKeyDown
textBox.AddHandler(KeyDownEvent, (_, e) =>
{
    if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Meta))
    {
        e.Handled = true;
        SubmitInlineAdd(textBox.Text, listName);
    }
}, RoutingStrategies.Tunnel);
```

When `e.Handled = true` is set in the tunnel phase, the TextBox's internal handler is skipped entirely — no newline is inserted and the submit fires.

Plain Enter (without Cmd) is NOT handled in our tunnel handler, so it falls through to the TextBox's internal handler which inserts a newline as normal.

## Key Insight

**Any custom keyboard shortcut involving `Key.Enter` on a TextBox with `AcceptsReturn=true` MUST use tunnel routing.** The `KeyDown +=` syntax (bubbling) will never work because the TextBox consumes Enter first.

This also applies to other keys that TextBox handles internally (Tab, Backspace, etc.).

## Prevention

When adding keyboard shortcuts to Avalonia TextBox controls:

1. Check if the key is handled internally by TextBox (`Enter`, `Tab`, `Backspace`, arrow keys)
2. If yes, use `AddHandler(KeyDownEvent, handler, RoutingStrategies.Tunnel)`
3. If no (like `Escape`), `KeyDown +=` (bubbling) works fine — TextBox doesn't consume Escape

## Cross-References

- [Avalonia TextBox KeyDown Event Bubbling](./avalonia-textbox-keydown-event-bubbling.md) — related event routing issue with Escape key
- [Avalonia GitHub Issue #860](https://github.com/AvaloniaUI/Avalonia/issues/860) — TextBox KeyDown handling
- [Avalonia GitHub Discussion #17522](https://github.com/AvaloniaUI/Avalonia/discussions/17522) — Cmd+Enter with AcceptsReturn
