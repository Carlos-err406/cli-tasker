---
title: fix: Preserve line breaks in TaskerTray description
type: fix
date: 2026-02-05
---

# Fix: Preserve Line Breaks in TaskerTray Description

## Problem

Task descriptions in TaskerTray lose their line breaks - all lines after the title are joined with spaces instead of newlines.

## Root Cause

In `TodoTaskViewModel.GetDescriptionPreview()`:
```csharp
return string.Join(" ", lines.Skip(1)...);  // Joins with space, not newline
```

## Solution

Change the join separator from `" "` to `"\n"`.

### TodoTaskViewModel.cs

```csharp
// Before:
return string.Join(" ", lines.Skip(1).Select(l => l.Trim()).Where(l => !string.IsNullOrEmpty(l)));

// After:
return string.Join("\n", lines.Skip(1).Select(l => l.Trim()).Where(l => !string.IsNullOrEmpty(l)));
```

## Acceptance Criteria

- [ ] Multi-line descriptions show each line on a separate line
- [ ] Empty lines are still filtered out (existing behavior)
- [ ] Line trimming still applied (existing behavior)

## Files to Modify

| File | Changes |
|------|---------|
| `src/TaskerTray/ViewModels/TodoTaskViewModel.cs` | Change `string.Join(" ",` to `string.Join("\n",` |
