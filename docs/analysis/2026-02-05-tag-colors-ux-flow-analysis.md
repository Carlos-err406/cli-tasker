# UX Flow Analysis: Consistent Tag Colors Feature

**Date**: 2026-02-05
**Feature**: Hash-based consistent tag colors across CLI, TUI, and TaskerTray
**Status**: Specification & Flow Analysis
**Analyst**: Claude Code - UX Flow Analyst

---

## Executive Summary

This analysis identifies **11 distinct user flows** affected by consistent tag coloring, **18 critical edge cases**, and **12 specification gaps** requiring clarification before implementation. The feature appears straightforward (apply hash-to-palette to CLI/TUI matching TaskerTray), but the details reveal subtle challenges around rendering contexts, accessibility, platform differences, and error handling.

**Key Finding**: The current specification assumes deterministic hashing, but doesn't define behavior for:
- Rendering context incompatibilities (ANSI colors vs Avalonia colors)
- Platform-specific color limitations (terminal capabilities)
- Accessibility requirements (color-blind users)
- Migration of existing cyan-formatted tasks

---

## Part 1: User Flow Overview

### Flow Diagram

```
User creates/views tasks with tags
    │
    ├─► CLI (list command)
    │   └─► FormatTags() [Output.cs]
    │       └─► Currently: [cyan] for ALL tags
    │       └─► Desired: Hash-based per-tag color
    │
    ├─► TUI (interactive)
    │   └─► FormatTags() [TuiRenderer.cs]
    │       └─► Currently: [cyan] for ALL tags
    │       └─► Desired: Hash-based per-tag color
    │
    └─► TaskerTray (Avalonia UI)
        └─► GetTagColor() [TaskListPopup.axaml.cs]
            └─► Already: Hash-based palette (10 colors)
            └─► Status: Baseline implementation to match
```

---

## Part 2: Detailed User Flows (11 Flows)

### Flow 1: Add Task with Tags (CLI Entry)

**Entry Point**: `tasker add "Task description #tag1 #tag2" -l work`

**Steps**:
1. User types task with embedded tags
2. TaskDescriptionParser extracts tags (preserves order)
3. TodoTask created with `Tags = ["tag1", "tag2"]`
4. Task saved to storage
5. Tags stored as string array in JSON

**Affected Systems**: Storage, task model (no rendering yet)

**Expected Behavior**: Tags persisted identically, no color assignment yet (colors only appear on display)

**Return Value**: Success message, no visual tag preview in CLI add confirmation

---

### Flow 2: View Tasks - CLI List Command

**Entry Point**: `tasker list` or `tasker list -l work`

**Current Behavior**:
```
(abc) [x] - Task with tags  [cyan]#tag1 #tag2[/]
```

**Desired Behavior**:
```
(abc) [x] - Task with tags  [TAG1_COLOR]#tag1[/] [TAG2_COLOR]#tag2[/]
```

**Steps**:
1. ListCommand retrieves tasks (filtered by list if `-l` provided)
2. For each task, calls `Output.FormatTags(task.Tags)`
3. Current: Returns `"  [cyan]#{tags}[/]"`
4. Desired: Returns `"  [color1]#tag1[/] [color2]#tag2[/]"` (per-tag coloring)
5. Output rendered to terminal via AnsiConsole

**Data Flow**:
```
TodoTask.Tags (string[])
    ↓
Output.FormatTags(tags)
    ├─► For each tag: GetTagColor(tag) → ANSI hex
    └─► Build markup string
```

**Terminal Constraints**:
- Must use ANSI color codes (Spectre.Console markup)
- Colors rendered as `[#RRGGBB]text[/]` or standard named colors
- Terminal may not support all colors (limited palette on older terminals)

**Related Code**:
- `/Users/carlos/self-development/cli-tasker/Output.cs` (lines 33-38)
- `/Users/carlos/self-development/cli-tasker/AppCommands/ListCommand.cs` (uses `Output.FormatTags()`)

---

### Flow 3: View Tasks - TUI Renderer

**Entry Point**: `tasker tui`

**Current Behavior**:
```
  (abc) [x] [ ]  Task with tags  [cyan]#tag1 #tag2[/]
```

**Desired Behavior**:
```
  (abc) [x] [ ]  Task with tags  [TAG1_COLOR]#tag1[/] [TAG2_COLOR]#tag2[/]
```

**Steps**:
1. TuiRenderer.RenderTask() called for each task
2. Calls `FormatTags(task.Tags)`
3. Current: Returns `"  [cyan]#{tags}[/]"`
4. Desired: Returns per-tag colored output
5. Appended to task line in RenderTask()

**Data Flow**:
```
TodoTask.Tags (string[])
    ↓
TuiRenderer.FormatTags(tags)
    ├─► For each tag: GetTagColor(tag) → ANSI hex
    └─► Build markup string
```

**Rendering Context**:
- ANSI terminal with Spectre.Console
- Same color constraints as CLI
- TUI can switch lists, search, multiselect—colors must be consistent across all views

**Related Code**:
- `/Users/carlos/self-development/cli-tasker/Tui/TuiRenderer.cs` (lines ~170+)
- `FormatTags()` method (private static)

---

### Flow 4: View Tasks - TaskerTray Popup

**Entry Point**: User clicks menu bar icon

**Current Behavior**:
```
Task item with tag pills
  Background: [GetTagColor(tag)] (10-color hash palette)
  Text: white
```

**Desired Behavior**: No change (already implemented correctly)

**Steps**:
1. TaskListPopup.RefreshTasks() loads all tasks
2. For each task, CreateTaskItem() creates UI elements
3. Tag pills created in loop (lines 1200-1220)
4. Each tag gets `Background = new SolidColorBrush(GetTagColor(tag))`
5. GetTagColor() implements hash-based palette (lines 1544-1563)

**Hash Algorithm**:
```csharp
var index = Math.Abs(tag.GetHashCode()) % colors.Length;
return Color.Parse(colors[index]);
```

**Color Palette** (10 colors, TaskerTray):
- Blue: `#3B82F6`
- Emerald: `#10B981`
- Amber: `#F59E0B`
- Red: `#EF4444`
- Violet: `#8B5CF6`
- Pink: `#EC4899`
- Cyan: `#06B6D4`
- Lime: `#84CC16`
- Orange: `#F97316`
- Indigo: `#6366F1`

**Related Code**:
- `/Users/carlos/self-development/cli-tasker/src/TaskerTray/Views/TaskListPopup.axaml.cs` (lines 1544-1563)

---

### Flow 5: View Task Details - CLI Get Command

**Entry Point**: `tasker get abc123`

**Current Behavior**:
```
...
Tags:          #tag1 #tag2 (all cyan)
...
```

**Desired Behavior**:
```
...
Tags:          [TAG1_COLOR]#tag1[/] [TAG2_COLOR]#tag2[/]
...
```

**Steps**:
1. GetCommand retrieves single task by ID
2. Calls `Output.FormatTags(task.Tags)`
3. Displays in details section

**Related Code**:
- `/Users/carlos/self-development/cli-tasker/AppCommands/GetCommand.cs`
- Uses same `Output.FormatTags()` as Flow 2

---

### Flow 6: Add Task with Tags - TUI Inline Add

**Entry Point**: TUI interactive mode, press `a` for new task

**Current Behavior**:
1. Input textbox appears
2. User types: `Task #tag1 #tag2`
3. Parser extracts tags
4. Task added, TUI refreshes
5. Tags show in cyan

**Desired Behavior**:
1. Same as above
2. Tags now show with consistent colors

**Data Flow**: Same as Flow 1 (storage level) + Flow 3 (rendering)

---

### Flow 7: Add Task with Tags - TaskerTray Inline Add

**Entry Point**: TaskerTray popup, click `+` button on list header

**Current Behavior**:
1. Inline text field appears
2. User types: `Task #tag1 #tag2`
3. Parser extracts tags
4. Task saved
5. Popup refreshes
6. Tags show with consistent hash-based colors

**Desired Behavior**: No change (already implemented)

**Data Flow**: Flow 1 (storage) + Flow 4 (rendering)

---

### Flow 8: Search/Filter by Tags - TUI Search Mode

**Entry Point**: TUI search mode `/#tag1`

**Current Behavior**:
1. User types search query with tag
2. Tasks matching tag are filtered
3. Matching tasks display with cyan tags
4. Non-matching tags still cyan

**Desired Behavior**:
1. Same filtering
2. Tags now show with consistent colors
3. Search highlighting still works (overlays color)

**Questions**: How does search highlighting interact with per-tag colors? See gaps section.

---

### Flow 9: Edit Task Tags - CLI Rename

**Entry Point**: `tasker rename abc "New title #newtag"`

**Current Behavior**:
1. Task description updated via Rename()
2. Parser re-extracts tags from new description
3. Tags updated in TodoTask
4. Task re-inserted at top
5. List command shows with cyan tags

**Desired Behavior**:
1. Same as above
2. New tags show with their assigned colors

---

### Flow 10: Edit Task Tags - TUI Inline Edit

**Entry Point**: TUI interactive mode, select task and press `e` for edit

**Current Behavior**:
1. Inline text editor shows current description
2. User modifies tags
3. Parser re-extracts tags
4. Task renamed, TUI refreshes
5. Tags show in cyan

**Desired Behavior**: Tags show with consistent colors

---

### Flow 11: Edit Task Tags - TaskerTray Inline Edit

**Entry Point**: TaskerTray popup, right-click task → Edit

**Current Behavior**:
1. Inline text editor opens
2. User modifies description/tags
3. Tags re-extracted
4. Task saved, popup refreshes
5. Tags display with hash-based colors

**Desired Behavior**: No change (already implemented)

---

## Part 3: Flow Permutations Matrix

| Flow | User Type | Device | Context | Platform Constraint |
|------|-----------|--------|---------|-------------------|
| 1-5 (Add/View) | CLI user | Terminal | First-time task | ANSI color support |
| 1-5 | CLI user | Terminal | Returning user | ANSI color support |
| 2-3 (List view) | CLI user | Terminal | Terminal 16-color only | Degradation needed |
| 2-3 | CLI user | Terminal | Terminal 256-color | Full support |
| 2-3 | CLI user | Terminal | Terminal true-color | Full support |
| 4 | Power user | macOS | TaskerTray popup | Avalonia color support |
| 4 | Power user | macOS | Dragging/reordering | Same-render consistency |
| 6 | CLI user | Terminal | TUI inline add | ANSI color support |
| 7 | Power user | macOS | TaskerTray inline add | Avalonia color support |
| 8 | CLI power user | Terminal | Search active | Color + highlight overlap |
| 9-11 | Any user | Any | Editing tags | Color consistency during edit |

---

## Part 4: Missing Elements & Gaps

### Category 1: Color Mapping & Translation

**Gap 1.1**: ANSI vs Avalonia Color Models
- **Problem**: TaskerTray uses Avalonia `Color` objects; CLI/TUI use ANSI escape codes
- **Current**: TaskerTray palette is 10 RGB hex colors (#3B82F6, etc.)
- **Question**: Should CLI/TUI use identical hex colors via Spectre markup `[#RRGGBB]`, or map to standard ANSI named colors?
- **Impact**: Affects color consistency perception. Hex colors render accurately but may not display in 16-color terminals
- **Assumption**: Will use hex colors in markup, terminal will do best-effort rendering

**Gap 1.2**: Terminal Color Capability Detection
- **Problem**: Not all terminals support true color (24-bit RGB)
- **Current**: No detection; code assumes support
- **Question**: Should the app detect terminal capabilities and gracefully degrade to named colors or 256-color palette?
- **Example**: Older terminals might render `[#3B82F6]` as closest named color or escape code fail silently
- **Impact**: Colors might not render as intended on constrained environments
- **Assumption**: Will assume terminal supports true color; degradation left to terminal/Spectre.Console

**Gap 1.3**: Hash Collision Tolerance
- **Problem**: Different tags might hash to same color (low probability with 10-color palette)
- **Question**: Is visual collision acceptable, or should we ensure unique colors for frequently-used tags?
- **Example**: If user has 50 unique tags, collisions are guaranteed
- **Impact**: Loss of one-tag-to-one-color invariant
- **Assumption**: Collisions acceptable; users can distinguish by tag text

---

### Category 2: Edge Cases - Tag Content

**Gap 2.1**: Empty Tag Strings
- **Problem**: Parser could theoretically return empty tag `""`
- **Current**: Unknown if possible; parser behavior unspecified
- **Question**: Can tasks have empty tags? What should `GetTagColor("")` return?
- **Impact**: If possible, crashes or renders empty colored pill in TaskerTray
- **Assumption**: Parser prevents empty tags; no validation needed in color function

**Gap 2.2**: Unicode Tags
- **Problem**: Tags might contain emoji, non-Latin scripts, combining characters
- **Example**: Tags like `#日本語`, `#emoji👀`, `#café`
- **Question**: Does `string.GetHashCode()` handle Unicode consistently across platforms?
- **Impact**: Hash might differ on .NET Core vs Mono; emoji might render differently
- **Assumption**: Hash is consistent per platform; emoji render as-is with theme color

**Gap 2.3**: Very Long Tag Names
- **Problem**: No max length enforced on tags
- **Example**: `#verylongtagnamethatdoesnotfitonthedisplay`
- **Question**: How should long tags render in TUI (constrained width) and TaskerTray (pill width)?
- **Impact**: Text might overflow, be truncated, or wrap
- **Assumption**: No truncation; rendered as-is; UI framework handles text wrapping/clipping

**Gap 2.4**: Special Characters in Tags
- **Problem**: Parser might allow special characters (# is removed, but what about symbols?)
- **Example**: Tags like `#c++`, `#tag-with-dashes`, `#tag_with_underscores`
- **Question**: Parser spec unclear on allowed characters
- **Impact**: Escaping issues in Spectre.Console markup
- **Assumption**: Tags contain only alphanumerics, dashes, underscores; no Spectre escaping needed

**Gap 2.5**: Case Sensitivity
- **Problem**: Should `#tag` and `#TAG` be colored the same?
- **Question**: Is hash based on original casing or normalized?
- **Example**: User types `#urgent` vs `#Urgent`—different hash or same?
- **Impact**: Consistency perception; if different colors, confusing
- **Assumption**: Hash is case-sensitive (current behavior); `#tag` and `#TAG` get different colors

---

### Category 3: Rendering & Display Context

**Gap 3.1**: Search Result Highlighting + Tag Colors
- **Problem**: TUI search mode highlights matches
- **Current**: Search highlighting uses `[yellow]` markup
- **Question**: How should highlighting interact with per-tag colors?
- **Example**: Search for `tag`, which appears in description; tag pill also shows
- **Scenario A**: Tag color overrides search highlight
- **Scenario B**: Search highlight overrides tag color
- **Scenario C**: Conflict resolution undefined
- **Impact**: Visual inconsistency in search mode
- **Assumption**: Tag color takes precedence; search highlight only on text matches, not tag pills

**Gap 3.2**: Multi-Select Highlighting + Tag Colors
- **Problem**: TUI multi-select mode shows selection indicator
- **Current**: Selected tasks show bold/highlight
- **Question**: Should selected task's tag colors be brightened, dimmed, or unchanged?
- **Impact**: Readability of selected tasks vs colors
- **Assumption**: Tag colors unchanged; selection indicated by task background/text, not tag color

**Gap 3.3**: Checked/Completed Task Display
- **Problem**: Checked tasks shown strikethrough/dimmed in CLI and TUI
- **Current**: `[dim strikethrough]` or `[dim]`
- **Question**: Should tag colors be dimmed for checked tasks?
- **Example**: Completed task with `#urgent` tag—should tag still be red, or dim red?
- **Impact**: Visual weight of completed task
- **Assumption**: Tags follow same dim styling as task text; e.g., `[dim][TAG_COLOR]#tag[/][/]`

**Gap 3.4**: Dark Terminal vs Light Terminal
- **Problem**: Terminal background color affects color visibility
- **Current**: Code assumes dark background (color choices muted but visible)
- **Question**: Should colors be adjusted for light themes?
- **Example**: Blue `#3B82F6` on white background is hard to read
- **Impact**: Accessibility on light-theme terminals
- **Assumption**: Assume dark terminal; light theme users must adjust their terminal colors

---

### Category 4: Data Persistence & Migration

**Gap 4.1**: No Backward Compatibility Specification
- **Problem**: Existing tasks have no color metadata; storage format is text-only
- **Current**: Colors are computed on-the-fly from hash
- **Question**: If hash algorithm changes, should colors re-compute or persist?
- **Impact**: Consistency over time; if algorithm tweaked, colors might shift
- **Assumption**: Colors always recomputed from hash; no persistence needed

**Gap 4.2**: Sync & Export Implications
- **Problem**: Color is not stored, only computed
- **Current**: JSON storage doesn't include color data
- **Question**: If tasks exported to another tool or synced, colors preserved?
- **Impact**: Colors tied to this app's implementation; exported tasks lose coloring
- **Assumption**: Colors are UI-layer only; not part of data model or export

---

### Category 5: Palette & Design Specification

**Gap 5.1**: Palette Justification
- **Problem**: 10-color palette chosen, but no spec on why 10 or why these colors
- **Current**: TaskerTray has hardcoded palette
- **Question**: Are these colors intentional for accessibility (WCAG contrast), theme compatibility, or arbitrary?
- **Impact**: If colors don't meet accessibility standards, tags might be inaccessible
- **Assumption**: Palette is accessible; colors chosen for visibility on dark backgrounds

**Gap 5.2**: Consistent Palette Across Platforms
- **Problem**: Avalonia color parsing and ANSI rendering might produce different visuals
- **Current**: TaskerTray uses RGB hex; CLI will use `[#RRGGBB]` markup
- **Question**: Will `Color.Parse("#3B82F6")` in Avalonia produce identical pixel color to ANSI terminal rendering of same hex?
- **Impact**: Perceived color inconsistency across UI
- **Assumption**: Hex colors provide close-enough consistency; minor rendering differences acceptable

**Gap 5.3**: Color Accessibility (Color-Blind Users)
- **Problem**: Not all users can distinguish all colors
- **Current**: No specification for color-blind mode
- **Question**: Should the app support color-blind palettes (e.g., high contrast, deuteranopia)?
- **Impact**: Some users might not distinguish tags
- **Assumption**: No color-blind support required; tags are primary identifier, color is secondary

---

### Category 6: Implementation & Integration

**Gap 6.1**: Hash Function Definition
- **Problem**: `string.GetHashCode()` is implementation-specific
- **Current**: Code uses `Math.Abs(tag.GetHashCode()) % colors.Length`
- **Question**: Should hash be documented or standardized? (e.g., SHA256 for stability)
- **Impact**: If .NET version changes, hash might differ
- **Assumption**: `GetHashCode()` is stable per platform/runtime; no need for custom hash

**Gap 6.2**: Shared Utility Function Location
- **Problem**: GetTagColor() needs to exist in three places: Output.cs, TuiRenderer.cs, TaskListPopup.axaml.cs
- **Current**: Only in TaskListPopup.axaml.cs
- **Question**: Where should canonical implementation live?
- **Options**:
  - Duplicate in each module (maintenance burden)
  - Create shared `ColorHelper` class in TaskerCore
  - Create separate `TagColorHelper` in Output.cs
- **Impact**: Code duplication or tight coupling
- **Assumption**: Will create shared utility class in TaskerCore.Output or similar

**Gap 6.3**: Configuration of Palette
- **Problem**: Palette is hardcoded
- **Current**: 10-color array in GetTagColor()
- **Question**: Should palette be configurable? (user preference, theme)
- **Impact**: Flexibility vs complexity
- **Assumption**: Hardcoded palette; no user configuration needed

---

### Category 7: Acceptance & Testing

**Gap 7.1**: No Acceptance Criteria Specified
- **Problem**: Missing clear definition of "done"
- **Current**: Feature description vague
- **Question**: What specific behaviors must work?
- **Acceptance Criteria Should Include**:
  - Same tag always gets same color in all three UIs
  - Colors render without error on major terminal types
  - No regression in existing CLI/TUI functionality
  - Performance not impacted (color lookup is O(1))
- **Assumption**: Criteria defined in implementation planning phase

**Gap 7.2**: No Test Strategy for Cross-Platform Colors
- **Problem**: Testing color output is difficult (pixel comparison? escape codes?)
- **Current**: No tag color tests
- **Question**: How to verify colors render correctly?
- **Options**:
  - Unit tests: Verify hash → color mapping is consistent
  - Integration: Snapshot terminal output with color escapes
  - Manual: Visual inspection across terminal types
- **Assumption**: Unit tests for hash consistency; manual testing for visual verification

---

## Part 5: Critical Questions Requiring Clarification

### Priority 1: Critical (Blocks Implementation)

**Q1**: Hash Collision Handling
- **Question**: With 10 colors and potentially 50+ unique tags, hash collisions are guaranteed. Is this acceptable, or should palette be expanded or hash algorithm changed?
- **Why It Matters**: Determines if feature meets acceptance criteria (1:1 tag-to-color mapping)
- **Impact**: If unacceptable, must redesign palette/hash
- **Assumption If Unanswered**: Collisions acceptable; tags distinguished by text

**Q2**: ANSI Color Downgrade Path
- **Question**: Should the code detect limited-color terminals and downgrade gracefully (e.g., map to 16 named colors or remove colors entirely)?
- **Why It Matters**: Feature might break on legacy systems
- **Impact**: If yes, adds complexity; if no, sets minimum terminal requirement
- **Assumption If Unanswered**: Assume true-color terminal support; no fallback needed

**Q3**: Hash Algorithm Stability
- **Question**: Should `string.GetHashCode()` be replaced with deterministic hash (SHA256, etc.) for consistency across runtime versions?
- **Why It Matters**: Hash might change on .NET version upgrade
- **Impact**: If yes, colors could shift unexpectedly; if no, risk of inconsistency
- **Assumption If Unanswered**: Use `GetHashCode()`; accept minor inconsistencies across versions

**Q4**: Shared Implementation Location
- **Question**: Should GetTagColor() be in a shared utility class (TaskerCore.Output), or duplicated in Output.cs, TuiRenderer.cs, and TaskListPopup.axaml.cs?
- **Why It Matters**: Affects code maintainability and palette consistency
- **Impact**: If shared, must be accessible from both .NET and Avalonia; if duplicated, palette changes must be made in 3 places
- **Assumption If Unanswered**: Create shared TagColorHelper class in TaskerCore

---

### Priority 2: Important (Significantly Affects UX/Maintainability)

**Q5**: Tag Color Behavior During Edit
- **Question**: When editing a task's tags, should new tags immediately show their colors before save?
- **Example**: User types `#newtag` in TUI inline editor—does color preview appear?
- **Why It Matters**: Affects UX feedback loop
- **Impact**: If yes, must parse tags in real-time during edit; if no, colors only appear post-save
- **Assumption If Unanswered**: Colors only appear after save

**Q6**: Search Highlighting Interaction
- **Question**: In TUI search mode, if a tag matches the search query, should it show both search highlight and tag color?
- **Scenario**: Search for `#important` → results show `#important` tag with color; should it also be highlighted?
- **Why It Matters**: Visual clarity in search mode
- **Impact**: Changes markup structure; could cause markup conflicts
- **Assumption If Unanswered**: Tag color takes precedence; no search highlight on tags

**Q7**: Checked Task Tag Coloring
- **Question**: Should tag colors be dimmed for checked/completed tasks?
- **Example**: Completed task with `#urgent` (red) tag—red or gray?
- **Why It Matters**: Visual hierarchy of completed items
- **Impact**: If dimmed, requires additional markup logic; if not, completed task colors stand out
- **Assumption If Unanswered**: Tags follow same dim styling as task text

**Q8**: Palette Accessibility
- **Question**: Do the 10 chosen colors meet WCAG AA contrast requirements on dark backgrounds?
- **Why It Matters**: Accessibility compliance
- **Impact**: If no, palette must be redesigned
- **Assumption If Unanswered**: Assume colors are accessible; no changes needed

**Q9**: Consistent Hash Across Platforms
- **Question**: Should the app guarantee that `tag.GetHashCode()` returns identical value on macOS, Linux, Windows, and across .NET versions?
- **Why It Matters**: If hash differs, colors shift between platforms
- **Impact**: If yes, must implement custom deterministic hash (SHA256); if no, accept platform variation
- **Assumption If Unanswered**: Platform variation acceptable; use `GetHashCode()`

---

### Priority 3: Nice-to-Have (Improves Clarity)

**Q10**: User Documentation for Tag Colors
- **Question**: Should documentation explain why tag colors are consistent/persistent?
- **Why It Matters**: Users might not understand color logic
- **Impact**: Educational; no implementation impact
- **Assumption If Unanswered**: No documentation needed; behavior obvious to users

**Q11**: Configuration/Theme Support
- **Question**: Should tag colors be user-configurable or tied to theme?
- **Example**: Light theme uses different palette than dark theme
- **Why It Matters**: Flexibility and personalization
- **Impact**: If yes, adds settings infrastructure; if no, fixed palette
- **Assumption If Unanswered**: Fixed palette; no configuration

**Q12**: Performance Implications
- **Question**: Is `GetHashCode()` called frequently enough to be a performance concern?
- **Why It Matters**: Rendering performance
- **Impact**: Negligible (O(1) lookup); unlikely to be bottleneck
- **Assumption If Unanswered**: No performance concerns; no caching needed

---

## Part 6: Recommended Next Steps

### Immediate Actions (Before Code Implementation)

1. **Define Acceptance Criteria**
   - Clarify Q1-Q3 above (critical blockers)
   - Document specific test scenarios for each flow
   - Define success metrics (e.g., "colors match across all UIs")

2. **Palette & Color Verification**
   - Answer Q8: Verify WCAG contrast ratios for all 10 colors on dark background
   - Document color choices and accessibility rationale
   - Test visual rendering on actual terminals (iTerm2, Terminal.app, VSCode Terminal, etc.)

3. **Hash Algorithm Decision**
   - Answer Q3 & Q9: Decide on deterministic hash or platform-specific hash
   - If deterministic, implement custom hash function
   - Document stability contract

4. **Shared Utility Design**
   - Answer Q4: Design shared TagColorHelper class
   - Determine if it lives in TaskerCore, Output.cs, or separate module
   - Ensure accessibility from both .NET and Avalonia projects

### Implementation Phase

5. **Create Shared Utility**
   ```csharp
   // Location: TaskerCore/Colors/TagColorHelper.cs or Output.cs
   public static class TagColorHelper
   {
       // Palette definition
       private static readonly string[] ColorPalette = { /* ... */ };

       // Main method
       public static string GetTagColor(string tag)
       {
           var index = Math.Abs(tag.GetHashCode()) % ColorPalette.Length;
           return ColorPalette[index];
       }

       // For CLI/TUI: Return Spectre markup
       public static string GetTagColorMarkup(string tag)
       {
           var color = GetTagColor(tag);
           return $"[{color}]#{tag}[/]";
       }

       // For Avalonia: Return Brush/Color
       public static Color GetTagColorAvalonia(string tag)
       {
           var colorHex = GetTagColor(tag);
           return Color.Parse(colorHex);
       }
   }
   ```

6. **Update Output.FormatTags()**
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

7. **Update TuiRenderer.FormatTags()**
   - Same pattern as Output.FormatTags()
   - Use TagColorHelper.GetTagColorMarkup()

8. **Update TaskListPopup.GetTagColor()**
   - Replace with call to TagColorHelper.GetTagColorAvalonia()
   - Or refactor to share implementation

9. **Add Unit Tests**
   ```csharp
   // Test: Same tag always returns same color
   [Fact]
   public void SameTag_SameColor_Consistent()
   {
       var color1 = TagColorHelper.GetTagColor("urgent");
       var color2 = TagColorHelper.GetTagColor("urgent");
       Assert.Equal(color1, color2);
   }

   // Test: Different tags likely different colors
   [Fact]
   public void DifferentTags_ExpectDifferentColors()
   {
       var colors = new[] { "urgent", "feature", "bug", "docs", "test" }
           .Select(t => TagColorHelper.GetTagColor(t))
           .Distinct()
           .Count();
       Assert.True(colors > 1, "Expected variation in colors");
   }

   // Test: Color in valid palette
   [Fact]
   public void ColorInPalette_Valid()
   {
       var color = TagColorHelper.GetTagColor("random");
       Assert.Contains(color, TagColorHelper.ColorPalette);
   }
   ```

10. **Manual Testing Checklist**
    - [ ] CLI: `tasker list` displays colors on true-color terminal
    - [ ] CLI: `tasker list` degrades gracefully on 16-color terminal (if supported)
    - [ ] TUI: Colors display correctly in interactive mode
    - [ ] TUI: Colors consistent when searching, filtering, selecting
    - [ ] TaskerTray: Colors unchanged (baseline)
    - [ ] Cross-platform: Colors same on macOS, Linux, Windows (if testing all)
    - [ ] Accessibility: All colors readable on dark background
    - [ ] Edge cases: Empty tags, very long tags, unicode tags handled

---

## Part 7: Edge Case Scenarios to Test

### Rendering Edge Cases

1. **Empty Task Description with Tags Only**
   - Task: `#tag1 #tag2` (no description)
   - Expectation: Tags display, no crash

2. **Single Character Tags**
   - Task: `a #x #y #z`
   - Expectation: Single-char tags color correctly

3. **Duplicate Tags**
   - Task: `Task #tag #tag #tag`
   - Expectation: All instances same color; no duplication issues

4. **Mixed Case Tags**
   - Task: `Task #Tag #TAG #tag`
   - Expectation: Different hashes, potentially different colors (unless normalized)

5. **Numeric Tags**
   - Task: `Task #123 #456`
   - Expectation: Numeric tags color correctly

6. **Special Character Tags** (if parser allows)
   - Task: `Task #tag-name #tag_name #tag.name`
   - Expectation: No escaping errors in markup

### Terminal/Platform Edge Cases

7. **16-Color Terminal**
   - Expectation: Colors degrade or fail gracefully (depends on Q2 answer)

8. **Monochrome Terminal (no color)**
   - Expectation: Tags still render, no color (if supported)

9. **Very Wide Terminal**
   - Task with 10+ tags
   - Expectation: All tags visible and colored (no wrapping issues)

10. **Very Narrow Terminal (80 chars)**
    - Task with 10+ tags
    - Expectation: Tags wrap or truncate gracefully

### State & Context Edge Cases

11. **Checked Task with Colored Tags**
    - Task completed, then list displayed
    - Expectation: Tags still visible and colored (or dimmed per Q7)

12. **Edited Task, Tags Changed**
    - Add task with `#old`, then rename to `#new`
    - Expectation: New tag gets its color; old color forgotten

13. **Multiple Lists with Same Tags**
    - List "work" has task with `#important`
    - List "personal" has task with `#important`
    - Expectation: Both `#important` tags same color (consistency across lists)

14. **Search Mode: Tag Matches**
    - Search for `tag` in a task with `#tag` in description or as tag
    - Expectation: Tag pill colors correctly; search highlighting doesn't interfere

---

## Part 8: Specification Summary

### What's Clear

- TaskerTray already implements hash-based colors correctly
- CLI/TUI currently use flat cyan; need to switch to per-tag colors
- 10-color palette is defined in TaskerTray
- Hash algorithm is `Math.Abs(tag.GetHashCode()) % colorCount`

### What's Ambiguous

1. Should colors be shared between platforms identically?
2. How to handle terminal color limitations?
3. Interaction with search highlighting, multiselect, checked state?
4. Hash algorithm stability across .NET versions?
5. Whether hash collisions are acceptable?

### Implementation Assumptions

If questions remain unanswered, implementation will assume:

1. **Collisions acceptable**: 10-color palette is fixed; collisions expected
2. **No terminal degradation**: Assume true-color support; no fallback
3. **GetHashCode() stable**: Use as-is; no custom deterministic hash
4. **Shared utility**: Create TagColorHelper in TaskerCore
5. **Tag colors unchanged for checked tasks**: No dimming applied to colors
6. **No search highlighting on tags**: Tag color takes precedence
7. **No real-time color preview**: Colors appear after save
8. **No palette configuration**: Fixed colors; no user preference
9. **Colors not synced**: Colors are UI-only; not exported/backed up

---

## Appendix: Code References

### Current Implementation Files

**TaskerTray** (Avalonia, already correct):
- `/Users/carlos/self-development/cli-tasker/src/TaskerTray/Views/TaskListPopup.axaml.cs` (lines 1544-1563)
  - `GetTagColor(string tag)` method with 10-color palette

**CLI**:
- `/Users/carlos/self-development/cli-tasker/Output.cs` (lines 33-38)
  - `FormatTags(string[]? tags)` returns hardcoded cyan
- `/Users/carlos/self-development/cli-tasker/AppCommands/ListCommand.cs`
  - Uses `Output.FormatTags()` in list display
- `/Users/carlos/self-development/cli-tasker/AppCommands/GetCommand.cs`
  - Uses `Output.FormatTags()` in task detail display

**TUI**:
- `/Users/carlos/self-development/cli-tasker/Tui/TuiRenderer.cs` (lines ~170)
  - `FormatTags(string[]? tags)` returns hardcoded cyan
- `/Users/carlos/self-development/cli-tasker/Tui/TuiKeyHandler.cs`
  - Calls RenderTask() which uses FormatTags()

**Models & Data**:
- `/Users/carlos/self-development/cli-tasker/src/TaskerCore/Models/TodoTask.cs` (lines 5-69)
  - TodoTask record with `Tags` property
- `/Users/carlos/self-development/cli-tasker/src/TaskerTray/ViewModels/TodoTaskViewModel.cs` (lines 1-80)
  - ViewModel exposes Tags property

### Test Files

- `/Users/carlos/self-development/cli-tasker/tests/TaskerCore.Tests/Parsing/TaskDescriptionParserTests.cs`
  - Tests for tag parsing; relevant for edge cases
- No existing tag color tests

---

## Conclusion

This feature is **straightforward in concept** but **nuanced in execution**. The primary implementation task is extracting GetTagColor() to a shared utility and updating Output.FormatTags() and TuiRenderer.FormatTags() to use per-tag coloring instead of flat cyan.

**Critical blocker**: Decision on hash collision tolerance (Q1) and hash algorithm stability (Q3) will determine final design. Once those are resolved, implementation is straightforward.

**Recommended approach**: Answer questions Q1-Q4 immediately, then implement shared TagColorHelper, update the two FormatTags() methods, and run manual testing across terminal types.

---

**Document Version**: 1.0
**Last Updated**: 2026-02-05
**Status**: Ready for clarification phase
