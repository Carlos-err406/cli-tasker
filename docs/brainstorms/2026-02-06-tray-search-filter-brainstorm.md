---
title: "Search/filter in tray app"
date: 2026-02-06
tags: [tray, ui, ux, search, filter]
---

# Tray Search/Filter

## What We're Building

A unified search box in the tray popup that filters tasks in real-time using `LIKE '%query%'` on the task description. Since descriptions already contain tags and metadata inline, a simple text match covers all filtering needs.

## UI Behavior

- **Toggle**: Magnifying glass icon in the header row. Clicking reveals a search bar (sliding down or inline).
- **Scope**: List-agnostic — always searches across ALL lists regardless of the current list filter.
- **Filtering**: Real-time as-you-type with debounce. Single `LIKE '%query%'` on description column (case-insensitive).
- **Results**: Grouped by list as usual, but only showing matching tasks.
- **Escape handling**: When search bar is focused:
  - First Escape: clears search text (shows all tasks again)
  - Second Escape (or Escape with empty text): closes search bar
  - **Conflict**: Escape currently closes the popup window. The search TextBox must intercept Escape before the window-level handler when it has focus and contains text.

## Why This Approach

- **Unified text box** over separate controls — YAGNI. One input, one SQL clause.
- **`LIKE '%query%'` on description** — tags like `#work` and priority markers are already in the description text, so no special parsing is needed.
- **Search icon toggle** over always-visible bar — saves vertical space in a compact popup.
- **List-agnostic** — the whole point of search is to find things fast regardless of which list they're in.

## Key Decisions

1. **Unified search box** — no separate tag/priority/status controls
2. **LIKE on description** — simple SQL, no special query parsing
3. **Search icon toggle in header** — not always visible
4. **List-agnostic** — ignores current list filter when searching
5. **Real-time filtering** — debounced, no Enter required
6. **Escape intercept** — search bar captures Escape when focused with text

## Open Questions

- What debounce interval? (150ms? 300ms?)
- Should the search icon show a visual indicator when search text is active (e.g., colored dot)?
- Should the list filter dropdown be hidden/disabled while search is active?
- How to handle the Escape key conflict with popup close — key event routing in Avalonia?
