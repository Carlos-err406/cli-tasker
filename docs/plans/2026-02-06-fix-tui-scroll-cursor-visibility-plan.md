---
title: "fix: TUI scroll to keep cursor visible"
type: fix
date: 2026-02-06
task: daf
---

# fix: TUI scroll to keep cursor visible

The TUI doesn't scroll to follow the cursor when tasks overflow the terminal height. The viewport calculation in `TuiRenderer.RenderTasks()` assumes 1 line per task, but multi-line descriptions and list group headers consume extra lines, causing the cursor to fall outside the rendered area.

## Acceptance Criteria

- [x] Cursor task is always fully visible when navigating with Up/Down/Home/End
- [x] Multi-line task descriptions don't break viewport calculation
- [x] List group headers (all-lists view) are accounted for in line budget
- [x] Overlay modes (input/select) that reduce available lines still keep cursor visible
- [x] Clamp `availableLines` to minimum 1 to prevent negative values
- [x] Add unit tests for viewport calculation
- [x] Run `update.sh patch`

## Implementation

### 1. Extract `CountTaskLines()` private method in TuiRenderer

#### Tui/TuiRenderer.cs

Extract line-counting logic from `RenderTask()` into a reusable private method. Must use `TaskDescriptionParser.GetDisplayDescription()` (not raw `Description`) to match what `RenderTask` actually renders:

```csharp
private static int CountTaskLines(TodoTask task)
{
    var displayDesc = TaskDescriptionParser.GetDisplayDescription(task.Description);
    return displayDesc.Split('\n').Length;
}
```

Have `RenderTask()` call `CountTaskLines()` internally so there's a single source of truth for line count.

### 2. Replace viewport math inline in `RenderTasks()`

#### Tui/TuiRenderer.cs

Replace the current 3-line viewport calculation (lines 57-59) with line-height-aware logic directly in `RenderTasks()`. No new class or file — private `internal static` method on `TuiRenderer`:

```csharp
// Clamp
availableLines = Math.Max(1, availableLines);

// Pre-compute line heights
var lineHeights = new int[tasks.Count];
string? preGroup = null;
for (var i = 0; i < tasks.Count; i++)
{
    var height = CountTaskLines(tasks[i]);
    if (showingAllLists && tasks[i].ListName != preGroup)
    {
        height += 1; // list group header
        preGroup = tasks[i].ListName;
    }
    lineHeights[i] = height;
}

var (startIndex, endIndex) = ComputeViewport(state.CursorIndex, lineHeights, availableLines);
```

The `ComputeViewport` method is `internal static` on `TuiRenderer` for testability:

```csharp
internal static (int StartIndex, int EndIndex) ComputeViewport(
    int cursorIndex, int[] lineHeights, int availableLines)
{
    availableLines = Math.Max(1, availableLines);
    if (lineHeights.Length == 0) return (0, 0);
    cursorIndex = Math.Clamp(cursorIndex, 0, lineHeights.Length - 1);

    // Start with the cursor task, then expand upward, then fill downward
    var startIndex = cursorIndex;
    var budget = lineHeights[cursorIndex];

    // Expand upward
    while (startIndex > 0 && budget + lineHeights[startIndex - 1] <= availableLines)
        budget += lineHeights[--startIndex];

    // Expand downward
    var endIndex = cursorIndex + 1;
    while (endIndex < lineHeights.Length && budget + lineHeights[endIndex] <= availableLines)
        budget += lineHeights[endIndex++];

    return (startIndex, endIndex);
}
```

### 3. Add `[InternalsVisibleTo]` for test project

#### cli-tasker.csproj

Add so tests can call `ComputeViewport`:

```xml
<ItemGroup>
  <InternalsVisibleTo Include="TaskerCore.Tests" />
</ItemGroup>
```

### 4. Add unit tests

#### tests/TaskerCore.Tests/Tui/ViewportCalculatorTests.cs (new file)

Test `TuiRenderer.ComputeViewport()` directly:

- All single-line tasks, cursor in middle — baseline
- Multi-line tasks, cursor on a 3-line task near bottom — the actual bug
- Group headers (first task in group has +1 height), cursor on non-first group
- Single task exceeding available lines — no crash, shows what fits

## Edge Cases

- **Negative availableLines**: Clamped to 1
- **Task exceeds viewport**: Show as much as possible starting from first line
- **List header + cursor task**: If cursor is first task in a group, its header is included in line budget
- **Terminal wrapping** (long lines exceeding terminal width): Out of scope — known limitation
- **Terminal resize**: Handled naturally since `Console.WindowHeight` is re-read each render

## References

- Bug location: `Tui/TuiRenderer.cs:57-59` (viewport calculation)
- Cursor bounds: `Tui/TuiApp.cs:49-56`
- Cursor movement: `Tui/TuiKeyHandler.cs:42-54`
- Render entry: `Tui/TuiRenderer.cs:14` (`Render()`)
- Task rendering: `Tui/TuiRenderer.cs:89-145` (`RenderTask()`)
- Display description: `TaskerCore/Parsing/TaskDescriptionParser.cs:79` (`GetDisplayDescription()`)
