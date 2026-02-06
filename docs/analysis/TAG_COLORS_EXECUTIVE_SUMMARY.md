# Tag Colors Feature - Executive Summary

**Feature**: Consistent hash-based tag coloring across CLI, TUI, and TaskerTray
**Status**: Analysis complete; 12 specification gaps identified
**Blocking Decision**: 4 critical questions must be answered before coding

---

## The Ask

Same tag text always renders with the same color across all three UIs using a hash-to-palette approach:
- TaskerTray: Already has 10-color hash-based implementation ✓
- CLI: Currently hardcoded cyan, need to apply per-tag colors
- TUI: Currently hardcoded cyan, need to apply per-tag colors

---

## User Flows Affected (11 Total)

All three UIs have identical flows:
1. **Add task with tags** → Tags stored in TodoTask.Tags array
2. **View task list** → Tags displayed with colors
3. **View task detail** → Tags shown with colors
4. **Search/filter** → Tags maintain colors during search
5. **Edit tags** → New tags get assigned colors
6. **Mark complete** → Tags visible on completed tasks
7. **Inline add** (TUI/TaskerTray) → Tags colored on display
8. **Inline edit** (TUI/TaskerTray) → Tags recolored after edit
9. **Move task** → Colors follow tags across lists
10. **TaskerTray drag-drop** → Colors preserved during reordering
11. **TUI multi-select** → Colors visible on selected tasks

---

## Edge Cases (18 Critical)

**Tag Content**:
- Empty strings, unicode (emoji, CJK), very long names, special characters
- Case sensitivity: `#tag` vs `#TAG` → different colors?
- Hash collisions: With 10 colors & 50+ unique tags, collisions guaranteed

**Rendering**:
- Search highlighting + tag colors = conflict?
- Checked task tags = dimmed or normal color?
- Multi-select highlighting = interaction with tag color?

**Terminal Constraints**:
- True-color (24-bit) vs 256-color vs 16-color terminals
- Light vs dark background readability
- Platform differences (macOS vs Linux vs Windows)

---

## Critical Blockers (Must Answer Before Code)

### Q1: Hash Collision Tolerance
**Problem**: 10-color palette + 50+ unique tags = forced collisions
**Question**: Is visual collision acceptable, or expand palette?
**Impact**: Determines if feature meets "same tag = same color" requirement
**Assumption If Unanswered**: Collisions acceptable ✓

### Q2: Terminal Color Capability Fallback
**Problem**: Not all terminals support true color (24-bit RGB)
**Question**: Graceful degradation to 256-color or 16-color?
**Impact**: Feature might break on legacy systems
**Assumption If Unanswered**: Assume true-color support only ✓

### Q3: Hash Algorithm Stability
**Problem**: `string.GetHashCode()` is runtime-specific
**Question**: Should use deterministic hash (SHA256) for stability?
**Impact**: If .NET version changes, colors might shift
**Assumption If Unanswered**: Use GetHashCode(); accept variation ✓

### Q4: Shared Code Location
**Problem**: GetTagColor() needed in 3 places (Output, TuiRenderer, TaskListPopup)
**Question**: Shared utility class or duplicate code?
**Impact**: Affects maintainability and palette consistency
**Assumption If Unanswered**: Create shared TagColorHelper in TaskerCore ✓

---

## Important Questions (Affects UX)

- **Q5**: Real-time color preview during inline tag edit?
- **Q6**: How do search highlights interact with tag colors?
- **Q7**: Should tag colors be dimmed for completed tasks?
- **Q8**: Are the 10 colors WCAG accessible?
- **Q9**: Hash consistency across platforms (macOS/Linux/Windows)?

---

## Implementation Scope (If All Blockers Cleared)

**Low Complexity Tasks**:
1. Create `TagColorHelper` utility class
2. Update `Output.FormatTags()` to use per-tag colors
3. Update `TuiRenderer.FormatTags()` to use per-tag colors
4. Add unit tests for hash consistency
5. Manual testing on macOS, Linux, Windows terminals

**Estimated Effort**: 2-4 hours (code), 2-3 hours (testing)

**Files Changed**:
- Create: `src/TaskerCore/Colors/TagColorHelper.cs` (or in Output.cs)
- Modify: `Output.cs` (5-10 lines)
- Modify: `Tui/TuiRenderer.cs` (5-10 lines)
- Modify: `src/TaskerTray/Views/TaskListPopup.axaml.cs` (refactor GetTagColor to use helper)
- Create/Modify: Test files (10-15 tests)

---

## What's Already Working ✓

TaskerTray implementation (lines 1544-1563 of TaskListPopup.axaml.cs):

```csharp
private static Color GetTagColor(string tag)
{
    var colors = new[]
    {
        "#3B82F6", // Blue
        "#10B981", // Emerald
        "#F59E0B", // Amber
        "#EF4444", // Red
        "#8B5CF6", // Violet
        "#EC4899", // Pink
        "#06B6D4", // Cyan
        "#84CC16", // Lime
        "#F97316", // Orange
        "#6366F1", // Indigo
    };

    var index = Math.Abs(tag.GetHashCode()) % colors.Length;
    return Color.Parse(colors[index]);
}
```

This is the **baseline implementation to replicate** in CLI/TUI.

---

## Current Gaps in Specification

| Gap | Severity | Category |
|-----|----------|----------|
| Collision handling | 🔴 Critical | Color mapping |
| Terminal fallback | 🔴 Critical | Rendering |
| Hash stability | 🔴 Critical | Implementation |
| Shared location | 🔴 Critical | Architecture |
| Search highlighting | 🟠 Important | UX interaction |
| Checked task colors | 🟠 Important | Visual hierarchy |
| Color-blind support | 🟠 Important | Accessibility |
| Unicode handling | 🟡 Nice-to-have | Edge case |
| Long tag names | 🟡 Nice-to-have | Edge case |
| Config/theming | 🟡 Nice-to-have | Flexibility |

---

## Recommended Next Steps

1. **Today**: Answer Q1-Q4 above
2. **Tomorrow**: Verify color palette meets WCAG accessibility standards
3. **Later**: Create TagColorHelper class
4. **Later**: Update Output.FormatTags() and TuiRenderer.FormatTags()
5. **Later**: Add tests and manual verification

---

## Key Assumptions

If above questions remain unanswered, implementation will assume:

✓ Collisions acceptable (10 colors fixed)
✓ True-color terminals only (no fallback)
✓ GetHashCode() adequate (no custom hash)
✓ Tags unchanged when task completed (no dimming)
✓ Tag color takes precedence over search highlighting
✓ No real-time preview during edit
✓ No user configuration or theming
✓ Colors not persisted, only computed on render

---

## Success Criteria

- [ ] Same tag text renders same color in all 3 UIs
- [ ] Colors render without error on iTerm2, Terminal.app, VSCode Terminal, Linux terminals
- [ ] No regression in CLI list, get, TUI, or TaskerTray functionality
- [ ] Unit tests verify hash consistency
- [ ] Performance unchanged (color lookup is O(1))
- [ ] Edge cases documented and tested

---

## Questions for Product/Design

Before implementation:

1. **Collision Tolerance**: Is it OK for 2+ different tags to sometimes get the same color?
2. **Terminal Compatibility**: What's minimum terminal spec (16-color? 256-color? true-color)?
3. **Accessibility**: Should color-blind mode be supported?
4. **Stability**: Is hash consistency across .NET versions a concern?

Answers to these will unlock implementation.

---

**Full Analysis**: See `2026-02-05-tag-colors-ux-flow-analysis.md`
