---
title: Consistent Tag Colors
type: feat
date: 2026-02-05
task: "076"
brainstorm: docs/brainstorms/2026-02-05-consistent-tag-colors-brainstorm.md
---

# Consistent Tag Colors

## Overview

Make the same tag text always render with the same color across CLI, TUI, and TaskerTray using a hash-to-palette approach.

**Current state:** TaskerTray already has hash-based colors. CLI/TUI use flat cyan for all tags.

**Goal:** Extract the working pattern from TaskerTray and apply it to CLI/TUI.

## Problem Statement

Tags like `#feature`, `#bug`, `#ui` all render as cyan in CLI/TUI, making them harder to visually distinguish. TaskerTray already solves this with hash-based coloring - we need to bring that consistency to the terminal outputs.

## Proposed Solution

Create a shared `TagColors` utility in TaskerCore that all three outputs use.

```
tag text → hash → palette index → color
"feature" → 12345 → 5 → "#8B5CF6" (Violet)
```

## Technical Approach

### Files to Modify

| File | Change |
|------|--------|
| `src/TaskerCore/Utilities/TagColors.cs` | NEW - Shared color utility |
| `Output.cs:33-38` | Use `TagColors.GetSpectreMarkup(tag)` |
| `Tui/TuiRenderer.cs:164-169` | Use `TagColors.GetSpectreMarkup(tag)` |
| `src/TaskerTray/Views/TaskListPopup.axaml.cs:1544-1563` | Use `TagColors.GetHexColor(tag)` |

### New File: `src/TaskerCore/Utilities/TagColors.cs`

```csharp
namespace TaskerCore.Utilities;

public static class TagColors
{
    // Curated palette - dark theme friendly, visually distinct
    private static readonly string[] Palette =
    [
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
    ];

    public static string GetHexColor(string tag)
    {
        var index = Math.Abs(GetDeterministicHash(tag)) % Palette.Length;
        return Palette[index];
    }

    public static string GetSpectreMarkup(string tag)
    {
        var hex = GetHexColor(tag);
        return $"[{hex}]";  // Spectre supports hex colors
    }

    /// <summary>
    /// Returns a deterministic hash that's consistent across process restarts.
    /// string.GetHashCode() is randomized per-process in .NET Core for security.
    /// </summary>
    private static int GetDeterministicHash(string str)
    {
        unchecked
        {
            int hash = 5381;
            foreach (char c in str)
            {
                hash = ((hash << 5) + hash) ^ c;  // DJB2 hash algorithm
            }
            return hash;
        }
    }
}
```

### Update: `Output.cs`

```csharp
// Before (line 33-38):
public static string FormatTags(string[]? tags)
{
    if (tags is not { Length: > 0 }) return "";
    var tagStr = string.Join(" ", tags.Select(t => $"#{t}"));
    return $"  [cyan]{Spectre.Console.Markup.Escape(tagStr)}[/]";
}

// After:
public static string FormatTags(string[]? tags)
{
    if (tags is not { Length: > 0 }) return "";
    var formatted = tags.Select(t =>
        $"{TagColors.GetSpectreMarkup(t)}#{Spectre.Console.Markup.Escape(t)}[/]");
    return "  " + string.Join(" ", formatted);
}
```

### Update: `Tui/TuiRenderer.cs`

Same change as Output.cs (lines 164-169).

### Update: `TaskListPopup.axaml.cs`

```csharp
// Before (line 1544-1563):
private static Color GetTagColor(string tag)
{
    var colors = new[] { "#3B82F6", ... };
    return Color.Parse(colors[Math.Abs(tag.GetHashCode()) % colors.Length]);
}

// After:
private static Color GetTagColor(string tag)
{
    return Color.Parse(TagColors.GetHexColor(tag));
}
```

## Acceptance Criteria

- [x] Create `TagColors.cs` in `src/TaskerCore/Utilities/`
- [x] Update `Output.FormatTags()` to use per-tag colors
- [x] Update `TuiRenderer.FormatTags()` to use per-tag colors
- [x] Update `TaskListPopup.GetTagColor()` to use shared utility
- [x] Same tag renders same color in CLI, TUI, and TaskerTray
- [x] All existing tests pass
- [x] Add unit tests for `TagColors` (hash determinism, edge cases)

## Critical Bug Fix

**Issue:** `string.GetHashCode()` is randomized per-process in .NET Core/.NET 5+ for security. This means tag colors change on every app restart.

**Fix:** Use DJB2 hash algorithm which is deterministic across all runs.

## Edge Cases

| Case | Behavior |
|------|----------|
| Empty tag | Skip (shouldn't happen - parser filters) |
| Unicode tag | DJB2 hash works on any string |
| Very long tag | Hash works, display truncation is separate concern |
| Case sensitivity | `#Bug` and `#bug` get different colors (intentional) |
| App restart | Colors now persist (DJB2 is deterministic) |

## Testing

### Unit Tests: `TagColorsTests.cs`

```csharp
[Fact]
public void GetHexColor_SameTag_ReturnsSameColor()
{
    var color1 = TagColors.GetHexColor("feature");
    var color2 = TagColors.GetHexColor("feature");
    Assert.Equal(color1, color2);
}

[Fact]
public void GetHexColor_DifferentTags_MayReturnDifferentColors()
{
    var color1 = TagColors.GetHexColor("feature");
    var color2 = TagColors.GetHexColor("bug");
    // Note: Could collide, but unlikely with good hash distribution
    Assert.NotEqual(color1, color2);
}

[Fact]
public void GetHexColor_ReturnsValidHexFormat()
{
    var color = TagColors.GetHexColor("test");
    Assert.Matches(@"^#[0-9A-Fa-f]{6}$", color);
}

[Fact]
public void GetSpectreMarkup_ReturnsValidMarkup()
{
    var markup = TagColors.GetSpectreMarkup("feature");
    Assert.StartsWith("[#", markup);
    Assert.EndsWith("]", markup);
}
```

### Manual Verification

```bash
# Add tasks with various tags
tasker add "Test feature tag #feature"
tasker add "Test bug tag #bug"
tasker add "Test ui tag #ui"
tasker add "Multiple tags #feature #bug"

# Verify in CLI
tasker list

# Verify in TUI
tasker tui

# Verify in TaskerTray (relaunch app)
```

## References

- Brainstorm: `docs/brainstorms/2026-02-05-consistent-tag-colors-brainstorm.md`
- Existing implementation: `TaskListPopup.axaml.cs:1544-1563`
- Priority color pattern: `Output.cs:9-15`
- SpecFlow analysis: `docs/analysis/TAG_COLORS_EXECUTIVE_SUMMARY.md`
