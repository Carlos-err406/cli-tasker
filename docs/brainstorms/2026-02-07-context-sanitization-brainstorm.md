# Context Sanitization — CLAUDE.md & README Rewrite

## What We're Building

Full rewrite of both `CLAUDE.md` and `README.md` to match the current state of the codebase. An audit revealed that both files are severely outdated — CLAUDE.md still describes a `bool IsChecked` model, and the README still says data is stored in JSON files.

## Why This Approach

Both files serve different audiences and need different treatment:

- **CLAUDE.md** is the AI agent reference — it needs accurate model definitions, method signatures, schema, and command docs at the current level of detail. Inaccurate docs here cause agents to write wrong code.
- **README.md** is the user/developer landing page — it needs a showcase section up top (features, install, usage) followed by developer reference docs (architecture, build, contribute).

A full rewrite is warranted because the gaps are too numerous for incremental patches — 8+ missing commands, wrong data model, missing features (TUI, dependencies, 3-state status), wrong storage description.

## Key Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Scope | Full rewrite of both files | Too many gaps for incremental fixes |
| CLAUDE.md detail level | Same depth as current | Code snippets + method signatures are what make it useful for agents |
| README style | Showcase + developer docs | Quick feature overview at top, technical docs below |
| Self-maintenance | Add instruction for agents | Tells agents to suggest CLAUDE.md updates after architecture changes |
| Visuals | Text-only for now | Focus on getting content accurate first |

## Audit Summary — What's Wrong

### CLAUDE.md Major Gaps

1. **TodoTask model** — still shows `bool IsChecked`, missing `TaskStatus Status`, `CompletedAt`, `ParentId`
2. **Schema** — missing `status` column, `completed_at`, `parent_id`, `task_dependencies` table
3. **Commands table** — missing 8+ commands: `status`, `wip`, `due`, `priority`, `init`, `backup`, `deps`, `undo`/`redo`/`history`
4. **TUI** — entirely undocumented (major user-facing feature)
5. **Task dependencies** — subtasks, blocking, cascade operations undocumented
6. **Directory auto-detection** — `--all`/`-a` option, `init` command undocumented
7. **Inline metadata parsing** — `p1`/`@date`/`#tag`/`^parent`/`!blocks` syntax undocumented
8. **Result types** — `TaskResult` discriminated union undocumented
9. **3-state status** — `Pending`/`InProgress`/`Done` replaces bool, not documented
10. **Multi-project structure** — CLI + TaskerCore + TaskerTray not described
11. **Task ordering** — documented as "unchecked first, then checked", now "InProgress > Pending > Done"
12. **Undo commands** — missing `SetStatusCommand`, `SetParentCommand`, `AddBlockerCommand`, `RemoveBlockerCommand`
13. **TodoTaskList operations** — missing `SetStatus()`, `SetParent()`, `AddBlocker()`, `GetSubtasks()`, `GetBlocks()`, `GetBlockedBy()`, `SearchTasks()`, etc.

### README.md Major Gaps

1. **Data storage** — says JSON files, actually SQLite
2. **Features list** — missing TUI, 3-state status, dependencies, undo/redo, backup, priorities, due dates, directory auto-detection, inline metadata
3. **Commands table** — missing 10+ commands
4. **No mention** of TaskerTray companion app
5. **Example output** — shows old checkbox format, missing `[-]` in-progress state

## CLAUDE.md Structure (Proposed)

```
# CLAUDE.md
## Project Overview (updated)
## Building and Testing (keep, minor updates)
## Architecture (v4.0 — major rewrite)
  ### Multi-Project Structure (NEW)
  ### SQLite Storage (update schema)
  ### Service Container (keep, minor updates)
  ### Command Flow (keep)
  ### TUI Architecture (NEW)
  ### TaskerTray Architecture (NEW)
## Data Layer (major rewrite)
  ### TaskerDb (keep)
  ### TodoTask (rewrite — new model)
  ### TaskStatus Enum (NEW)
  ### TodoTaskList (major update — new methods)
  ### Task Dependencies (NEW)
  ### ListManager (update)
  ### AppConfig (keep)
  ### BackupManager (keep)
  ### UndoManager (update — new commands)
  ### Result Types (NEW)
## Inline Metadata Parsing (NEW)
## Directory Auto-Detection (NEW)
## Output Formatting (update)
## Exception Hierarchy (keep)
## Commands Reference (major update)
## Important Implementation Details (update)
## Maintaining This File (NEW — self-maintenance instruction)
```

## README.md Structure (Proposed)

```
# cli-tasker
## Overview (1-2 sentences)
## Features (bullet list of highlights)
## Installation
## Quick Start (3-4 common commands)
## Usage
  ### CLI Commands (table)
  ### Interactive TUI
  ### Menu Bar App (TaskerTray)
  ### Inline Metadata Syntax
  ### Task Dependencies
  ### Directory Auto-Detection
## Architecture (brief overview)
## Building from Source
## License
```

## Open Questions

- None — scope and approach are clear.
