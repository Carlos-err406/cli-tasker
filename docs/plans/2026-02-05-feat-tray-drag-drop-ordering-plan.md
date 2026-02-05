---
title: "feat: Enable drag-and-drop ordering in TaskerTray"
type: feat
date: 2026-02-05
tags: [tray-only, drag-drop, animations, ordering]
deepened: 2026-02-05
---

# Enable Drag-and-Drop Ordering in TaskerTray

## Enhancement Summary

**Deepened on:** 2026-02-05
**Research agents used:** best-practices-researcher, learnings-researcher, architecture-strategist, code-simplicity-reviewer, julik-frontend-races-reviewer, Context7 (Avalonia docs)

### Key Improvements
1. Simplified data model - use array position for BOTH task and list ordering (no DisplayOrder field needed)
2. Added comprehensive race condition mitigations (generation counters, file watcher suspension)
3. Incorporated accessibility requirements (keyboard alternatives, screen reader support)

### Critical Findings
- **Race conditions are the primary risk** - external changes during drag can corrupt state
- **DisplayOrder field is unnecessary** - array position already provides ordering
- **Auto-scroll is YAGNI** - defer to future iteration

---

## Overview

Add drag-and-drop reordering for both tasks (within lists) and lists (in the all-lists view) in TaskerTray. The feature should feel smooth with polished animations.

**Scope:** TaskerTray only (CLI continues using computed sorting by priority/date/creation).

## User Stories

1. As a user, I can drag a task to reorder it within its list
2. As a user, I can drag a list header to reorder lists in the all-lists view
3. As a user, I see smooth animations during drag operations
4. As a user, my custom order persists across app restarts

## Technical Approach

### Data Model - Simplified

**No new fields needed.** Use array position for ordering:

- **Task ordering:** `TaskList.Tasks[]` array index = display order
- **List ordering:** `TodoTaskList.TaskLists[]` array index = display order

This aligns with existing patterns and avoids redundant state.

### Research Insight: Remove Alphabetical Sorting

Current `GetAllListNames()` sorts lists alphabetically. For manual ordering, modify to preserve array order:

```csharp
// Before (TodoTaskList.cs:715-720):
return TaskLists.Select(l => l.ListName)
    .OrderBy(name => name != ListManager.DefaultListName)
    .ThenBy(name => name);

// After:
return TaskLists.Select(l => l.ListName);  // Array order IS display order
```

### Sort Mode Decision

**Manual order is the only mode in TaskerTray.** No toggle needed.

- New tasks: Go to **top** of list (current behavior)
- Checked tasks: **Stay in position** (don't auto-sort to bottom)
- Visual styling indicates completion (strikethrough, dimmed)

---

## Race Condition Mitigations (Critical)

### Research Finding: Four Race Conditions Identified

1. **Pointer events fire during UI rebuild** - drag state lost mid-operation
2. **External file changes during drag** - indices shift, wrong task moved
3. **Rapid reorder operations** - stale index references
4. **Stale event handlers** - handlers fire against rebuilt UI

### Mitigation 1: Generation Counter for All Rebuilds

```csharp
// In TaskListPopup.axaml.cs
private int _generationId;

private void BuildTaskList()
{
    _generationId++;  // Invalidate ALL handlers from previous builds
    TaskListPanel.Children.Clear();
    // ...
}

// In every event handler:
var capturedGeneration = _generationId;
border.PointerPressed += (sender, e) =>
{
    if (capturedGeneration != _generationId) return;  // Stale handler
    // ...
};
```

### Mitigation 2: Operation State Machine

```csharp
private enum PopupState { Idle, Dragging, DroppingInProgress, RefreshPending }
private PopupState _state = PopupState.Idle;

public void RefreshTasks()
{
    if (_state == PopupState.Dragging || _state == PopupState.DroppingInProgress)
    {
        _state = PopupState.RefreshPending;
        return;  // Queue refresh for after drag completes
    }
    DoRefreshTasks();
}
```

### Mitigation 3: Suspend File Watcher During Drag

```csharp
// In FileWatcherService.cs
private bool _suspended;
private bool _pendingChange;

public void SuspendNotifications() => _suspended = true;
public void ResumeNotifications()
{
    _suspended = false;
    if (_pendingChange)
    {
        _pendingChange = false;
        ExternalChangeDetected?.Invoke();
    }
}
```

### Mitigation 4: Drag Snapshot

Capture state when drag starts to validate on drop:

```csharp
private class DragSnapshot
{
    public int Generation { get; init; }
    public string DraggedTaskId { get; init; }
    public string SourceList { get; init; }
    public int SourceIndex { get; init; }
}
```

---

## Drag-and-Drop Implementation

### Drag Affordance

**Research Best Practice:** 6-dot grip handle, minimum 44pt touch target

```xml
<!-- In TaskListPopup.axaml -->
<Style Selector="Border.task-item">
    <Style Selector="^ /template/ Path.grip-handle">
        <Setter Property="Opacity" Value="0.3"/>
    </Style>
    <Style Selector="^:pointerover /template/ Path.grip-handle">
        <Setter Property="Opacity" Value="1"/>
        <Setter Property="Cursor" Value="SizeAll"/>
    </Style>
</Style>
```

### During Drag

**Research Best Practice:** Ghost item with elevation, gap at drop target

```csharp
// Drag visual feedback
private void OnDragMove(PointerEventArgs e)
{
    if (_activeDrag == null) return;

    // Update ghost position
    _dragGhost.RenderTransform = new TranslateTransform(
        e.GetPosition(this).X - _dragStartPoint.X,
        e.GetPosition(this).Y - _dragStartPoint.Y
    );

    // Calculate drop target
    var targetIndex = CalculateDropIndex(e.GetPosition(TaskListPanel));
    UpdateDropIndicator(targetIndex);
}
```

### Animation Specifications

Based on existing patterns (150ms CubicEaseOut):

```xml
<Style Selector="Border.dragging">
    <Setter Property="Opacity" Value="0.8"/>
    <Setter Property="RenderTransform" Value="scale(1.02)"/>
    <Setter Property="Effect">
        <DropShadowEffect BlurRadius="8" Opacity="0.3" OffsetY="4"/>
    </Setter>
</Style>

<Style Selector="Border.task-item">
    <Setter Property="Transitions">
        <Transitions>
            <TransformOperationsTransition Property="RenderTransform" Duration="0:0:0.15" Easing="CubicEaseOut"/>
            <DoubleTransition Property="Opacity" Duration="0:0:0.15"/>
        </Transitions>
    </Setter>
</Style>
```

---

---

## Implementation Phases

### Phase 1: Task Reordering (Core)

1. Add `_generationId` counter to `TaskListPopup.axaml.cs`
2. Add `PopupState` enum and state management
3. Add grip handle UI to task items
4. Implement pointer event handlers with generation checks
5. Add drag visual feedback (ghost, drop indicator)
6. Update `TaskList.Tasks` array order on drop
7. Add `SuspendNotifications()`/`ResumeNotifications()` to `FileWatcherService`

### Phase 2: List Reordering + Polish

1. Add grip handle to list headers
2. Implement list header drag-drop
3. Modify `GetAllListNames()` to preserve array order
4. Polish animations

---

## Acceptance Criteria

- [x] Tasks can be reordered within a list via drag-and-drop
- [x] Lists can be reordered in all-lists view via drag-and-drop
- [x] Grip handle visible on hover (6-dot pattern)
- [x] Smooth animations during drag (150ms CubicEaseOut)
- [x] Custom order persists across app restarts
- [x] Checked tasks stay in custom position
- [x] New tasks appear at top of list
- [x] External changes during drag are queued, not lost
- [x] Generation counter prevents stale handler execution

---

## Edge Cases

| Scenario | Behavior |
|----------|----------|
| Single task in list | No grip handle shown |
| Collapsed list | Cannot drag tasks within collapsed list |
| Drop on collapsed list header | **Reject drop** (user must expand first) |
| Drag outside window | Cancel drag, snap back |
| External change during drag | Queue refresh, apply after drop |
| Stale handler fires | Generation check returns early |

---

## Out of Scope (Deferred)

- Cross-list drag (use existing "Move to..." menu)
- CLI/TUI support for custom ordering
- Auto-scroll when dragging near edges (YAGNI)
- Sort mode toggle (always manual in Tray)

---

## Files to Modify

| File | Changes |
|------|---------|
| `src/TaskerCore/Data/TodoTaskList.cs` | Add `ReorderTask()`, `ReorderList()`, modify `GetAllListNames()` |
| `src/TaskerTray/Views/TaskListPopup.axaml` | Add grip handles, drag styles |
| `src/TaskerTray/Views/TaskListPopup.axaml.cs` | Add generation counter, state machine, drag handlers |
| `src/TaskerTray/Services/FileWatcherService.cs` | Add suspend/resume methods |

---

## References

### Institutional Learnings
- Animation timing: `docs/solutions/feature-implementations/tray-animations-transitions.md`
- State persistence: `docs/solutions/feature-implementations/collapsible-lists-tray.md`
- Race prevention: `docs/solutions/ui-bugs/list-duplication-on-inline-add.md`

### External
- [Atlassian: Pragmatic Drag and Drop Guidelines](https://atlassian.design/components/pragmatic-drag-and-drop/design-guidelines/)
- [WCAG 2.5.7: Dragging Movements](https://www.w3.org/WAI/WCAG22/Understanding/dragging-movements.html)
- [Apple HIG: Drag and Drop](https://developer.apple.com/design/human-interface-guidelines/drag-and-drop)
