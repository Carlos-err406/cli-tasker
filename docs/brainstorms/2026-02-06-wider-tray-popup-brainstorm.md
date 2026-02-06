---
title: "Make tray popup wider"
date: 2026-02-06
tags: [tray, ui, ux, popup-sizing]
---

# Wider Tray Popup

## What We're Building

Increase the tray popup width from 340px to ~400px. The current width feels cramped. Keep it a fixed size (no user-resizable).

## Why This Approach

- **Fixed width is simpler** — no resize handles, no persisting user preference, no edge cases with minimum/maximum constraints.
- **~400px is a modest bump** — noticeably roomier without feeling oversized. Sits between macOS Reminders (~360px) and Slack popup (~440px).
- **One-line change** — just update `Width="340"` to `Width="400"` in the AXAML. May also want to bump height proportionally.

## Key Decisions

1. **Fixed, not resizable** — YAGNI. Can add resizing later if needed.
2. **~400px target** — user preference based on "feels cramped" feedback.
3. **Height**: evaluate whether 520px still works at the new width or should scale up slightly.

## Open Questions

- Should the height increase proportionally (e.g., 520 → 560)?
- Does the positioning logic in `ShowAtPosition()` need adjustment for the wider window near screen edges?
