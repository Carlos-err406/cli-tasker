# Tag Colors Feature - Decision Matrix

**Purpose**: Document all critical decisions needed to unblock implementation.

---

## Decision 1: Hash Collision Tolerance

| Aspect | Option A: Accept Collisions | Option B: Prevent Collisions |
|--------|----------------------------|---------------------------|
| **Design** | Keep 10-color fixed palette | Expand palette to 20+ colors OR use custom hash |
| **Impact** | With 50+ tags, ~15-20 will collide | Each tag gets unique(ish) color |
| **Complexity** | Low - no code changes | Medium - redesign palette or hash |
| **User Experience** | Tags distinguished by TEXT not color | Each tag visually unique |
| **Example** | `#urgent` and `#feature` both blue | Each tag gets different color |
| **Accessibility** | Text is primary identifier ✓ | Color becomes more important |
| **Maintenance** | Simple, no future changes | Might need rebalancing |
| **Recommendation** | ✓ Accept collisions (current impl) | Consider if 50+ tags becomes issue |

**Decision Required**: Keep Option A or redesign to Option B?

**Current Assumption**: Option A (collisions acceptable)

---

## Decision 2: Terminal Color Capability Fallback

| Aspect | Option A: True-Color Only | Option B: Graceful Degradation |
|--------|--------------------------|------------------------------|
| **Design** | Assume all terminals support RGB (24-bit) | Detect capability, map to 256 or 16-color |
| **ANSI Markup** | Use `[#RRGGBB]` hex directly | Use named colors or 256-color codes |
| **Compatibility** | Modern terminals (iTerm2, VSCode, etc.) | Legacy terminals, SSH sessions |
| **Implementation** | 0 lines of code | 20-40 lines capability detection |
| **User Impact** | Colors might not render on old terminals | Colors always render somehow |
| **Performance** | No overhead (no detection needed) | Minimal overhead (one-time check) |
| **Example** | `[#3B82F6]` works on modern Mac/Linux | `[blue]` on 16-color terminal |
| **Maintenance** | Simple | More complex fallback logic |
| **Recommendation** | ✓ Start simple; add fallback if needed | Add if users report color issues |

**Decision Required**: Accept Option A or implement Option B?

**Current Assumption**: Option A (true-color only)

---

## Decision 3: Hash Algorithm Stability

| Aspect | Option A: GetHashCode() | Option B: Deterministic (SHA256) |
|--------|------------------------|--------------------------------|
| **Algorithm** | `Math.Abs(tag.GetHashCode()) % 10` | `SHA256(tag).GetHashCode() % 10` |
| **Stability** | Runtime/platform specific | Guaranteed consistent |
| **Performance** | O(1) - very fast | O(n) - SHA256 computation |
| **Cross-Platform** | Hash might differ on .NET upgrades | Same hash across versions |
| **Risk** | Colors shift if .NET version changes | No risk of color changes |
| **Impact** | User sees `#urgent` change colors after upgrade | Colors never change |
| **Implementation** | 0 lines | ~10 lines (SHA256 wrapper) |
| **Example** | macOS .NET 8 → .NET 9: hash changes | Always consistent across upgrades |
| **Recommendation** | ✓ Simple; accept minor inconsistency | Use if color stability critical |

**Decision Required**: Use Option A or Option B?

**Current Assumption**: Option A (GetHashCode())

---

## Decision 4: Shared Utility Implementation Location

| Aspect | Option A: TaskerCore.Output | Option B: New TaskerCore.Colors.TagColorHelper | Option C: Each Module (Duplicated) |
|--------|---------------------------|---------------------------------------------|----------------------------------|
| **File Location** | `src/TaskerCore/Output.cs` (new section) | `src/TaskerCore/Colors/TagColorHelper.cs` | Output.cs, TuiRenderer.cs, TaskListPopup |
| **Accessibility** | .NET only | Both .NET + Avalonia | Both .NET + Avalonia |
| **Duplication** | 0 copies (central) | 0 copies (central) | 3 copies (maintenance burden) |
| **Integration** | Simple: same file as FormatTags() | Clean: separate concern | Messy: palette change = 3 edits |
| **Future Expansion** | Easy to add color utilities | Very clean for color-related code | Hard to maintain consistency |
| **Import Strategy** | `using TaskerCore;` | `using TaskerCore.Colors;` | `using ...;` (multiple) |
| **Test Location** | `TaskerCore.Tests/Output/` | `TaskerCore.Tests/Colors/TagColorHelperTests.cs` | Test each module separately |
| **Example Usage** | `TagColorHelper.GetTagColor(tag)` | `TagColorHelper.GetTagColor(tag)` | Duplicate code in 3 files |
| **Recommendation** | ✓ Simple, minimal changes | Better structure if more color features | Avoid - maintenance nightmare |

**Decision Required**: Choose A, B, or C?

**Current Assumption**: Option B (new TagColorHelper class in Colors namespace)

---

## Decision 5: Tag Color Behavior for Completed Tasks

| Aspect | Option A: Dimmed (Follow Text) | Option B: Keep Bright (Retain Color) |
|--------|------------------------------|-------------------------------------|
| **Markup** | `[dim][TAG_COLOR]#tag[/][/]` | `[TAG_COLOR]#tag[/]` |
| **Visual Hierarchy** | Completed task colors fade with text | Completed task colors stand out |
| **Readability** | Dimmed colors harder to distinguish | Colors still vibrant |
| **User Expectation** | Tags indicate content even when done | Color prominence doesn't change |
| **Implementation** | Slightly more complex | Simple (no special handling) |
| **Example** | Completed task: gray text + dim blue tag | Completed task: gray text + bright blue tag |
| **Recommendation** | ✓ Maintain visual consistency | Consider if tags important for done items |

**Decision Required**: Choose A or B?

**Current Assumption**: Option A (dimmed colors for completed tasks)

---

## Decision 6: Search Highlighting vs Tag Colors

| Aspect | Option A: Tag Color Priority | Option B: Search Highlight Priority | Option C: Nested Markup |
|--------|---------------------------|--------------------------------|---------------------|
| **Behavior** | Tag color shown; search highlight ignored for tags | Search highlight shown; tag color ignored | `[yellow][#3B82F6]#tag[/][/]` |
| **User Sees** | Colored tag, no highlight | Highlighted tag, no color | Overlapping markup (undefined) |
| **Clarity** | Clear tag color for identification | Clear match indicator | Confusing/broken |
| **Implementation** | Simplest (don't color tags matching search) | Skip coloring for matching tags | Complex + fragile |
| **Example** | Search `urgent` → tag shows blue | Search `urgent` → tag shows yellow highlight | Both colors overlap |
| **Recommendation** | ✓ Simplest and clearest | Could work; requires logic | Avoid - fragile markup |

**Decision Required**: Choose A, B, or C?

**Current Assumption**: Option A (tag color takes precedence)

---

## Decision 7: Color-Blind Accessibility Support

| Aspect | Option A: No Special Support | Option B: High-Contrast Palette | Option C: Config Option |
|--------|---------------------------|------------------------------|-----------------------|
| **Support** | Use standard palette only | Provide alternative palette | User can choose palette |
| **Cost** | 0 effort | Design new palette (~2 hours) | Design palette + settings (~4 hours) |
| **WCAG Compliance** | Need to verify contrast ratios | Explicitly accessible | Accessible by design |
| **User Impact** | Color-blind users rely on text (okay) | All users can distinguish colors | Users can customize |
| **Complexity** | Simple | Medium | High |
| **Recommendation** | ✓ Start here; add if needed | Add if color-blind users report issues | Only if color becomes critical |

**Decision Required**: Choose A, B, or C?

**Current Assumption**: Option A (no special support; text is primary identifier)

---

## Decision 8: User Configuration / Theming

| Aspect | Option A: Fixed Palette | Option B: Theme-Based Palette | Option C: User-Configurable |
|--------|----------------------|-------------------------------|--------------------------|
| **Design** | Same 10 colors always | Dark theme colors vs light theme colors | Users customize palette in config |
| **Effort** | Minimal | Medium (design 2 palettes) | High (settings infrastructure) |
| **Maintenance** | Simple | Medium (keep palettes in sync) | Complex (validate user input) |
| **User Control** | None | Automatic per theme | Full control |
| **Example** | Blue always `#3B82F6` | Dark: `#3B82F6`, Light: `#1E40AF` | User edits colors in config.json |
| **Recommendation** | ✓ Start here | Add if theming feature added | Only if user requests |

**Decision Required**: Choose A, B, or C?

**Current Assumption**: Option A (fixed palette)

---

## Decision 9: Real-Time Color Preview During Inline Edit

| Aspect | Option A: Colors After Save | Option B: Live Preview While Typing |
|--------|---------------------------|-------------------------------------|
| **Behavior** | Edit task → Save → Colors appear | Edit task → Type `#newtag` → Color appears immediately |
| **Implementation** | Current approach; no changes | Must parse & color in real-time |
| **Effort** | 0 extra effort | ~20-30 lines (editor + parser call) |
| **User Experience** | Confirm first, see colors on list | Immediate visual feedback |
| **Risk** | No issues | Complex state management |
| **Example** | Type new tag → save → color appears | Type new tag → color previews in editor |
| **Recommendation** | ✓ Start simple; add if needed | Consider for nice-to-have |

**Decision Required**: Choose A or B?

**Current Assumption**: Option A (colors after save)

---

## Decision 10: Palette Size vs Collision Risk

| Size | Colors | ~Max Unique Tags (avoid collisions) | Collision Probability (50 tags) |
|-----|--------|-----------------------------------|---------------------------------|
| 10 | Small, curated | ~10-15 | High (60%+) |
| 20 | Medium | ~25-30 | Medium (20%) |
| 30 | Large | ~40-50 | Low (5%) |
| 100+ | Huge | 100+ | Minimal | Complex |

**Current Palette**: 10 colors (from TaskerTray)

**Question**: If users frequently have 50+ tags, is collision rate acceptable?

**Options**:
- A: Keep 10, accept collisions (current)
- B: Expand to 20 colors (moderate effort)
- C: Expand to 30+ colors (high effort, diminishing returns)

**Recommendation**: ✓ Option A (start with 10; expand if users report issues)

---

## Summary: Recommended Configuration

If all questions answered per recommendations:

```
┌─────────────────────────────────────────────────────────┐
│ RECOMMENDED CONFIGURATION (Low Risk, Low Effort)        │
├─────────────────────────────────────────────────────────┤
│ Collision Handling    │ Accept collisions (10 colors)   │
│ Terminal Fallback     │ True-color only                 │
│ Hash Algorithm        │ GetHashCode() as-is             │
│ Shared Utility        │ New TagColorHelper class        │
│ Completed Tasks       │ Dimmed colors follow text       │
│ Search Highlighting   │ Tag color priority              │
│ Color-Blind Support   │ No special support              │
│ User Configuration    │ Fixed palette                   │
│ Real-Time Preview     │ Colors appear after save        │
│ Palette Size          │ 10 colors (from TaskerTray)     │
├─────────────────────────────────────────────────────────┤
│ Estimated Effort: 4 hours code + 1-2 hours testing      │
│ Risk Level: Low (no major architectural changes)        │
│ Breaking Changes: None (backward compatible)            │
│ User Impact: Positive (better visual distinction)       │
└─────────────────────────────────────────────────────────┘
```

---

## Alternative Configurations

### Configuration B: Maximum Accessibility (High Effort)

```
┌─────────────────────────────────────────────────────────┐
│ If color accuracy and accessibility are critical:       │
├─────────────────────────────────────────────────────────┤
│ Collision Handling    │ Prevent (expand to 30 colors)   │
│ Terminal Fallback     │ Graceful degradation            │
│ Hash Algorithm        │ Deterministic (SHA256)          │
│ Shared Utility        │ TagColorHelper + utils          │
│ Completed Tasks       │ Bright (color importance)       │
│ Search Highlighting   │ Smart overlap detection         │
│ Color-Blind Support   │ High-contrast palette option    │
│ User Configuration    │ Config-based palette choice     │
│ Real-Time Preview     │ Live color preview              │
│ Palette Size          │ 30 colors (no collisions)       │
├─────────────────────────────────────────────────────────┤
│ Estimated Effort: 12-16 hours code + testing            │
│ Risk Level: Medium (complex state management)           │
│ Breaking Changes: None                                  │
│ User Impact: Excellent (maximum flexibility)            │
└─────────────────────────────────────────────────────────┘
```

### Configuration C: Minimal Implementation (Ultra-Low Effort)

```
┌─────────────────────────────────────────────────────────┐
│ If absolute minimal effort:                             │
├─────────────────────────────────────────────────────────┤
│ Collision Handling    │ Accept (10 colors)              │
│ Terminal Fallback     │ True-color only                 │
│ Hash Algorithm        │ GetHashCode()                   │
│ Shared Utility        │ Inline in Output.cs             │
│ Completed Tasks       │ Normal colors (no dimming)      │
│ Search Highlighting   │ Simple (tag color only)         │
│ Color-Blind Support   │ None                            │
│ User Configuration    │ None                            │
│ Real-Time Preview     │ No                              │
│ Palette Size          │ 10 colors                       │
├─────────────────────────────────────────────────────────┤
│ Estimated Effort: 2-3 hours code + minimal testing      │
│ Risk Level: Ultra-low (minimal code changes)            │
│ Breaking Changes: None                                  │
│ User Impact: Okay (works but less flexible)             │
└─────────────────────────────────────────────────────────┘
```

---

## Decision Template

For each question, answer YES to accept Recommended Configuration:

- [ ] **Q1**: Is accepting ~15-20 tag collisions with 50+ tags acceptable?
- [ ] **Q2**: Is assuming true-color terminal support adequate (no fallback needed)?
- [ ] **Q3**: Is `GetHashCode()` acceptable or need deterministic hash?
- [ ] **Q4**: Is new TagColorHelper class the right location?
- [ ] **Q5**: Should completed task tags be dimmed?
- [ ] **Q6**: Should tag color take priority over search highlighting?
- [ ] **Q7**: Is accessibility sufficient via text-based tagging (no color-blind mode)?
- [ ] **Q8**: Is fixed palette adequate (no user configuration)?
- [ ] **Q9**: Should colors only appear post-save (no real-time preview)?
- [ ] **Q10**: Is 10-color palette sufficient?

**If all YES**: Proceed with Recommended Configuration (4 hours, low risk)

**If any NO**: Identify which Configuration (B or C) aligns with requirements

---

## Approval Checklist

- [ ] Product/Design approves recommended configuration
- [ ] No accessibility compliance issues with color choices
- [ ] Team agrees on hash algorithm (GetHashCode vs SHA256)
- [ ] Shared utility location confirmed (TaskerCore.Colors)
- [ ] Terminal support requirements clarified
- [ ] Definition of done (acceptance criteria) documented

---

**Next Action**: Get YES/NO answers to the 10 decisions above to unlock implementation.
