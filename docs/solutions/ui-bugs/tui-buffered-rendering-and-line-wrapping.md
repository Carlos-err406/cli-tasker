---
title: "TUI buffered rendering and line wrapping"
date: 2026-02-06
category: ui-bugs
tags: [tui, rendering, flicker, buffer, wrapping, viewport, ansi, spectre-console]
module: [TUI]
severity: high
symptoms:
  - TUI flickers visibly on every cursor move
  - Header and first list name cropped off top of screen
  - Long continuation lines clipped horizontally at terminal edge
---

# TUI Buffered Rendering and Line Wrapping

## Problem

Three interrelated TUI rendering issues:

1. **Flicker**: Every cursor move triggered dozens of individual `Console.Write()` and `AnsiConsole.Markup()` calls, making re-renders visually noticeable.
2. **Top cropping**: After buffering the output, the header ("tasker (all lists)") and first list group name were pushed off the top of the screen.
3. **Horizontal clipping**: After disabling terminal auto-wrap to fix the cropping, long continuation lines were silently clipped at the terminal edge.

## Root Cause

### Flicker
Each `AnsiConsole.Markup()` and `Console.Write()` call flushed to the terminal individually. With 30+ calls per frame, the terminal displayed partially-rendered frames between calls.

### Top cropping
Terminal-level text wrapping was the culprit. Long description lines (wider than terminal width) wrapped at the terminal edge, creating extra *visual* lines beyond what the viewport budget counted. The viewport budget counted only logical lines (`\n` characters), so the actual visual output exceeded terminal height and scrolled the top off screen.

Debug confirmed: `termHeight=56` but the frame contained `58` newlines — 2 extra from terminal wrapping.

Two additional issues discovered during investigation:
- `StringWriter.NewLine` defaults to `"\r\n"` on ALL platforms (not `"\n"` like `Console.Out`), adding invisible `\r` characters
- `Spectre.Console` wraps text at `Profile.Width`, inserting extra newlines into the buffer

### Horizontal clipping
Disabling terminal auto-wrap (`\x1b[?7l`) prevented the visual overflow but caused long lines to be silently truncated at the terminal edge with no visual indicator.

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

## Key Gotchas

1. **`StringWriter.NewLine` defaults to `"\r\n"` everywhere** — even on macOS/Linux. Always set `.NewLine = "\n"` explicitly when buffering terminal output.

2. **`Spectre.Console Profile.Width` causes wrapping** — Set to `int.MaxValue` when you want the renderer (not Spectre) to control line breaks.

3. **Terminal auto-wrap (`\x1b[?7l`) must be re-enabled on exit** — Put the re-enable in a `finally` block alongside alternate screen buffer exit, or the user's shell will have broken wrapping after a crash.

4. **Viewport line count must match rendering exactly** — If the renderer wraps lines, `CountTaskLines` must use the same wrapping logic. Any mismatch causes the viewport to over- or under-estimate, scrolling content off screen.

5. **Synchronized output markers (`\x1b[?2026h`/`\x1b[?2026l`)** were tried and removed — they had compatibility issues with some terminals (Warp). The single-flush buffer approach via StringWriter achieves the same atomic rendering without terminal-specific escape codes.

## Prevention

- When building TUI renderers, always buffer the entire frame and flush once. Never write line-by-line to the console.
- When disabling terminal features (auto-wrap, cursor visibility), always restore them in a `finally` block.
- When computing viewport budgets for scrollable TUIs, account for all sources of visual lines: multi-line descriptions, group headers, AND text wrapping.
- Test with tasks that have long descriptions to catch wrapping issues early.

## References

- `Tui/TuiRenderer.cs` — buffered rendering, word wrapping, viewport calculation
- `Tui/TuiApp.cs:37-41` — alternate screen buffer and auto-wrap setup
- `Tui/TuiApp.cs:67-72` — cleanup in finally block
- `tests/TaskerCore.Tests/Tui/ViewportTests.cs` — viewport and wrap tests
- Related plan: `docs/plans/2026-02-06-fix-tui-scroll-cursor-visibility-plan.md`
- ANSI escape reference: `\x1b[?7l` (DECRST auto-wrap), `\x1b[?1049h` (alternate screen)
