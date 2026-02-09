---
title: "feat: Tray sticky list headers"
type: feat
date: 2026-02-08
---

# feat: Tray sticky list headers

## Overview

When scrolling through tasks in the Tray popup's "All Lists" view, the current section's list header should pin to the top of the scroll area (iOS-style push-replace). The sticky header is the real header element — chevron, task count, add button, and menu button all remain functional while pinned. Only one header is sticky at a time; the next section's header pushes the current one out.

## Problem Statement

When a list has many tasks, the header scrolls off-screen, forcing the user to scroll back up to interact with it (add task, collapse, rename, delete). This is friction that sticky headers eliminate by keeping the active section's header always visible.

## Proposed Solution

Transform-based pinning using `TransformOperations.Parse("translateY(Npx)")` on the header `Border` elements. On `ScrollChanged`, compute which header should be sticky and apply a Y-offset to pin it at the top of the scroll viewport. When the next header approaches, clamp the transform so the sticky header gets pushed upward.

### Why transforms work

Avalonia 11.3.0's hit-testing fully accounts for `RenderTransform` — clicks register at the **visual** position. Confirmed by Avalonia's own unit test (`HitTest_Should_Find_Control_Translated_Outside_Parent_Bounds`) and the composition renderer source (`CompositionTarget.HitTestCore` inverts the transform matrix). This means buttons on the sticky header remain clickable at their pinned position.

## Technical Approach

### File to modify

`src/TaskerTray/Views/TaskListPopup.axaml.cs` and `src/TaskerTray/Views/TaskListPopup.axaml`

### Implementation

#### 1. Add header tracking dictionary

```csharp
// TaskListPopup.axaml.cs — new field alongside _listTaskPanels
private List<(string listName, Border header)> _listHeaders = new();
```

Use an ordered `List` (not `Dictionary`) because sticky logic needs headers in visual order. Clear in `BuildTaskList()` alongside the other tracking collections. Populate in `AddListHeader()`.

#### 2. Add XAML style for sticky state

```xml
<!-- TaskListPopup.axaml — new style -->
<Style Selector="Border.listHeader.sticky">
    <Setter Property="ZIndex" Value="50"/>
    <Setter Property="Background" Value="#303030"/>
    <Setter Property="BoxShadow" Value="0 2 8 0 #20000000"/>
</Style>
```

- `ZIndex="50"` — above normal content, below drag ghost (`ZIndex="100"`)
- `Background="#303030"` — matches the app header background, opaque to prevent content bleed-through
- `BoxShadow` — subtle bottom shadow for visual separation

#### 3. Subscribe to ScrollChanged

```csharp
// In constructor or InitializeComponent follow-up
TaskListScrollViewer.ScrollChanged += OnScrollChanged;
```

#### 4. Core sticky logic — OnScrollChanged

```csharp
private Border? _currentStickyHeader;
private Avalonia.Animation.Transitions? _savedTransitions;

private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
{
    if (_listHeaders.Count <= 1 || _currentListFilter != null)
        return; // No sticky in single-list view or when filtered to one list

    // Skip during drag operations to avoid RenderTransform conflicts
    if (_state != PopupState.Idle)
        return;

    var scrollOffset = TaskListScrollViewer.Offset.Y;
    var viewportTop = scrollOffset;

    Border? stickyCandidate = null;
    double stickyCandidateY = 0;
    Border? nextHeader = null;
    double nextHeaderY = 0;

    // Find the header that should be sticky (last one scrolled past the top)
    // and the next header (for push calculation)
    for (int i = 0; i < _listHeaders.Count; i++)
    {
        var (_, header) = _listHeaders[i];
        // Skip collapsed sections' headers
        if (i + 1 < TaskListPanel.Children.Count)
        {
            var idx = TaskListPanel.Children.IndexOf(header);
            if (idx + 1 < TaskListPanel.Children.Count &&
                TaskListPanel.Children[idx + 1] is StackPanel sp &&
                sp.Classes.Contains("collapsed"))
                continue;
        }

        var headerTop = header.Bounds.Top; // Natural Y in TaskListPanel
        if (headerTop <= viewportTop)
        {
            stickyCandidate = header;
            stickyCandidateY = headerTop;
        }
        else if (stickyCandidate != null && nextHeader == null)
        {
            nextHeader = header;
            nextHeaderY = headerTop;
        }
    }

    // Un-sticky the previous header if it changed
    if (_currentStickyHeader != null && _currentStickyHeader != stickyCandidate)
    {
        _currentStickyHeader.Classes.Remove("sticky");
        _currentStickyHeader.RenderTransform = TransformOperations.Parse("translateY(0px)");
        if (_savedTransitions != null)
            _currentStickyHeader.Transitions = _savedTransitions;
        _currentStickyHeader = null;
    }

    if (stickyCandidate == null)
        return;

    // Make it sticky
    if (_currentStickyHeader != stickyCandidate)
    {
        // Disable transitions while sticky to prevent jelly lag
        _savedTransitions = stickyCandidate.Transitions;
        stickyCandidate.Transitions = null;
        stickyCandidate.Classes.Add("sticky");
        _currentStickyHeader = stickyCandidate;
    }

    // Calculate pin offset
    var pinOffset = viewportTop - stickyCandidateY;

    // If next header is approaching, clamp so sticky gets pushed up
    if (nextHeader != null)
    {
        var headerHeight = stickyCandidate.Bounds.Height;
        var maxOffset = nextHeaderY - stickyCandidateY - headerHeight;
        pinOffset = Math.Min(pinOffset, maxOffset);
    }

    stickyCandidate.RenderTransform = TransformOperations.Parse($"translateY({pinOffset}px)");
}
```

#### 5. Reset sticky state on BuildTaskList

At the end of `BuildTaskList()`, after the tree is rebuilt:

```csharp
// Reset sticky state since all elements were recreated
if (_currentStickyHeader != null)
{
    _currentStickyHeader = null;
    _savedTransitions = null;
}

// Recalculate sticky after layout completes
Dispatcher.UIThread.Post(() => OnScrollChanged(null, null!), DispatcherPriority.Render);
```

Note: `OnScrollChanged` needs to handle `null` sender gracefully (it already would since sender isn't used).

#### 6. Disable sticky during drag

In `StartTaskDrag()` and `StartListDrag()`, reset sticky before drag begins:

```csharp
// Reset sticky header before drag
if (_currentStickyHeader != null)
{
    _currentStickyHeader.Classes.Remove("sticky");
    _currentStickyHeader.RenderTransform = TransformOperations.Parse("translateY(0px)");
    if (_savedTransitions != null)
        _currentStickyHeader.Transitions = _savedTransitions;
    _currentStickyHeader = null;
    _savedTransitions = null;
}
```

The `_state != PopupState.Idle` guard in `OnScrollChanged` prevents re-pinning during drag.

#### 7. Scroll to inline add field when triggered from sticky header

In `StartInlineAdd()`, after `BuildTaskList()` rebuilds with the inline field:

```csharp
// If the add field was triggered from a sticky header, ensure it's visible
Dispatcher.UIThread.Post(() =>
{
    // The inline input Border has class "inlineInput" — find it and scroll into view
    // BuildTaskList already inserts it; just ensure the ScrollViewer shows it
}, DispatcherPriority.Render);
```

#### 8. ClipToBounds consideration

The `TaskListPanel` StackPanel does not set `ClipToBounds` (defaults to `false` in Avalonia). The `ScrollViewer` clips its content inherently. Since we're translating headers _within_ the scroll viewport (not outside it), `ClipToBounds` should not be an issue — the sticky header is positioned within the visible area of the ScrollViewer.

## Acceptance Criteria

- [ ] Scrolling down in "All Lists" view pins the current section header at the top
- [ ] Next section header pushes the pinned header upward (push-replace)
- [ ] Sticky header has opaque background, subtle shadow, and elevated ZIndex
- [ ] All buttons on sticky header remain clickable (chevron, add, menu)
- [ ] Hover state (`:pointerover`) works on sticky header
- [ ] Scrolling back up un-pins the header smoothly
- [ ] No "jelly" lag — header tracks scroll position 1:1 (transitions disabled while sticky)
- [ ] Sticky is disabled during drag-and-drop operations
- [ ] Sticky state recomputes after `BuildTaskList()` rebuilds
- [ ] Single-list filter view has no sticky behavior
- [ ] Collapsed sections' headers are not sticky candidates
- [ ] No performance jank with 5+ lists

## Key Risks

1. **`Bounds.Top` accuracy during scroll** — `Bounds` gives position relative to the parent `StackPanel`, not the `ScrollViewer`. Need to verify this is the natural (layout) position, not the visual position after scroll. If not, use `header.TranslatePoint(new Point(0, 0), TaskListPanel)` instead.

2. **ScrollChanged event signature** — Avalonia's `ScrollChangedEventArgs` may differ from WPF. Need to check if the event fires on every pixel of scroll or only on discrete changes. If not granular enough, subscribe to `TaskListScrollViewer.GetObservable(ScrollViewer.OffsetProperty)` instead.

3. **Transition restoration** — `_savedTransitions` stores a reference to the original `Transitions` collection. If `BuildTaskList` recreates the header, the saved reference becomes stale. The rebuild reset in step 5 handles this.

## References

- Brainstorm: `docs/brainstorms/2026-02-08-tray-sticky-list-headers-brainstorm.md`
- Avalonia hit-testing source: `CompositionTarget.HitTestCore` inverts `RenderTransform` matrix
- Avalonia hit-test unit test: `HitTest_Should_Find_Control_Translated_Outside_Parent_Bounds`
- Institutional learning: `docs/solutions/ui-bugs/avalonia-transform-operations-vs-translate-transform.md` — always use `TransformOperations.Parse()`, never `TranslateTransform` objects
- Drag system transforms: `TaskListPopup.axaml.cs` lines 2260, 2471, 2479
- `_listTaskPanels` dictionary: `TaskListPopup.axaml.cs` line 1942
- `BuildTaskList()`: `TaskListPopup.axaml.cs` lines 296-440
- `AddListHeader()`: `TaskListPopup.axaml.cs` lines 442-610
- `listHeader` XAML styles: `TaskListPopup.axaml` lines 71-89
