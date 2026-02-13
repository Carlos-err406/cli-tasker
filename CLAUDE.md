# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

cli-tasker is a lightweight task manager with two interfaces: a CLI (`tasker` command) and a macOS menu bar app (Electron tray). Built with TypeScript, using a pnpm monorepo with SQLite (Drizzle ORM) for persistent storage.

**Monorepo structure:**
- `packages/core/` — shared core library (models, data layer, queries, undo system, backup)
- `apps/cli/` — CLI tool (Commander.js + Chalk)
- `apps/desktop/` — macOS menu bar app (Electron + React + Tailwind)

**Legacy C# codebase** (for reference only): `../cli-tasker-c#/`

Design docs, brainstorms, and plans live in `docs/`.

## Building and Testing

```bash
pnpm build               # Build all packages
pnpm test                 # Run all tests
pnpm typecheck            # Typecheck all packages
pnpm dev:desktop          # Run desktop app in dev mode

# Per-package
pnpm --filter @tasker/core run build    # Build core
pnpm --filter @tasker/core run test     # Test core
pnpm --filter @tasker/desktop run build # Build desktop
pnpm --filter @tasker/cli run build     # Build CLI
```

**Important:** When verifying new functionality, write tests instead of manual testing. Tests are repeatable, don't affect real data, and serve as documentation.

**Important:** After changing `@tasker/core` source, rebuild it (`pnpm --filter @tasker/core run build`) before the desktop or CLI can pick up the changes. The desktop dev server (`pnpm dev:desktop`) must be restarted to pick up core changes.

### Releasing

Release is done via `./release.sh <version>` (e.g. `./release.sh 3.1.0`). It tags the commit and pushes the tag, which triggers the GitHub Actions release workflow. Do NOT use `electron-builder` or `pnpm run package` directly.

### Interpreting task references

When the user provides 3 alphanumeric characters (e.g. `chj`, `a3f`, `1b2`), it's a tasker task ID. Run `tasker get <id>` to see the full task before proceeding.

### Working on tasks from the backlog

Always read the **full task description** — tasks often have multi-line descriptions with important context:

```bash
tasker get <taskId>           # Full description
tasker get <taskId> --json    # JSON output
tasker wip <taskId>           # Mark as in-progress when starting
tasker check <taskId>         # Mark as done when complete
```

**When a task has subtasks, blockers, or related tasks:** Ask the user if they want to work on those as well, or just the specified task. Don't assume — the user may only want the specific task they referenced.

## Reference Docs

### Models and Schema
Task type definition, TaskStatus enum, full SQLite schema, and result types.
→ `docs/reference/models-and-schema.md`

### Commands Reference
All CLI commands with their files, options, and patterns.
→ `docs/reference/commands.md`

### Inline Metadata Parsing
How `TaskDescriptionParser` parses priority, due dates, tags, and relationships from task descriptions.
→ `docs/reference/inline-metadata.md`

### Conventions
Task ordering, display formatting, cascade operations, undo system, sort order, and default list protection.
→ `docs/reference/conventions.md`

## Key Dependencies

| Package | Where | Purpose |
|---------|-------|---------|
| `drizzle-orm` | core | SQLite ORM |
| `libsql` (as better-sqlite3) | core | SQLite driver |
| `commander` | CLI | Command parsing |
| `chalk` | CLI | Terminal colors |
| `electron` | desktop | Desktop shell |
| `react` + `react-dom` | desktop | UI framework |
| `tailwindcss` | desktop | Styling |
| `@dnd-kit/sortable` | desktop | Drag-and-drop reordering |
| `chokidar` | desktop | DB file watcher |
| `vite-plugin-electron` | desktop | Electron + Vite integration |

## Architecture Notes

### Desktop sort order
The desktop app respects the user's manual drag-and-drop ordering (`sort_order` column). It uses `getAllTasks()` which returns `ORDER BY sort_order DESC`. The CLI uses `getSortedTasks()` which applies `sortTasksForDisplay()` (system sort by status/priority/due date) on top.

Operations like `renameTask`, `setTaskDueDate`, `setTaskPriority` must NOT call `bumpSortOrder` — that would move the task to the top of the list, overriding the user's manual ordering.

### Desktop IPC pipeline
Main process (Node.js) → preload (contextBridge) → renderer services → React store (`useReducer`).

Status changes use optimistic local updates (no `refresh()` call) to avoid re-sorting. Relationship status badges are updated locally in the `UPDATE_TASK_STATUS` reducer. Full `refresh()` happens on `popup:hidden` so re-sorting occurs while invisible.

## Maintaining This File

After completing tasks that change architecture, commands, models, or data layer:
1. Check if CLAUDE.md and the reference docs are still accurate
2. Suggest specific updates to the user
3. Common triggers: new commands, model field changes, schema migrations, new undo command types
