# Brainstorm: Recursive Get Flag

**Date:** 2026-02-07
**Task:** 800 — "add flag to tasker get --recursive"

## What We're Building

Add a `--recursive` flag to `tasker get` that traverses the full relationship graph (subtasks, blockers, blocked-by, related, parent) and displays a nested tree with full task details at each node.

Example: `tasker get abc --recursive` would show task `abc`, then indent and show full details for each related task, then indent further for *their* related tasks, and so on until the graph is exhausted.

## Why This Approach

- **Nested tree** over flat list — preserves the relationship structure visually, making it clear *how* tasks connect
- **Full get output** at each node — agents and users need complete context (status, tags, priority, due dates) to reason about task dependencies
- **No depth limit** — task graphs in this tool are small enough that unlimited traversal is practical. Keeps the API simple (no `--depth` flag to explain)
- **CLI only** — TUI and Tray already show relationships inline. The recursive flag is primarily for agents consuming `--json` and power users wanting the full picture
- **Cycle detection** via visited set — relationships can be bidirectional (related, blocks/blockedBy), so we must track visited task IDs to avoid infinite loops

## Key Decisions

1. **Nested tree output** — both human-readable and JSON show hierarchical structure
2. **Full task details** at each level — not truncated summaries
3. **No depth limit** — traverse the entire reachable graph
4. **CLI surface only** — `tasker get --recursive` (and `tasker get --recursive --json`)
5. **Cycle-safe** — track visited IDs, skip already-seen tasks with a marker like "(see above)"

## Open Questions

None — scope is clear.

## Next Steps

Run `/workflows:plan` when ready to implement.
