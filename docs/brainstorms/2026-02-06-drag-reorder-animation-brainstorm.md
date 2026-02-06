---
topic: Smooth drag reorder animation for tasks and list headers
date: 2026-02-06
task: "b83"
status: decided
---

# Drag Reorder Animation

## What We're Building

When dragging a task or list header to reorder, sibling items should smoothly slide apart to make space at the drop position. This replaces the current static blue drop indicator with a more physical, spatial animation where items visually "move out of the way."

**Scope:** Both task items and list headers animate during their respective drag reorders.

## Why This Approach

**TranslateTransform on siblings** — when the drop index changes during drag, apply `TranslateTransform.Y` offsets to sibling items to push them up or down by the dragged item's height. Avalonia's existing CSS transitions (150ms CubicEaseOut) handle the smooth animation.

**Why not spacer insertion?** DOM manipulation during drag is fragile in Avalonia — inserting/removing elements can cause layout flicker and interfere with the drop index calculation (which counts Border children).

**Why not margin animation?** Margin changes trigger full relayout, which is less performant and harder to control precisely.

## Key Decisions

- **Animation method:** TranslateTransform.Y on sibling items (no DOM changes during drag)
- **Timing:** 150ms CubicEaseOut (matches existing animation style)
- **Scope:** Both tasks and list headers
- **Drop indicator:** Removed (replaced by the visual gap created by sliding items)
- **Reset:** All transforms reset to 0 on drop completion or drag cancel

## Open Questions

- Should the gap height match the exact dragged item height, or use a fixed standard height?
- Should items animate when the drag first starts (original position opens up), or only when crossing boundaries?
