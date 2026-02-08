# Brainstorm: Tray Popup Close Behavior

**Date:** 2026-02-07
**Task:** 8cb — "tray closes when opening system popups"

## What We're Building

Change the tray popup so it only closes via two explicit actions:
1. **Escape key**
2. **Clicking the tray icon** (toggle)

Remove the `Deactivated` event handler that currently closes the popup whenever it loses focus. This fixes the bug where system popups (emoji picker, Raycast, Spotlight) close the tray.

## Why This Approach

The current `Deactivated` handler can't distinguish between "user clicked another app" and "system overlay appeared." Rather than adding complex heuristics to detect overlay types, just remove auto-close entirely. The popup stays open until the user explicitly dismisses it — predictable, simple, no surprises.

## Key Decisions

1. **Remove `Deactivated` handler** — no more auto-close on blur
2. **Escape key** — already works (existing KeyDown handler)
3. **Tray icon toggle** — already works (App.axaml.cs toggles show/hide on icon click)
4. **No "click outside" close** — deliberately omitted for simplicity and to avoid the same overlay detection problem

## Open Questions

None — this is a subtraction, not an addition.

## Next Steps

Run `/workflows:plan` when ready to implement.
