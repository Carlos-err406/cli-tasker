# Tag Colors Feature Analysis

**Feature**: Consistent hash-based tag colors across CLI, TUI, and TaskerTray
**Date**: 2026-02-05
**Status**: Ready for decision phase
**Analyst**: Claude Code - UX Flow Analyst

---

## Quick Navigation

Start here based on your role:

### For Product/Designers
- **Read First**: [`TAG_COLORS_EXECUTIVE_SUMMARY.md`](./TAG_COLORS_EXECUTIVE_SUMMARY.md) (5 min)
  - Problem statement
  - 11 user flows affected
  - 4 critical blockers
  - Acceptance criteria

- **Then**: [`TAG_COLORS_DECISION_MATRIX.md`](./TAG_COLORS_DECISION_MATRIX.md) (10 min)
  - 10 key decisions with options
  - Risk/effort tradeoffs
  - Recommended configuration

### For Developers
- **Read First**: [`TAG_COLORS_EXECUTIVE_SUMMARY.md`](./TAG_COLORS_EXECUTIVE_SUMMARY.md) (5 min)
  - Quick overview of scope

- **Then**: [`TAG_COLORS_VISUAL_FLOWS.md`](./TAG_COLORS_VISUAL_FLOWS.md) (10 min)
  - Visual diagrams of all flows
  - Architecture before/after
  - Implementation roadmap

- **Detailed Reference**: [`2026-02-05-tag-colors-ux-flow-analysis.md`](./2026-02-05-tag-colors-ux-flow-analysis.md)
  - Complete specification gaps
  - Edge case scenarios
  - Code references with line numbers

### For QA/Testing
- **Read First**: [`TAG_COLORS_VISUAL_FLOWS.md`](./TAG_COLORS_VISUAL_FLOWS.md) - Flow 11 (Testing section)
  - Unit test scenarios
  - Integration test scenarios
  - Manual test checklist

- **Then**: [`2026-02-05-tag-colors-ux-flow-analysis.md`](./2026-02-05-tag-colors-ux-flow-analysis.md) - Part 7 (Edge Cases)
  - 18 edge cases to test
  - Platform-specific scenarios

---

## Document Overview

### 1. TAG_COLORS_EXECUTIVE_SUMMARY.md (6.9 KB)

**Purpose**: High-level overview for decision makers

**Contents**:
- Quick problem statement
- 11 user flows (1-sentence each)
- 18 edge case categories
- 4 critical blockers with assumptions
- 5 important questions
- Implementation scope and effort estimate
- Success criteria

**Best For**: Quick briefing, stakeholder alignment, getting to "what do we need to decide?"

**Time to Read**: 5-10 minutes

---

### 2. TAG_COLORS_VISUAL_FLOWS.md (28 KB)

**Purpose**: Visual diagrams and implementation guidance

**Contents**:
- 12 flow diagrams (ASCII art):
  1. Add task with tags (all platforms)
  2. View tasks before/after implementation
  3. Color assignment algorithm
  4. Consistency guarantee
  5. Hash collision scenario
  6. Checked task coloring ambiguity
  7. Search highlighting interaction
  8. Multi-select mode interaction
  9. Code architecture (before/after)
  10. Data flow storage to render
  11. Test scenarios
  12. Implementation steps (detailed)

**Best For**: Understanding architecture, planning implementation, designing tests

**Time to Read**: 15-20 minutes

---

### 3. TAG_COLORS_DECISION_MATRIX.md (17 KB)

**Purpose**: Structure decisions with pros/cons and tradeoffs

**Contents**:
- 10 decision points with options:
  1. Hash collision tolerance (accept vs prevent)
  2. Terminal color fallback (true-color only vs degradation)
  3. Hash algorithm stability (GetHashCode vs SHA256)
  4. Shared utility location (3 options)
  5. Completed task coloring (dimmed vs bright)
  6. Search highlighting priority (tag color vs search highlight)
  7. Color-blind support (none vs high-contrast vs config)
  8. User configuration (fixed vs theme vs customizable)
  9. Real-time preview (post-save vs live)
  10. Palette size (10 vs 20 vs 30+ colors)

- 3 complete configurations:
  - **A**: Recommended (low effort, low risk, 4 hours)
  - **B**: Maximum accessibility (high effort, medium risk, 12-16 hours)
  - **C**: Minimal implementation (ultra-low effort, 2-3 hours)

**Best For**: Decision-making, tradeoff analysis, choosing configuration

**Time to Read**: 15-20 minutes

---

### 4. 2026-02-05-tag-colors-ux-flow-analysis.md (34 KB)

**Purpose**: Exhaustive specification and gap analysis

**Contents**:
- **Part 1-2**: 11 user flows with detailed steps
  - Add task with tags (Flow 1)
  - View tasks - CLI (Flow 2)
  - View tasks - TUI (Flow 3)
  - View tasks - TaskerTray (Flow 4)
  - View task details (Flow 5)
  - Add task with tags - TUI/TaskerTray (Flows 6-7)
  - Search/filter by tags (Flow 8)
  - Edit task tags (Flows 9-11)

- **Part 3**: Flow permutations matrix (12 variations)

- **Part 4**: Missing elements & gaps (7 categories, 18 gaps)
  1. Color mapping & translation (3 gaps)
  2. Edge cases - tag content (5 gaps)
  3. Rendering & display context (4 gaps)
  4. Data persistence & migration (2 gaps)
  5. Palette & design specification (3 gaps)
  6. Implementation & integration (3 gaps)
  7. Acceptance & testing (2 gaps)

- **Part 5**: Critical questions prioritized
  - 4 Critical (blocking)
  - 5 Important (affects UX/maintainability)
  - 3 Nice-to-have

- **Part 6**: Recommended next steps (10 steps with code examples)

- **Part 7**: Edge case scenarios (14 specific test cases)

- **Part 8**: Specification summary

- **Appendix**: Code references with file paths and line numbers

**Best For**: Detailed specification, gap identification, comprehensive reference

**Time to Read**: 30-45 minutes (full), 10 minutes (sections)

---

## Key Findings

### What's Already Working ✓

TaskerTray has **correct implementation** (lines 1544-1563 of TaskListPopup.axaml.cs):
```csharp
private static Color GetTagColor(string tag)
{
    var colors = new[] { "#3B82F6", "#10B981", ... };  // 10 colors
    var index = Math.Abs(tag.GetHashCode()) % colors.Length;
    return Color.Parse(colors[index]);
}
```

This is the **baseline to replicate** in CLI/TUI.

### What Needs Fixing

- CLI: `Output.FormatTags()` returns flat cyan for all tags
- TUI: `TuiRenderer.FormatTags()` returns flat cyan for all tags
- Need shared utility to avoid code duplication

### Critical Blockers (Must Answer Before Code)

| Q | Decision | Impact |
|---|----------|--------|
| Q1 | Hash collisions acceptable? | Determines palette size |
| Q2 | Terminal color fallback? | Determines compatibility |
| Q3 | Deterministic hash needed? | Affects consistency over time |
| Q4 | Shared utility location? | Affects code organization |

See [`TAG_COLORS_DECISION_MATRIX.md`](./TAG_COLORS_DECISION_MATRIX.md) for full analysis.

### Recommended Path Forward

**If answers follow recommendations**:
- Effort: 4 hours code + 1-2 hours testing
- Risk: Low (no architecture changes)
- Complexity: Low
- Breaking Changes: None

**If additional requirements**: See alternative configurations in decision matrix.

---

## How to Use This Analysis

### Scenario 1: Get quick decision criteria

1. Read [`TAG_COLORS_EXECUTIVE_SUMMARY.md`](./TAG_COLORS_EXECUTIVE_SUMMARY.md)
2. Identify which questions are ambiguous
3. Go to [`TAG_COLORS_DECISION_MATRIX.md`](./TAG_COLORS_DECISION_MATRIX.md) for that question
4. Choose option that fits your constraints

### Scenario 2: Plan implementation

1. Read [`TAG_COLORS_VISUAL_FLOWS.md`](./TAG_COLORS_VISUAL_FLOWS.md) Flow 12 (Implementation Steps)
2. Review code architecture diagram (Flow 9)
3. Reference code locations in [`2026-02-05-tag-colors-ux-flow-analysis.md`](./2026-02-05-tag-colors-ux-flow-analysis.md) Appendix
4. Follow step-by-step implementation with code examples

### Scenario 3: Identify edge cases for testing

1. Read [`2026-02-05-tag-colors-ux-flow-analysis.md`](./2026-02-05-tag-colors-ux-flow-analysis.md) Part 7 (Edge Cases)
2. Review test scenarios in [`TAG_COLORS_VISUAL_FLOWS.md`](./TAG_COLORS_VISUAL_FLOWS.md) Flow 11
3. Cross-reference specific flows (Flows 1-11) for context

### Scenario 4: Understand all user impacts

1. Read [`2026-02-05-tag-colors-ux-flow-analysis.md`](./2026-02-05-tag-colors-ux-flow-analysis.md) Flows 1-11
2. Review Part 3 (Flow Permutations Matrix)
3. Check Part 4 for ambiguities in your areas of concern

---

## Key Terminology

- **Hash-to-palette**: Algorithm that maps tag string to one of N colors via hash function
- **GetHashCode()**: .NET method that returns integer hash; platform/runtime-specific
- **Collision**: When two different tags get same color (happens with small palette)
- **True-color terminal**: Supports 24-bit RGB colors (modern terminals)
- **Degradation**: Fallback behavior for limited-color terminals
- **ANSI escape codes**: Terminal color codes like `\x1b[31m` for red
- **Markup**: Spectre.Console format like `[#RRGGBB]text[/]` for colors
- **Per-tag coloring**: Each tag gets its own color vs all tags same color

---

## Document Statistics

| Document | Size | Word Count | Time to Read |
|----------|------|-----------|--------------|
| Executive Summary | 6.9 KB | 2,100 | 5-10 min |
| Visual Flows | 28 KB | 3,500 | 15-20 min |
| Decision Matrix | 17 KB | 3,200 | 15-20 min |
| Full Analysis | 34 KB | 8,500 | 30-45 min |
| **Total** | **86 KB** | **17,300** | **60-90 min** |

---

## File Locations

**Analysis Documents**:
- `/Users/carlos/self-development/cli-tasker/docs/analysis/README.md` (this file)
- `/Users/carlos/self-development/cli-tasker/docs/analysis/TAG_COLORS_EXECUTIVE_SUMMARY.md`
- `/Users/carlos/self-development/cli-tasker/docs/analysis/TAG_COLORS_VISUAL_FLOWS.md`
- `/Users/carlos/self-development/cli-tasker/docs/analysis/TAG_COLORS_DECISION_MATRIX.md`
- `/Users/carlos/self-development/cli-tasker/docs/analysis/2026-02-05-tag-colors-ux-flow-analysis.md`

**Related Code** (existing):
- `/Users/carlos/self-development/cli-tasker/Output.cs` (lines 33-38)
- `/Users/carlos/self-development/cli-tasker/Tui/TuiRenderer.cs` (lines ~170)
- `/Users/carlos/self-development/cli-tasker/src/TaskerTray/Views/TaskListPopup.axaml.cs` (lines 1544-1563)

---

## Next Steps

1. **Share documents** with product/design and development team
2. **Get answers** to 4 critical blockers (Q1-Q4) in decision matrix
3. **Choose configuration** (Recommended A, Accessibility B, or Minimal C)
4. **Implement** following step-by-step guidance in Visual Flows
5. **Test** using edge case scenarios in full analysis

---

## Questions?

Each document includes:
- Clear problem statements
- Specific questions to resolve ambiguities
- Code examples and references
- Visual diagrams
- Risk/effort assessments
- Implementation guidance

If unclear on any aspect, reference the specific flow or gap in the relevant document.

---

**Analysis Date**: 2026-02-05
**Status**: Ready for decision phase
**Next Review**: After decisions made; before implementation starts
