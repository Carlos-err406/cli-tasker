---
title: "TUI buffered rendering and line wrapping"
date: 2026-02-06
category: ui-bugs
module: TUI
problem_type: ui_bug
component: frontend_stimulus
symptoms:
  - "TUI flickers visibly on every cursor move due to line-by-line console writes"
  - "Header and first list name cropped off top of screen after buffering"
  - "Long continuation lines clipped horizontally at terminal edge"
root_cause: logic_error
resolution_type: code_fix
severity: high
tags: [tui, rendering, flicker, buffer, wrapping, viewport, ansi, spectre-console]
---

# Troubleshooting: TUI Buffered Rendering and Line Wrapping

## Problem

Three interrelated TUI rendering issues: visible flicker on every cursor move, header cropped off the top of the screen after buffering, and long continuation lines clipped horizontally after disabling terminal auto-wrap.

## Environment

- Module: TUI
- Framework: .NET 10 / Spectre.Console 0.54.0
- Affected Component: `Tui/TuiRenderer.cs`, `Tui/TuiApp.cs`
- Date: 2026-02-06

## Symptoms

- TUI flickers visibly on every cursor move (dozens of individual `Console.Write` calls per frame)
- After buffering fix: "tasker (all lists)" header and first list group name pushed off screen top
- Debug output showed `termHeight=56` but frame contained `58` newlines
- After disabling terminal auto-wrap: long continuation lines silently clipped at terminal edge with no visual indicator

## What Didn't Work

**Attempted Solution 1:** Synchronized output markers (`\x1b[?2026h`/`\x1b[?2026l`)
- **Why it failed:** Compatibility issues with Warp terminal. Markers weren't reliably supported.

**Attempted Solution 2:** `Console.SetCursorPosition(0,0)` instead of `\x1b[H` in buffer
- **Why it failed:** The cropping wasn't caused by cursor positioning — it was terminal-level text wrapping creating extra visual lines.

**Attempted Solution 3:** Remove synchronized output markers entirely
- **Why it failed:** Didn't address the root cause (visual line overflow from terminal wrapping).

**Attempted Solution 4:** Set `_buffer.NewLine = "\n"` and `_ansi.Profile.Width = Console.WindowWidth`
- **Why it failed:** Fixed the `\r\n` issue but Spectre's width-based wrapping still inserted extra newlines. And terminal wrapping still added visual lines beyond the budget.

**Attempted Solution 5:** Set `_ansi.Profile.Width = int.MaxValue` to prevent Spectre wrapping
- **Why it failed:** Prevented Spectre wrapping but terminal-level auto-wrap still created extra visual lines for long text.

**Attempted Solution 6:** Disable terminal auto-wrap with `\x1b[?7l`
- **Why it partially failed:** Fixed the vertical cropping but long continuation lines were now silently clipped horizontally — text just disappeared at the terminal edge.

## Solution

Three-layer fix applied to `Tui/TuiRenderer.cs` and `Tui/TuiApp.cs`:

### 1. Buffered rendering (eliminates flicker)

Replace individual console writes with a single-flush buffer:

```csharp
// Tui/TuiRenderer.cs
private StringWriter _buffer = new();
private IAnsiConsole _ansi = null!;

public void Render(TuiState state, IReadOnlyList<TodoTask> tasks)
{
    _buffer = new StringWriter();
    _buffer.NewLine = "\n"; // StringWriter defaults to "\r\n" on all platforms
    _ansi = AnsiConsole.Create(new AnsiConsoleSettings
    {
        Ansi = AnsiSupport.Yes,
        ColorSystem = ColorSystemSupport.TrueColor,
        Out = new AnsiConsoleOutput(_buffer),
    });
    _ansi.Profile.Width = int.MaxValue; // never wrap — renderer handles it

    RenderHeader(state, tasks.Count);
    RenderTasks(state, tasks);
    RenderStatusBar(state, tasks.Count);

    Console.SetCursorPosition(0, 0);
    Console.Write(_buffer.ToString());
}
```

All `WriteLineCleared` and `ClearLine` helpers write to `_buffer`/`_ansi` instead of directly to the console.

### 2. Disable terminal auto-wrap (prevents visual overflow)

```csharp
// Tui/TuiApp.cs — in Run()
Console.Write("\x1b[?1049h"); // alternate screen buffer
Console.Write("\x1b[?7l");    // disable auto-wrap

// In finally block:
Console.Write("\x1b[?7h");    // re-enable auto-wrap
Console.Write("\x1b[?1049l"); // exit alternate screen
```

### 3. Word-wrap continuation lines in renderer (preserves readability)

Wrap long continuation lines at word boundaries, maintaining indentation. This keeps the viewport line count accurate since the renderer controls exactly how many visual lines each task produces.

```csharp
// In RenderTask — continuation line rendering
var wrapWidth = Math.Max(10, Console.WindowWidth - indent.Length);
for (var i = 1; i < lines.Length && linesRendered < maxLines; i++)
{
    var wrappedSegments = WrapLine(lines[i], wrapWidth);
    foreach (var segment in wrappedSegments)
    {
        if (linesRendered >= maxLines) break;
        var continuationLine = HighlightSearch(segment, searchQuery);
        WriteLineCleared($"{indent}[dim]{continuationLine}[/]");
        linesRendered++;
    }
}
```

The `WrapLine` helper breaks at spaces when possible, mid-word as last resort:

```csharp
internal static List<string> WrapLine(string line, int maxWidth)
{
    if (maxWidth < 1) maxWidth = 1;
    if (line.Length <= maxWidth) return [line];

    var result = new List<string>();
    var remaining = line;
    while (remaining.Length > maxWidth)
    {
        var breakAt = remaining.LastIndexOf(' ', maxWidth - 1);
        if (breakAt <= 0) breakAt = maxWidth;
        result.Add(remaining[..breakAt]);
        remaining = remaining[breakAt..].TrimStart();
    }
    if (remaining.Length > 0)
        result.Add(remaining);
    return result;
}
```

`CountTaskLines` uses the same `WrapLine` logic so viewport budgeting matches rendering exactly:

```csharp
private static int CountTaskLines(TodoTask task, int wrapWidth)
{
    var displayDesc = TaskDescriptionParser.GetDisplayDescription(task.Description);
    var lines = displayDesc.Split('\n');
    var count = 1; // first line always 1 visual line
    for (var i = 1; i < lines.Length; i++)
        count += WrapLine(lines[i], wrapWidth).Count;
    return count;
}
```

## Why This Works

1. **Buffering** eliminates flicker because the terminal only receives one large write per frame instead of 30+ individual calls. No partially-rendered state is ever visible.

2. **Disabling auto-wrap** (`\x1b[?7l`) prevents the terminal from creating extra visual lines when text exceeds terminal width. This keeps the renderer in full control of the line count.

3. **Renderer-level word wrapping** means `CountTaskLines` and `RenderTask` use the same `WrapLine` logic, so the viewport budget is always accurate. The renderer decides exactly how many visual lines each task produces — no surprises from terminal or Spectre wrapping.

The key insight: **the renderer must be the single source of truth for visual line count**. Neither the terminal (auto-wrap) nor the library (Spectre.Console Profile.Width) should insert line breaks — only the renderer's `WrapLine` function.

## Prevention

- When building TUI renderers, always buffer the entire frame and flush once. Never write line-by-line to the console.
- When disabling terminal features (auto-wrap, cursor visibility), always restore them in a `finally` block.
- When computing viewport budgets for scrollable TUIs, account for all sources of visual lines: multi-line descriptions, group headers, AND text wrapping.
- Test with tasks that have long descriptions to catch wrapping issues early.
- Remember: `StringWriter.NewLine` defaults to `"\r\n"` on ALL platforms. Always set `.NewLine = "\n"` explicitly when buffering terminal output.
- Set `Spectre.Console Profile.Width = int.MaxValue` when you want the renderer (not Spectre) to control line breaks.
- Synchronized output markers (`\x1b[?2026h`/`\x1b[?2026l`) have compatibility issues — prefer single-flush buffer approach.

## Related Issues

- Related plan: [fix-tui-scroll-cursor-visibility-plan](../../plans/2026-02-06-fix-tui-scroll-cursor-visibility-plan.md)
