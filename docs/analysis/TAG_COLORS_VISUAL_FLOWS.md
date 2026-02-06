# Tag Colors Feature - Visual Flow Diagrams

---

## Flow 1: Add Task with Tags (All Platforms)

```
User Input (CLI/TUI/TaskerTray)
┌──────────────────────────────┐
│ "Task description #tag1 #tag2" │
└──────────────────┬───────────┘
                   │
                   ▼
    TaskDescriptionParser.Parse()
    ┌─────────────────────────────────┐
    │ Extract tags: ["tag1", "tag2"]   │
    │ Description: "Task description"  │
    │ Priority/Due: (if present)       │
    └──────────────┬──────────────────┘
                   │
                   ▼
       TodoTask.CreateTodoTask()
       ┌────────────────────────────┐
       │ new TodoTask(               │
       │   Id: "abc",                │
       │   Tags: ["tag1", "tag2"]    │
       │ )                           │
       └──────────┬───────────────────┘
                  │
                  ▼
         TodoTaskList.AddTodoTask()
         ┌──────────────────────┐
         │ Insert at top        │
         │ Save to JSON storage │
         └──────────┬───────────┘
                    │
                    ▼
          Task Stored (No Colors Yet)
          ┌──────────────────────────┐
          │ JSON:                     │
          │ {                         │
          │   "Tags": ["tag1", "tag2"] │
          │ }                         │
          └──────────┬────────────────┘
                     │
       ┌─────────────┼─────────────┐
       │             │             │
       ▼             ▼             ▼
      CLI          TUI         TaskerTray
    (Render)    (Render)      (Render)
```

---

## Flow 2: View Tasks - Before and After Implementation

### Current State (All Platforms Render Cyan)

```
CLI: tasker list
┌──────────────────────────────────┐
│ (abc) [x] - Task with tags      │
│           [cyan]#tag1 #tag2[/] │  ◄─── ALL TAGS SAME CYAN
└──────────────────────────────────┘

TUI Interactive
┌──────────────────────────────────┐
│ (abc) [x] [ ] Task with tags     │
│             [cyan]#tag1 #tag2[/] │  ◄─── ALL TAGS SAME CYAN
└──────────────────────────────────┘

TaskerTray Popup
┌──────────────────────────────┐
│ ▢ Task with tags             │
│   [Blue #tag1]  [Red #tag2] │  ◄─── EACH TAG DIFFERENT COLOR
└──────────────────────────────┘
```

### After Implementation (Consistent Across All)

```
CLI: tasker list
┌──────────────────────────────────┐
│ (abc) [x] - Task with tags      │
│         [#3B82F6]#tag1[/]       │  ◄─── Per-tag hash color
│         [#10B981]#tag2[/]       │
└──────────────────────────────────┘

TUI Interactive
┌──────────────────────────────────┐
│ (abc) [x] [ ] Task with tags     │
│         [#3B82F6]#tag1[/]        │  ◄─── Same colors as CLI
│         [#10B981]#tag2[/]        │
└──────────────────────────────────┘

TaskerTray Popup
┌──────────────────────────────┐
│ ▢ Task with tags             │
│   [Blue #tag1]  [Emerald #tag2] │  ◄─── Same colors as CLI/TUI
└──────────────────────────────┘
```

---

## Flow 3: Color Assignment Algorithm

```
Input: Tag String
┌─────────────┐
│   "urgent"  │
└──────┬──────┘
       │
       ▼
  tag.GetHashCode()
  ┌──────────────────────┐
  │ Returns platform-    │
  │ specific int hash    │
  │ e.g., -1234567890    │
  └──────┬───────────────┘
         │
         ▼
   Math.Abs()
   ┌──────────────────────┐
   │ Makes it positive    │
   │ e.g., 1234567890     │
   └──────┬───────────────┘
          │
          ▼
   % ColorPalette.Length
   ┌──────────────────────┐
   │ 1234567890 % 10      │
   │ = 0 (example)        │
   └──────┬───────────────┘
          │
          ▼
   ColorPalette[index]
   ┌──────────────────────┐
   │ ColorPalette[0]      │
   │ = "#3B82F6" (Blue)   │
   └──────┬───────────────┘
          │
          ▼
   Output Format
   ┌──────────────────────┐
   │ CLI/TUI:             │
   │ [#3B82F6]#urgent[/]  │
   │                      │
   │ TaskerTray:          │
   │ Color.Parse("#3B...") │
   │ = Brush (Avalonia)   │
   └──────────────────────┘
```

---

## Flow 4: Tag Color Consistency Guarantee

```
Same Tag, Different Contexts
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Tag: #urgent

┌─────────┐
│ CLI     │
│ list    │
└────┬────┘
     │ FormatTags()
     │ ▼
     └─► TagColorHelper.GetTagColor("urgent")
         ▼
         [#EF4444] (Red)

┌─────────┐
│ TUI     │
│ view    │
└────┬────┘
     │ FormatTags()
     │ ▼
     └─► TagColorHelper.GetTagColor("urgent")
         ▼
         [#EF4444] (Red)

┌──────────┐
│ TaskerTray│
│ popup    │
└────┬─────┘
     │ CreateTaskItem()
     │ ▼
     └─► TagColorHelper.GetTagColor("urgent")
         ▼
         Color.Parse("#EF4444") (Red)

RESULT: All three UIs show same color for same tag ✓
```

---

## Flow 5: Hash Collision Scenario (Edge Case)

```
Scenario: User has 50 unique tags, palette has 10 colors

Tag 1: "urgent"      ────► hash % 10 = 0 ──► Blue    (#3B82F6)
Tag 2: "feature"     ────► hash % 10 = 0 ──► Blue    (#3B82F6) ← COLLISION!
Tag 3: "bug"         ────► hash % 10 = 1 ──► Emerald (#10B981)
Tag 4: "documentation" ──► hash % 10 = 2 ──► Amber   (#F59E0B)
...
Tag 50: "random"     ────► hash % 10 = ? ──► ?

CLI Output:
┌─────────────────────────────────────────────┐
│ (abc) [x] - Different tags, similar colors  │
│   [#3B82F6]#urgent[/]                       │
│   [#3B82F6]#feature[/]  ◄─── SAME COLOR    │
│   [#10B981]#bug[/]                          │
│   [#F59E0B]#documentation[/]                │
└─────────────────────────────────────────────┘

User still distinguishes tags by TEXT ("#urgent" vs "#feature")
Colors help but are not 1:1 unique

Question: Is this acceptable?
```

---

## Flow 6: Checked Task Rendering (Ambiguity)

```
Scenario: User completes a task with colored tags

Current Behavior: Task text gets dimmed, but tag color?

Option A: Tags also dimmed (follow text)
┌──────────────────────────┐
│ ✓ (abc) - Done task      │
│         [dim]            │
│         [#3B82F6]#tag[/] │  ◄─── Dimmed blue
│         [/][/]           │
└──────────────────────────┘

Option B: Tags stay bright (retain color emphasis)
┌──────────────────────────┐
│ ✓ (abc) - Done task      │
│         [dim][/]         │
│         [#3B82F6]#tag[/] │  ◄─── Still bright blue
└──────────────────────────┘

Implementation Assumption: Option A (tags follow text dimming)
```

---

## Flow 7: Search Highlighting Interaction (Ambiguity)

```
Scenario: TUI search mode with query "#urgent"

Step 1: User searches
┌──────────────────┐
│ /urgent          │  ◄─── Search query
└──────────────────┘

Step 2: Task displayed with highlight
┌──────────────────────────────────┐
│ (abc) [ ] [ ] Task description   │
│        [yellow]urgent[/]          │  ◄─── Highlight matches
│        [#EF4444]#urgent[/]        │  ◄─── Tag color
└──────────────────────────────────┘

Question: What if tag matches search?
┌──────────────────────────────────┐
│ (abc) [ ] [ ] Task #urgent       │
│        [#EF4444][yellow]#urgent[/][/] │  ◄─── Nested markup?
│  OR                              │
│        [#EF4444]#urgent[/]        │  ◄─── Tag color only?
│  OR                              │
│        [yellow]#urgent[/]         │  ◄─── Search highlight only?
└──────────────────────────────────┘

Implementation Assumption: Tag color takes precedence (no nested markup)
```

---

## Flow 8: Multi-Select Mode Interaction (Ambiguity)

```
Scenario: TUI multi-select mode with colored tags

Normal task:
┌──────────────────────┐
│ [ ] (abc) My task    │
│       [#3B82F6]#tag[/] │
└──────────────────────┘

Selected task (multi-select):
┌──────────────────────────────────┐
│ [*] (abc) My task (highlighted)  │
│       [blue]                     │  ◄─── Selection indicator
│       [#3B82F6]#tag[/]           │  ◄─── Tag color unchanged
│     [/]                          │
└──────────────────────────────────┘

Question: Should tag colors brighten, darken, or stay same?

Implementation Assumption: Tag colors unchanged; selection indicated by text/background
```

---

## Flow 9: Code Architecture (After Implementation)

```
Before Implementation:
━━━━━━━━━━━━━━━━━━━━━━━━

Output.cs
├─► FormatTags(tags)
│   └─► [cyan]#tag1 #tag2[/]  ◄─── Hardcoded
│
TuiRenderer.cs
├─► FormatTags(tags)
│   └─► [cyan]#tag1 #tag2[/]  ◄─── Hardcoded
│
TaskListPopup.axaml.cs
├─► GetTagColor(tag)
│   └─► Hash-to-palette ✓  ◄─── Already implemented


After Implementation:
━━━━━━━━━━━━━━━━━━━━━━

TaskerCore/Colors/TagColorHelper.cs  ◄─── NEW SHARED UTILITY
├─► ColorPalette[] (10 colors)
├─► GetTagColor(tag) → hex string
├─► GetTagColorMarkup(tag) → Spectre markup
└─► GetTagColorAvalonia(tag) → Avalonia Color

Output.cs
├─► FormatTags(tags)
│   └─► For each tag:
│       └─► TagColorHelper.GetTagColorMarkup(tag)
│           └─► [#3B82F6]#tag1[/] [#10B981]#tag2[/]

TuiRenderer.cs
├─► FormatTags(tags)
│   └─► For each tag:
│       └─► TagColorHelper.GetTagColorMarkup(tag)
│           └─► [#3B82F6]#tag1[/] [#10B981]#tag2[/]

TaskListPopup.axaml.cs
├─► GetTagColor(tag)
│   └─► TagColorHelper.GetTagColorAvalonia(tag)
│       └─► Color.Parse("#3B82F6")
```

---

## Flow 10: Data Flow - Storage to Render

```
┌──────────────────────────────────────────────────────────────────┐
│                    USER INTERACTION LAYER                        │
│                                                                  │
│  CLI: tasker list                                                │
│  TUI: tasker tui (interactive)                                   │
│  TaskerTray: Menu bar popup                                      │
└──────────┬──────────────────────────────────────────────────────┘
           │
           ▼
┌──────────────────────────────────────────────────────────────────┐
│                      DATA RETRIEVAL LAYER                        │
│                                                                  │
│  TodoTaskList.GetAllTasks()                                      │
│  TodoTaskList.ListTodoTasks(filter?)                             │
│  Returns: IReadOnlyList<TodoTask>                                │
└──────────┬──────────────────────────────────────────────────────┘
           │
           ▼
┌──────────────────────────────────────────────────────────────────┐
│                    FORMATTING/COLORING LAYER                     │
│                                                                  │
│  CLI:                  TUI:                 TaskerTray:          │
│  ┌──────────────┐    ┌──────────────┐    ┌──────────────┐      │
│  │ FormatTags() │    │ FormatTags() │    │ CreateTask   │      │
│  │ (Output.cs)  │    │(TuiRenderer) │    │ Item()       │      │
│  │              │    │              │    │              │      │
│  │ For each tag:│    │ For each tag:│    │ For each tag:│      │
│  │  Hash→Color  │    │  Hash→Color  │    │  Hash→Color  │      │
│  │  →Markup     │    │  →Markup     │    │  →Brush      │      │
│  └──────────┬───┘    └──────┬───────┘    └──────┬───────┘      │
│             │               │                    │               │
│    Markup string      Markup string      Avalonia Brush          │
│                                                                  │
└──────────┬───────────────┬──────────────────┬────────────────────┘
           │               │                  │
           ▼               ▼                  ▼
┌──────────────────┐ ┌──────────────┐ ┌───────────────────┐
│  RENDER LAYER    │ │ RENDER LAYER │ │  UI RENDER LAYER  │
│                  │ │              │ │                   │
│ AnsiConsole.     │ │ Console.     │ │ Border.Child =    │
│ MarkupLine()     │ │ WriteLine()  │ │ TextBlock()       │
│                  │ │              │ │                   │
│ Terminal renders │ │ Terminal     │ │ Avalonia renders  │
│ ANSI codes       │ │ renders      │ │ UI framework      │
│ as colors        │ │ ANSI codes   │ │                   │
└────────────┬─────┘ └──────┬───────┘ └───┬────────────────┘
             │              │             │
             ▼              ▼             ▼
     ┌──────────────────────────────────────────┐
     │        USER SEES COLORED TAGS            │
     │                                          │
     │  #tag1 (Blue)  #tag2 (Emerald)           │
     │  SAME COLOR across all UIs ✓             │
     └──────────────────────────────────────────┘
```

---

## Flow 11: Test Scenarios

```
Unit Tests
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Test: Hash Consistency
┌─────────────────────────────────┐
│ var color1 = GetTagColor("foo") │
│ var color2 = GetTagColor("foo") │
│ Assert.Equal(color1, color2)    │
│ ✓ PASS: Same tag always same color
└─────────────────────────────────┘

Test: Palette Membership
┌──────────────────────────────────┐
│ var color = GetTagColor("random")│
│ Assert.Contains(color, Palette)  │
│ ✓ PASS: Result in palette array
└──────────────────────────────────┘

Test: Edge Cases
┌──────────────────────────────────┐
│ GetTagColor("")       ──► Error or default?
│ GetTagColor("UNICODE")──► Hash works?
│ GetTagColor(null)     ──► Error?
│ GetTagColor("a"*1000) ──► Hash works?
└──────────────────────────────────┘

Integration Tests
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Test: CLI List Output
┌──────────────────────────────────────────┐
│ 1. Create task with tags                 │
│ 2. Run: tasker list                      │
│ 3. Verify: Tags have ANSI color codes    │
│ 4. Verify: Same tag has same code        │
│ ✓ PASS: Colors display in CLI
└──────────────────────────────────────────┘

Test: Cross-Platform Consistency
┌──────────────────────────────────────────┐
│ 1. Create task with tags                 │
│ 2. Run on macOS Terminal                 │
│ 3. Run on iTerm2                         │
│ 4. Run on VSCode Terminal                │
│ 5. Run on Linux terminal (if available)  │
│ 6. Verify: Colors appear same            │
│ ✓ PASS: Cross-platform consistent colors
└──────────────────────────────────────────┘

Manual Testing
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

[ ] CLI list: Colors appear for tags
[ ] CLI get: Colors appear for tags
[ ] TUI interactive: Colors appear for tags
[ ] TUI search: Colors appear during search
[ ] TUI multiselect: Colors appear when selected
[ ] TaskerTray: Colors unchanged (baseline)
[ ] Light terminal: Colors readable?
[ ] 16-color terminal: Colors degrade gracefully?
[ ] Very long tags: Don't overflow?
[ ] Unicode tags: Render correctly?
```

---

## Flow 12: Implementation Steps (Simplified)

```
┌─────────────────────────────────────────────────────────────────┐
│ STEP 1: Answer Blocking Questions (30 min)                      │
│                                                                 │
│ □ Collision tolerance acceptable? (Q1)                          │
│ □ Terminal fallback strategy? (Q2)                              │
│ □ Hash algorithm (GetHashCode vs SHA256)? (Q3)                  │
│ □ Shared utility location? (Q4)                                 │
└─────────────────────────────────────────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────────────┐
│ STEP 2: Create Shared Utility (45 min)                          │
│                                                                 │
│ Create: TaskerCore/Colors/TagColorHelper.cs                    │
│ ├─► ColorPalette = 10 colors (from TaskListPopup)              │
│ ├─► GetTagColor(tag) → string hex                              │
│ ├─► GetTagColorMarkup(tag) → Spectre markup                    │
│ └─► GetTagColorAvalonia(tag) → Avalonia Color                  │
└─────────────────────────────────────────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────────────┐
│ STEP 3: Update CLI Formatting (15 min)                          │
│                                                                 │
│ Edit: Output.cs                                                 │
│ Old: return $"  [cyan]{...}[/]";                                │
│ New: For each tag:                                              │
│      return TagColorHelper.GetTagColorMarkup(tag)               │
└─────────────────────────────────────────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────────────┐
│ STEP 4: Update TUI Formatting (15 min)                          │
│                                                                 │
│ Edit: Tui/TuiRenderer.cs                                        │
│ Old: return $"  [cyan]{...}[/]";                                │
│ New: For each tag:                                              │
│      return TagColorHelper.GetTagColorMarkup(tag)               │
└─────────────────────────────────────────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────────────┐
│ STEP 5: Refactor TaskerTray (10 min)                            │
│                                                                 │
│ Edit: TaskListPopup.axaml.cs                                    │
│ Old: Inline GetTagColor() method                                │
│ New: Call TagColorHelper.GetTagColorAvalonia()                  │
└─────────────────────────────────────────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────────────┐
│ STEP 6: Add Tests (1 hour)                                      │
│                                                                 │
│ Create: TaskerCore.Tests/Colors/TagColorHelperTests.cs         │
│ ├─► Test hash consistency                                       │
│ ├─► Test palette membership                                     │
│ ├─► Test edge cases (empty, unicode, long)                     │
│ └─► Test cross-platform behavior (if needed)                   │
└─────────────────────────────────────────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────────────┐
│ STEP 7: Manual Testing (1-2 hours)                              │
│                                                                 │
│ □ CLI: Colors visible on macOS, Linux terminals                │
│ □ TUI: Colors visible in interactive mode                      │
│ □ TaskerTray: Colors unchanged and visible                     │
│ □ Edge cases: Long tags, unicode, special chars                │
│ □ Accessibility: Colors readable on dark background            │
│ □ No regressions: All existing tests pass                      │
└─────────────────────────────────────────────────────────────────┘
                            │
                            ▼
        IMPLEMENTATION COMPLETE ✓
        Total: ~4 hours code + 1-2 hours testing
```

---

**Reference**: Full analysis in `2026-02-05-tag-colors-ux-flow-analysis.md`
