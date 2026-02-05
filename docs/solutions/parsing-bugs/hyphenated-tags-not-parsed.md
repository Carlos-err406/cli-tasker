---
title: "Tags with hyphens not parsed in task metadata"
category: parsing-bug
tags:
  - regex
  - metadata
  - tags
  - inline-parsing
  - character-class
module: TaskerCore/Parsing/TaskDescriptionParser
symptoms:
  - "Tags containing hyphens (e.g., #cli-only) are not recognized"
  - "LastLineIsMetadataOnly returns false when hyphenated tags are present"
  - "Metadata line with hyphenated tags is treated as regular text"
  - "Task metadata fields (Priority, DueDate, Tags) not populated from description"
date_solved: 2026-02-05
---

# Tags with Hyphens Not Parsed in Task Metadata

## Problem

Tags containing hyphens (like `#cli-only`, `#high-priority`, `#work-related`) were not being parsed correctly by `TaskDescriptionParser`. The metadata line detection failed, causing no metadata to be extracted at all.

## Symptoms

- Task created with `p3 #feature #cli-only` had `Priority: null` and `Tags: null`
- `LastLineIsMetadataOnly` returned `false` for lines with hyphenated tags
- The `-only` portion of `#cli-only` was left behind after regex replacement

## Root Cause

The `TagRegex` pattern used `#(\w+)` which only matches **word characters** (letters, digits, underscore). Hyphens are NOT word characters in regex.

**How the failure cascaded:**

```
Input last line: "p3 #feature #cli-only"

1. PriorityRegex replaces p3:     " #feature #cli-only"
2. TagRegex replaces #feature:    "  #cli-only"
3. TagRegex replaces #cli only:   "  -only"     ← "-only" remains!
4. IsNullOrWhiteSpace("  -only"): false         ← detection fails
5. Result: No metadata parsed at all
```

The regex `#(\w+)` only matched `#cli`, leaving `-only` as residual text.

## Solution

Changed the `TagRegex` pattern to include hyphens in the character class:

```csharp
// Before (broken):
[GeneratedRegex(@"#(\w+)")]
private static partial Regex TagRegex();

// After (fixed):
[GeneratedRegex(@"#([\w-]+)")]
private static partial Regex TagRegex();
```

**File:** `src/TaskerCore/Parsing/TaskDescriptionParser.cs:182`

## Verification

```
Input last line: "p3 #feature #cli-only"

1. PriorityRegex replaces p3:     " #feature #cli-only"
2. TagRegex replaces #feature:    "  #cli-only"
3. TagRegex replaces #cli-only:   "  "          ← full tag removed!
4. IsNullOrWhiteSpace("  "): true               ← detection succeeds
5. Result: Priority=Low, Tags=["feature", "cli-only"]
```

## Prevention

### When writing regex for user-facing parsers:

1. **Document valid characters first** - Know what characters users can type before writing regex
2. **Test boundary characters** - Hyphens, underscores, dots are common edge cases
3. **Verify input/output parity** - If users can type it, the parser should accept it

### Test cases to add:

```csharp
[Theory]
[InlineData("#simple", "simple")]
[InlineData("#with-hyphen", "with-hyphen")]
[InlineData("#multiple-hyphens-here", "multiple-hyphens-here")]
[InlineData("#mixed-case_123", "mixed-case_123")]
public void ParseTags_SupportsHyphensAndUnderscores(string input, string expected)
{
    var result = TaskDescriptionParser.Parse($"Task\n{input}");
    Assert.Contains(expected, result.Tags);
}
```

## Related

- `docs/solutions/feature-implementations/task-metadata-inline-system.md` - Main metadata system documentation (needs regex pattern updated)
- `docs/brainstorms/2026-02-04-task-metadata-brainstorm.md` - Original brainstorm mentioned tag character rules as open question
