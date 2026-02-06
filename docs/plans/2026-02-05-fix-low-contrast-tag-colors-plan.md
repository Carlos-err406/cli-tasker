---
title: Fix low contrast tag colors in TaskerTray
type: fix
date: 2026-02-05
task: "fe7"
---

# Fix Low Contrast Tag Colors in TaskerTray

## Overview

Some tag pill badges in TaskerTray have low contrast - white text on light backgrounds like Lime (`#84CC16`) and Cyan (`#06B6D4`) is hard to read.

**Scope:** TaskerTray only. CLI/TUI are fine since they apply color to text, not backgrounds.

## Problem

`TaskListPopup.axaml.cs:1216` hardcodes white foreground for all tag pills:

```csharp
Foreground = new SolidColorBrush(Color.Parse("#FFF"))
```

Some palette colors are too light for white text:
- `#84CC16` (Lime) - very low contrast
- `#06B6D4` (Cyan) - low contrast
- `#F59E0B` (Amber) - borderline
- `#FFEAA7` (Soft Yellow) - if ever added

## Proposed Solution

Add a `GetForegroundColor()` method to `TagColors` that returns black or white based on the background's relative luminance. This follows WCAG contrast guidelines.

### File Changes

| File | Change |
|------|--------|
| `src/TaskerCore/Utilities/TagColors.cs` | Add `GetForegroundHex(string tag)` method |
| `src/TaskerTray/Views/TaskListPopup.axaml.cs:1216` | Use `TagColors.GetForegroundHex(tag)` |

### TagColors.cs Addition

```csharp
public static string GetForegroundHex(string tag)
{
    var hex = GetHexColor(tag);
    return IsLightColor(hex) ? "#000000" : "#FFFFFF";
}

private static bool IsLightColor(string hex)
{
    // Parse hex to RGB
    var r = Convert.ToInt32(hex.Substring(1, 2), 16) / 255.0;
    var g = Convert.ToInt32(hex.Substring(3, 2), 16) / 255.0;
    var b = Convert.ToInt32(hex.Substring(5, 2), 16) / 255.0;

    // Relative luminance (WCAG formula)
    var luminance = 0.2126 * r + 0.7152 * g + 0.0722 * b;
    return luminance > 0.5;
}
```

### TaskListPopup.axaml.cs Update

```csharp
// Before (line 1216):
Foreground = new SolidColorBrush(Color.Parse("#FFF"))

// After:
Foreground = new SolidColorBrush(Color.Parse(TagColors.GetForegroundHex(tag)))
```

## Acceptance Criteria

- [x] Add `GetForegroundHex()` and `IsLightColor()` to `TagColors.cs`
- [x] Update `TaskListPopup.axaml.cs` to use dynamic foreground color
- [x] Light backgrounds (Lime, Cyan, Amber) get black text
- [x] Dark backgrounds (Blue, Red, Violet, Indigo) keep white text
- [x] All existing tests pass
- [x] Add unit tests for `GetForegroundHex()` and `IsLightColor()`

## Testing

```csharp
[Theory]
[InlineData("#84CC16", "#000000")] // Lime → black text
[InlineData("#06B6D4", "#000000")] // Cyan → black text
[InlineData("#F59E0B", "#000000")] // Amber → black text
[InlineData("#3B82F6", "#FFFFFF")] // Blue → white text
[InlineData("#EF4444", "#FFFFFF")] // Red → white text
[InlineData("#6366F1", "#FFFFFF")] // Indigo → white text
public void GetForegroundHex_ReturnsCorrectContrast(string bg, string expectedFg)
```
