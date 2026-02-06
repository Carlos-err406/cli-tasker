---
title: Tag colors changed on every app restart
category: ui-bugs
tags: [hashing, determinism, dotnet-core, color-assignment, djb2]
module: TaskerCore.Utilities.TagColors
date: 2026-02-05
severity: medium
symptoms:
  - Tag pill badges displayed different colors each time TaskerTray was restarted
  - Same tag had different colors across app sessions
  - Colors persisted during popup open/close cycles but not app restarts
  - "#feature" displayed blue, then green, then red on successive restarts
---

# Tag Colors Changed on Every App Restart

## Problem

Tag colors in TaskerTray were inconsistent across app restarts. The same tag (e.g., `#feature`) would display a different color each time the app was launched, even though colors remained stable during a single session.

## Root Cause

The original implementation used `string.GetHashCode()` to determine tag colors:

```csharp
// BROKEN: GetHashCode() is randomized per-process in .NET Core
var index = Math.Abs(tag.GetHashCode()) % colors.Length;
```

**Why this fails:** In .NET Core/.NET 5+, `string.GetHashCode()` is intentionally randomized per-process for security reasons (to prevent hash collision attacks). Each app launch generates a new random seed, causing the same string to produce different hash values across restarts.

This is documented behavior: https://learn.microsoft.com/en-us/dotnet/api/system.string.gethashcode#remarks

## Solution

Replace `string.GetHashCode()` with a deterministic hash algorithm (DJB2):

### New File: `src/TaskerCore/Utilities/TagColors.cs`

```csharp
namespace TaskerCore.Utilities;

public static class TagColors
{
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

    /// <summary>
    /// DJB2 hash - deterministic across process restarts.
    /// </summary>
    private static int GetDeterministicHash(string str)
    {
        unchecked
        {
            int hash = 5381;
            foreach (char c in str)
            {
                hash = ((hash << 5) + hash) ^ c;
            }
            return hash;
        }
    }
}
```

### Update Consumers

**TaskListPopup.axaml.cs:**
```csharp
// Before
var index = Math.Abs(tag.GetHashCode()) % colors.Length;

// After
return Color.Parse(TagColors.GetHexColor(tag));
```

**Output.cs and TuiRenderer.cs:**
```csharp
// Before: All tags were cyan
return $"  [cyan]{Markup.Escape(tagStr)}[/]";

// After: Each tag gets its deterministic color
var formatted = tags.Select(t =>
    $"{TagColors.GetSpectreMarkup(t)}#{Markup.Escape(t)}[/]");
return "  " + string.Join(" ", formatted);
```

## Why DJB2 Works

DJB2 is a simple, fast, non-cryptographic hash algorithm that:

1. **Is deterministic** - Same input always produces same output (fixed seed of 5381)
2. **Has good distribution** - Spreads values evenly across the palette
3. **Is fast** - O(n) with minimal operations
4. **Is well-tested** - Used widely for hash tables and checksums

The `unchecked` keyword allows integer overflow without exceptions, which is intentional for hash algorithms.

## Prevention

### When to use `string.GetHashCode()`
- Hash tables within a single process lifetime
- Dictionary keys (correct usage)
- Performance-critical in-memory lookups

### When NOT to use `string.GetHashCode()`
- Persisting hash values to disk
- Cross-process communication
- Deterministic color/ID assignment
- Anything that must be consistent across app restarts

### Alternative Deterministic Hashes
- **DJB2** - Simple, fast, good for short strings (used here)
- **FNV-1a** - Similar to DJB2, slightly different distribution
- **xxHash** - Faster for large inputs, needs NuGet package
- **SHA256** - Cryptographic, overkill for colors but 100% deterministic

## Testing

```csharp
[Fact]
public void GetHexColor_SameTag_ReturnsSameColor()
{
    var color1 = TagColors.GetHexColor("feature");
    var color2 = TagColors.GetHexColor("feature");
    Assert.Equal(color1, color2);
}

[Theory]
[InlineData("feature")]
[InlineData("bug")]
[InlineData("emoji🎉")]
public void GetHexColor_IsDeterministic_ForVariousTags(string tag)
{
    var firstResult = TagColors.GetHexColor(tag);
    for (var i = 0; i < 10; i++)
    {
        Assert.Equal(firstResult, TagColors.GetHexColor(tag));
    }
}
```

## Related Documentation

- [Task Metadata Inline System](../feature-implementations/task-metadata-inline-system.md) - Original tag color implementation
- [Microsoft Docs: String.GetHashCode](https://learn.microsoft.com/en-us/dotnet/api/system.string.gethashcode) - Official documentation on hash randomization
