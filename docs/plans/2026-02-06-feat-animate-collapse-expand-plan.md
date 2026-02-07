---
title: "feat: Animate list collapse/expand in tray"
type: feat
date: 2026-02-06
task: 97f
---

# feat: Animate list collapse/expand in tray

Clicking the chevron to collapse/expand a list section snaps instantly with no animation. The XAML transitions (MaxHeight, Opacity, chevron rotation) are already defined but never play because `OnToggleListCollapsed` calls `BuildTaskList()` which destroys and recreates all elements.

## Acceptance Criteria

- [x] Collapsing a list animates smoothly (MaxHeight + Opacity fade out, chevron rotates)
- [x] Expanding a list animates smoothly (reverse)
- [x] Rapid toggle (clicking chevron multiple times) works correctly
- [x] Chevron tooltip updates ("Expand list" / "Collapse list")
- [x] State persists to SQLite (already works)
- [x] All other code paths (search, undo, inline edit, drag) still work via BuildTaskList()
- [x] Run `update.sh patch`

## Implementation

### 1. Add `_listChevronButtons` dictionary

#### TaskListPopup.axaml.cs

Add a new dictionary alongside `_listTaskPanels`:

```csharp
private Dictionary<string, Button> _listChevronButtons = new();
```

Clear it in `BuildTaskList()` where `_listTaskPanels` is cleared. Populate it in `AddListHeader()` after creating the chevron button.

### 2. Fix stale closure in chevron click handler

#### TaskListPopup.axaml.cs — `AddListHeader()`

The current handler captures `isCollapsed` at build time:

```csharp
chevronBtn.Click += (_, _) => OnToggleListCollapsed(listName, !isCollapsed);
```

Change to pass no boolean — `OnToggleListCollapsed` will read current state dynamically:

```csharp
chevronBtn.Click += (_, _) => OnToggleListCollapsed(listName);
```

### 3. Rewrite `OnToggleListCollapsed` for in-place toggling

#### TaskListPopup.axaml.cs

```csharp
private void OnToggleListCollapsed(string listName)
{
    // Read current state from the element, not a captured boolean
    var isCurrentlyCollapsed = _listTaskPanels.TryGetValue(listName, out var panel)
        && panel.Classes.Contains("collapsed");

    var newCollapsed = !isCurrentlyCollapsed;

    // Persist to SQLite first — only toggle UI if write succeeds
    TodoTaskList.SetListCollapsed(listName, newCollapsed);

    // Cancel any active inline edit inside this list
    if (_addingToList == listName || /* editing task in this list */)
        CancelInlineEdit();

    // Toggle classes in-place (triggers XAML transitions)
    if (panel != null)
    {
        if (newCollapsed)
            panel.Classes.Add("collapsed");
        else
            panel.Classes.Remove("collapsed");
    }

    if (_listChevronButtons.TryGetValue(listName, out var chevron))
    {
        if (newCollapsed)
            chevron.Classes.Add("collapsed");
        else
            chevron.Classes.Remove("collapsed");

        ToolTip.SetTip(chevron, newCollapsed ? "Expand list" : "Collapse list");
    }

    UpdateStatus(); // refresh footer counts
}
```

### 4. Optionally refine animation timing

#### TaskListPopup.axaml

Review current values after testing:
- `StackPanel.listTasks` MaxHeight: 200ms CubicEaseInOut — likely fine
- `StackPanel.listTasks` Opacity: 150ms CubicEaseOut — likely fine
- `Button.chevron` rotation: 200ms CubicEaseInOut — likely fine

Adjust if animations feel too fast/slow during manual testing.

## Edge Cases Handled

- **Stale closure**: Handler reads state from element classes, not captured boolean
- **Missing dictionary entry**: Uses `TryGetValue`, bails silently if panel not found
- **Inline edit active**: Cancels inline edit before collapsing
- **Auto-expand on add**: `StartInlineAdd` still calls `BuildTaskList()` — no animation for that path (acceptable, it needs to insert a TextBox)
- **Other rebuild triggers**: 12+ call sites for `BuildTaskList()` re-read SQLite state, so in-place toggle and rebuild paths stay consistent

## References

- Brainstorm: `docs/brainstorms/2026-02-06-animate-collapse-expand-brainstorm.md`
- Working pattern: `CollapseAllListsForDrag()` at `TaskListPopup.axaml.cs:2319`
- Working pattern: `RestoreCollapsedLists()` at `TaskListPopup.axaml.cs:2351`
- Working pattern: `UpdateTaskStatusInPlace()` at `TaskListPopup.axaml.cs:1437`
- XAML transitions: `TaskListPopup.axaml:91-121`
- Documented solution: `docs/solutions/feature-implementations/tray-animations-transitions.md`
