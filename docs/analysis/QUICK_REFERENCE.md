# Tag Colors Feature - Quick Reference Card

**Print this page for quick lookup during planning and implementation.**

---

## The Feature (One Sentence)

Same tag text always renders with the same color across CLI, TUI, and TaskerTray using hash-to-palette approach.

---

## Current State

```
CLI:        #urgent → [cyan]#urgent[/]
TUI:        #urgent → [cyan]#urgent[/]
TaskerTray: #urgent → [#EF4444] (Red)  ✓ CORRECT

Goal: Make CLI and TUI match TaskerTray
```

---

## Files That Need Changes

| File | Type | Changes | Lines |
|------|------|---------|-------|
| `Output.cs` | Modify | Update `FormatTags()` method | 33-38 |
| `Tui/TuiRenderer.cs` | Modify | Update `FormatTags()` method | ~170 |
| `src/TaskerTray/Views/TaskListPopup.axaml.cs` | Refactor | Use shared utility instead of inline | 1544-1563 |
| `src/TaskerCore/Colors/TagColorHelper.cs` | Create | New shared utility class | N/A |
| Tests | Create | Unit tests for consistency | N/A |

---

## Implementation Checklist

- [ ] Answer 4 critical blockers (Q1-Q4) from Decision Matrix
- [ ] Create `TagColorHelper` class with:
  - Color palette (10 colors from TaskerTray)
  - `GetTagColor(tag)` → hex string
  - `GetTagColorMarkup(tag)` → Spectre markup for CLI/TUI
  - `GetTagColorAvalonia(tag)` → Avalonia Color for TaskerTray
- [ ] Update `Output.FormatTags()` to use per-tag colors
- [ ] Update `TuiRenderer.FormatTags()` to use per-tag colors
- [ ] Refactor `TaskListPopup.GetTagColor()` to use shared utility
- [ ] Add 5+ unit tests for hash consistency
- [ ] Manual test on macOS, Linux (if available)
- [ ] Verify against all 18 edge cases

---

## 10-Color Palette (From TaskerTray)

```
ColorPalette[0]  = "#3B82F6"  // Blue
ColorPalette[1]  = "#10B981"  // Emerald
ColorPalette[2]  = "#F59E0B"  // Amber
ColorPalette[3]  = "#EF4444"  // Red
ColorPalette[4]  = "#8B5CF6"  // Violet
ColorPalette[5]  = "#EC4899"  // Pink
ColorPalette[6]  = "#06B6D4"  // Cyan
ColorPalette[7]  = "#84CC16"  // Lime
ColorPalette[8]  = "#F97316"  // Orange
ColorPalette[9]  = "#6366F1"  // Indigo
```

---

## Hash Algorithm

```csharp
var index = Math.Abs(tag.GetHashCode()) % ColorPalette.Length;
return ColorPalette[index];
```

**Key Property**: Same input string always produces same color.

---

## Code Examples (After Implementation)

### Output.cs
```csharp
// Before
public static string FormatTags(string[]? tags)
{
    if (tags is not { Length: > 0 }) return "";
    var tagStr = string.Join(" ", tags.Select(t => $"#{t}"));
    return $"  [cyan]{Spectre.Console.Markup.Escape(tagStr)}[/]";
}

// After
public static string FormatTags(string[]? tags)
{
    if (tags is not { Length: > 0 }) return "";
    var tagMarkup = string.Join(" ",
        tags.Select(t => TagColorHelper.GetTagColorMarkup(t)));
    return $"  {tagMarkup}";
}
```

### TuiRenderer.cs
```csharp
// Same pattern as Output.cs
// Replace inline [cyan] with TagColorHelper.GetTagColorMarkup(tag)
```

### TaskListPopup.axaml.cs
```csharp
// Before: GetTagColor() is inline static method
// After: Call TagColorHelper.GetTagColorAvalonia(tag)

Background = new SolidColorBrush(
    TagColorHelper.GetTagColorAvalonia(tag)
)
```

---

## User Flows Affected (11 Total)

1. Add task with tags (CLI/TUI/TaskerTray)
2. View task list - CLI
3. View task list - TUI
4. View task list - TaskerTray
5. View task detail - CLI get command
6. Add task with tags - TUI inline
7. Add task with tags - TaskerTray inline
8. Search/filter by tags - TUI
9. Edit task tags - CLI
10. Edit task tags - TUI
11. Edit task tags - TaskerTray

**All flows follow same pattern**: Parse tags → Hash to color → Display with color

---

## Edge Cases to Test (Sample)

- [ ] Same tag in different contexts (CLI, TUI, TaskerTray) → same color
- [ ] Different tags → different colors (usually)
- [ ] Long tag names → render without overflow
- [ ] Unicode in tags (`#café`, `#日本語`) → hash correctly
- [ ] Checked/completed task with tags → colors visible or dimmed?
- [ ] Searched task with matching tag → tag colored or highlighted?
- [ ] Multi-select with colored tags → selection indicator visible?
- [ ] 16-color terminal (legacy) → colors render or degrade?
- [ ] Light terminal background → colors readable?
- [ ] Very large task list (100+) → performance unchanged?

---

## Critical Questions (4 Must Answer)

| Q | Decision | Options | Assumption |
|---|----------|---------|-----------|
| Q1 | Collision tolerance | Accept or expand palette | Accept ✓ |
| Q2 | Terminal fallback | True-color only or degrade | True-color only ✓ |
| Q3 | Hash stability | GetHashCode or SHA256 | GetHashCode ✓ |
| Q4 | Utility location | TaskerCore.Colors or inline | Shared class ✓ |

**If assumptions don't match requirements**: See Decision Matrix for alternatives.

---

## Configurations (Pick One)

| Config | Effort | Risk | Time |
|--------|--------|------|------|
| **A: Recommended** | Low | Low | 4 hrs |
| **B: Maximum Accessibility** | High | Medium | 12-16 hrs |
| **C: Minimal Implementation** | Ultra-low | Ultra-low | 2-3 hrs |

**Recommended** = Accept collision risk, no terminal fallback, shared utility, low effort.

---

## Performance Impact

- Color lookup: O(1) - constant time hash → array index
- No database queries or file I/O
- Called once per tag per render
- Expected impact: **Negligible**

---

## Testing Requirements

**Unit Tests** (~10 tests):
- Hash consistency (same input → same output)
- Palette membership (result in palette array)
- Edge cases (empty, unicode, long strings)

**Integration Tests** (~5 tests):
- CLI list output contains color codes
- TUI renderer produces color codes
- TaskerTray displays colors
- Cross-platform consistency

**Manual Tests**:
- Verify colors visible on iTerm2, Terminal.app, VSCode Terminal
- Check readability on dark background
- Verify no regressions in existing features

---

## Success Criteria

- [x] Task is well-understood (this document)
- [x] All flows are mapped (11 flows)
- [x] All gaps are identified (12 gaps)
- [ ] 4 blocking questions answered
- [ ] Configuration chosen
- [ ] Implementation started
- [ ] All tests passing
- [ ] Manual testing complete

---

## Common Mistakes to Avoid

1. **Duplicating code** - Use shared `TagColorHelper` utility
2. **Ignoring edge cases** - Test unicode, special chars, long tags
3. **Assuming terminal support** - Might fail on legacy terminals
4. **Forgetting about TaskerTray** - Must refactor to use shared utility
5. **Not testing cross-platform** - Colors might render differently
6. **Assuming consistent hash** - Hash might change on .NET upgrade
7. **No test coverage** - Tests ensure consistency over time

---

## Key Assumptions (If Not Overridden)

- Collisions acceptable (different tags, same color)
- True-color terminals only (no 16-color fallback)
- GetHashCode() adequate (no SHA256 needed)
- 10-color palette sufficient
- Tags always strings (not empty or null)
- No persistence of color data (computed on render)

---

## Document Locations

**Quick Decision**:
  → `TAG_COLORS_EXECUTIVE_SUMMARY.md` (5 min)
  → `TAG_COLORS_DECISION_MATRIX.md` (15 min)

**Detailed Implementation**:
  → `TAG_COLORS_VISUAL_FLOWS.md` (20 min)
  → `2026-02-05-tag-colors-ux-flow-analysis.md` (45 min)

**Navigation**:
  → `README.md` (all documents indexed by role)

All in: `/Users/carlos/self-development/cli-tasker/docs/analysis/`

---

## Key Code References

| What | File | Lines | What |
|------|------|-------|------|
| Current cyan hardcoding | Output.cs | 33-38 | FormatTags() |
| Current cyan hardcoding | TuiRenderer.cs | ~170 | FormatTags() |
| Working implementation | TaskListPopup.axaml.cs | 1544-1563 | GetTagColor() |
| Task model | TodoTask.cs | 5-69 | Tags property |

---

## Next 3 Steps

1. **Today**: Read Executive Summary + Decision Matrix
2. **Tomorrow**: Get answers to 4 critical blockers
3. **This week**: Start implementation following Visual Flows guide

---

**Print this for quick reference during standups and code reviews.**
