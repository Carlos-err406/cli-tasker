---
title: Consistent Tag Colors
date: 2026-02-05
status: ready-for-planning
task: "076"
---

# Consistent Tag Colors

## What We're Building

A system where the same tag text always renders with the same color across CLI, TUI, and TaskerTray. Colors are auto-generated from the tag text using a hash-to-palette approach.

**Example:**
- `#feature` → always coral red
- `#bug` → always teal
- `#ui` → always purple

No user configuration needed - colors are deterministic based on tag text.

## Why This Approach

**Hash-to-Palette** was chosen over Hash-to-HSL because:

1. **Guaranteed aesthetics** - Hand-picked colors ensure readability on dark backgrounds
2. **Simplicity** - Just `hash % palette.length`, no color space math
3. **Cross-platform** - Easy to map palette colors to both Spectre markup and Avalonia brushes
4. **YAGNI** - 10-12 colors is plenty; unlimited colors adds complexity without clear benefit

## Key Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Color source | Auto-generated from hash | Zero config, always consistent |
| Algorithm | Hash-to-Palette index | Simple, predictable, good-looking |
| Palette size | 10-12 colors | Enough variety, low collision risk |
| Scope | CLI + TUI + TaskerTray | Consistent experience everywhere |
| Customization | None | YAGNI - can add later if needed |

## Current State

Tags are currently rendered in **cyan** everywhere:
- CLI: `Output.FormatTags()` → `[cyan]#{tag}[/]`
- TUI: `TuiRenderer.cs` → same cyan markup
- TaskerTray: No explicit tag coloring (inherits text color)

## Implementation Notes

### Shared Logic Location

Create `TagColors.cs` in TaskerCore with:
- A curated palette array (hex strings)
- `GetColorForTag(string tag)` returning hex color
- Simple hash: `Math.Abs(tag.GetHashCode()) % palette.Length`

### Per-Platform Mapping

| Platform | Color Format | Mapping |
|----------|--------------|---------|
| CLI | Spectre markup | Map hex to closest named color OR use `[#{hex}]` |
| TUI | Spectre markup | Same as CLI |
| TaskerTray | Avalonia Brush | `SolidColorBrush.Parse(hex)` |

### Suggested Palette (Dark Theme Friendly)

```
#FF6B6B - Coral Red
#4ECDC4 - Teal
#45B7D1 - Sky Blue
#96CEB4 - Sage Green
#FFEAA7 - Soft Yellow
#DDA0DD - Plum
#98D8C8 - Mint
#F7DC6F - Gold
#BB8FCE - Lavender
#85C1E9 - Light Blue
#F8B500 - Amber
#58D68D - Emerald
```

## Open Questions

None - design is straightforward.

## Next Steps

Run `/workflows:plan` to create implementation plan.
