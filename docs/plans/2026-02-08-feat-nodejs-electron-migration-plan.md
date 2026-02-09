---
title: "feat: Migrate cli-tasker to Node.js, Electron, Commander.js, React"
type: feat
date: 2026-02-08
---

# feat: Migrate cli-tasker to Node.js, Electron, Commander.js, React

## Overview

Greenfield rewrite of cli-tasker from .NET/C# to a TypeScript-based stack. Two surfaces: CLI (Commander.js, for humans and agents) and desktop tray app (Electron + React + shadcn/ui). The TUI is dropped. The existing SQLite database and schema are preserved — existing user data works immediately. Version jumps from 2.42.4 to 3.0.0.

## Problem Statement

The current C# codebase works well but creates friction:
1. **Developer familiarity** — JS/TS/React is a more comfortable development environment
2. **Cross-platform** — Electron provides macOS, Windows, and Linux from one codebase (Avalonia only targets macOS currently)
3. **Ecosystem** — npm has richer component libraries, faster iteration, and broader tooling

The TUI was a stepping stone to the desktop app. Now that TaskerTray exists, humans use the tray app and agents use the CLI. The TUI serves neither audience well enough to justify porting.

## Proposed Solution

A pnpm monorepo with three packages:

| Package | Role |
|---------|------|
| `@tasker/core` (`shared/core/`) | Models, Drizzle schema, queries, parsers, undo system |
| `@tasker/cli` (`apps/cli/`) | Commander.js commands, chalk output formatting |
| `@tasker/desktop` (`apps/desktop/`) | Electron + React + shadcn/ui tray app |

## Technical Approach

### Architecture

```
tasker/
├── shared/
│   └── core/                  # @tasker/core
│       ├── src/
│       │   ├── schema/        # Drizzle table definitions (introspected)
│       │   ├── queries/       # TaskQueries, ListQueries, UndoQueries, etc.
│       │   ├── types/         # Task, NewTask, TaskStatus, Priority, Result<T>
│       │   ├── ipc/           # IPC contracts (shared type-safe channel defs)
│       │   │   └── contracts.ts
│       │   ├── undo/          # Discriminated union command types + handlers
│       │   ├── parsers/       # TaskDescriptionParser, DateParser
│       │   └── db.ts          # Connection factory (WAL, FK pragmas)
│       ├── drizzle/           # Generated migrations
│       ├── package.json
│       ├── tsconfig.json
│       └── vitest.config.ts
├── apps/
│   ├── cli/                   # @tasker/cli
│   │   ├── src/
│   │   │   ├── commands/      # One file per command (mirrors AppCommands/)
│   │   │   ├── output.ts      # chalk-based formatting (mirrors Output.cs)
│   │   │   ├── helpers.ts     # Error handling, list resolution
│   │   │   └── index.ts       # Commander program setup
│   │   ├── package.json       # bin: { "tasker": "./dist/index.js" }
│   │   ├── tsconfig.json
│   │   └── vitest.config.ts
│   └── desktop/               # @tasker/desktop
│       ├── electron/
│       │   ├── main.ts
│       │   ├── preload.ts
│       │   ├── electron-env.d.ts
│       │   ├── lib/
│       │   │   ├── index.ts
│       │   │   ├── window.ts
│       │   │   ├── tray.ts
│       │   │   └── config.ts
│       │   └── ipc/           # Modular IPC channels
│       │       ├── register.ts
│       │       ├── types.d.ts
│       │       ├── tasks/
│       │       │   ├── channels.ts
│       │       │   ├── main.ts
│       │       │   ├── preload.ts
│       │       │   └── utils.ts
│       │       ├── lists/
│       │       ├── undo/
│       │       └── window/
│       ├── src/               # Renderer (React)
│       │   ├── main.tsx
│       │   ├── app.tsx
│       │   ├── styles.css     # Tailwind V4 + CSS variables
│       │   ├── components/
│       │   │   ├── TaskList.tsx
│       │   │   ├── TaskItem.tsx
│       │   │   ├── QuickAdd.tsx
│       │   │   ├── SearchBar.tsx
│       │   │   └── ui/        # shadcn/ui components
│       │   └── lib/
│       │       ├── services/  # IPC wrapper functions
│       │       └── utils.ts   # cn() helper
│       ├── vite.config.ts
│       └── electron-builder.json5
├── pnpm-workspace.yaml
├── .npmrc
├── tsconfig.base.json
├── tsconfig.json              # Solution references
└── vitest.workspace.ts
```

### Stack Mapping

| Layer | C# Current | TypeScript New |
|-------|-----------|----------------|
| Language | C# (.NET 10.0) | TypeScript (ES2022, strict) |
| Core library | `TaskerCore/` | `@tasker/core` (shared/core) |
| CLI framework | System.CommandLine | Commander.js |
| Desktop app | Avalonia (macOS) | Electron + React |
| UI components | — | shadcn/ui (Radix-based) |
| Styling | — | Tailwind CSS V4 (CSS vars, no config, no PostCSS) |
| Database | Microsoft.Data.Sqlite | better-sqlite3 via Drizzle ORM |
| ORM | Raw SQL | Drizzle ORM |
| Test framework | xUnit | Vitest |
| Build | MSBuild / dotnet CLI | Vite + pnpm workspaces |
| Distribution | dotnet global tool | npm global package + electron-builder |

### Type Mappings

| C# Pattern | TypeScript Equivalent |
|-------------|----------------------|
| `record TodoTask(...)` | `interface Task` with `readonly` fields |
| `enum TaskStatus` | `const enum TaskStatus` or `type TaskStatus = 0 \| 1 \| 2` |
| `TaskResult` (Success/NotFound/NoChange/Error) | `Result<T>` discriminated union: `{ success: true, data: T }` / `{ success: false, error: string }` |
| `IUndoableCommand` with `[JsonDerivedType]` | Discriminated union with `type` field |
| LINQ queries | Array methods (`.filter()`, `.map()`, `.sort()`) |
| `ImmutableArray<T>` mutation methods | Spread + override: `{ ...task, status: 2 }` |

### Implementation Phases

#### Phase 1: Monorepo Scaffold + Core/Schema

Set up the project skeleton and port the data layer — everything depends on this.

**Tasks:**

- [ ] Initialize pnpm monorepo with workspace config
  - `pnpm-workspace.yaml`: `packages: ['shared/*', 'apps/*']`
  - `.npmrc`: `link-workspace-packages=true`, `auto-install-peers=true`
  - Root `package.json` with workspace scripts
- [ ] Create `tsconfig.base.json` with shared settings
  - ES2022 target, strict mode, composite, declaration, bundler moduleResolution
- [ ] Create `shared/core/` package scaffold
  - `package.json` with `"name": "@tasker/core"`, exports map
  - `tsconfig.json` extending base
- [ ] Introspect existing SQLite schema with `drizzle-kit pull`
  - Point at `~/Library/Application Support/cli-tasker/tasker.db`
  - Generate `src/schema/` files: `tasks.ts`, `lists.ts`, `taskDependencies.ts`, `config.ts`, `undoHistory.ts`
- [ ] Create database connection factory (`src/db.ts`)
  - `createDb(path?: string)` — defaults to platform-appropriate path
  - Enable WAL mode: `db.run('PRAGMA journal_mode = WAL')`
  - Enable foreign keys: `db.run('PRAGMA foreign_keys = ON')`
  - Export Drizzle instance wrapping better-sqlite3
- [ ] Define TypeScript types (`src/types/`)
  - `Task` interface (from Drizzle `$inferSelect`)
  - `NewTask` type (from Drizzle `$inferInsert`)
  - `TaskStatus` enum: `{ Pending = 0, InProgress = 1, Done = 2 }`
  - `Priority` enum: `{ High = 1, Medium = 2, Low = 3 }`
  - `Result<T>` discriminated union
  - `BatchResult<T>` for multi-ID operations
- [ ] Port `TaskDescriptionParser` to `src/parsers/taskDescriptionParser.ts`
  - Last-line-only metadata parsing: `p1`/`p2`/`p3`, `@date`, `#tag`, `^parentId`, `!blockerId`
  - `getDisplayDescription()` — hides metadata-only last line
  - `syncMetadataToDescription()` — updates metadata line when properties change
  - **Gotcha:** Use `[\w-]+` regex for tags (not `\w+`) — hyphenated tags must parse correctly
  - **Gotcha:** Preserve raw date text alongside resolved value for re-evaluation on rename
- [ ] Port `DateParser` to `src/parsers/dateParser.ts`
  - Natural language: today, tomorrow, weekday names, monthDay (jan15), relative (+3d), ISO
  - Return `DateOnly` equivalent (date string, no time component)
- [ ] Port data access layer to `src/queries/`
  - `taskQueries.ts` — CRUD, batch operations, sort order management
  - `listQueries.ts` — list CRUD, default list protection ("tasks" list cannot be deleted/renamed)
  - `dependencyQueries.ts` — parent/child, blockers, circular detection
  - `configQueries.ts` — key-value config storage
  - `trashQueries.ts` — soft delete, restore, clear
  - **Sort order convention:** Highest `sort_order` = newest, display uses `ORDER BY sort_order DESC`
  - **Task ordering:** InProgress first, then Pending, then Done. Active sorted by priority → due date → newest. Done sorted by `CompletedAt DESC`
  - **Cascade operations:** SetStatus(Done) cascades to descendants, Delete trashes descendants, Move moves descendants, Restore restores descendants
  - **Don't bump `sort_order` on status changes** — sort on open, not during interaction
  - **Directory auto-detection:** `resolveListFilter(cwd)` — match directory name to list name, `--all` bypasses
  - **Default list protection:** "tasks" list cannot be deleted or renamed
- [ ] Port undo system to `src/undo/`
  - `types.ts` — discriminated union with `type` field for all 17 command types
  - `undoManager.ts` — two-stack architecture (undo/redo) stored in SQLite via Drizzle
  - `handlers/` — one handler per command type with `execute()` and `undo()` methods
  - `composite.ts` — batch operations using `beginBatch()` / `endBatch()`
  - **Gotcha:** Use `recordUndo: false` parameter when calling from undo/redo to prevent recursion
  - **Gotcha:** Capture state BEFORE modification for undo data
  - JSON serialization compatible with existing `undo_history` table format
- [ ] Port backup system
  - Use better-sqlite3 `.backup()` method (maps to SQLite hot backup API)
  - Atomic writes: write to `.tmp` then rename
- [ ] Set up Vitest workspace
  - `vitest.workspace.ts` at root: `['shared/*/vitest.config.ts', 'apps/*/vitest.config.ts']`
  - Core tests: `environment: 'node'`, in-memory SQLite (`:memory:`)
  - Test isolation: unique temp dirs per test, never hardcode production paths, cleanup in afterEach
- [ ] Write core tests (port from 24 existing xUnit test files)
  - Schema validation tests
  - TaskDescriptionParser tests (metadata parsing, display description, sync)
  - DateParser tests (all date formats)
  - Query tests (CRUD, batch, sort order, cascades)
  - Undo tests (all 17 command types, composite operations, redo)
  - Backup tests

**Success criteria:** `pnpm test --filter @tasker/core` passes, Drizzle schema matches existing DB, all query operations work against in-memory SQLite.

#### Phase 2: CLI Commands

Port all 17 commands + undo/redo/history using Commander.js.

**Tasks:**

- [ ] Create `apps/cli/` package scaffold
  - `package.json` with `"name": "@tasker/cli"`, `"bin": { "tasker": "./dist/index.js" }`, dependency on `"@tasker/core": "workspace:*"`
  - `tsconfig.json` extending base with reference to `../../shared/core`
- [ ] Set up Commander.js program (`src/index.ts`)
  - Version: 3.0.0
  - Global options: `-l, --list <name>`, `-a, --all`
  - Error handling wrapper (mirrors `CommandHelper.WithErrorHandling()`)
- [ ] Port output formatting (`src/output.ts`)
  - chalk-based formatting matching current Spectre.Console output
  - Checkboxes: green `[x]` (done), yellow `[-]` (in-progress), grey `[ ]` (pending)
  - Priority indicators: red `>>>` (high), yellow `>>` (medium), blue `>` (low)
  - Relationship indicators: `↑ Subtask of`, `↳ Subtask`, `⊘ Blocks`, `⊘ Blocked by`
  - Due date formatting with overdue highlighting
  - Completed due dates: frozen at completion time
- [ ] Port commands (one file per command in `src/commands/`)
  - `add.ts` — `tasker add <description> [-l list]` — uses default list if no `-l`, inline metadata parsing
  - `list.ts` — `tasker list [-l] [-c] [-u] [-p priority] [--overdue] [--all]`
  - `check.ts` + `uncheck.ts` — `tasker check <ids...>` / `tasker uncheck <ids...>` — multiple IDs, cascade to descendants
  - `delete.ts` + `clear.ts` — `tasker delete <ids...>` / `tasker clear [-l list] [--all]` — multiple IDs, cascade
  - `status.ts` + `wip.ts` — `tasker status <ids...> <status>` / `tasker wip <ids...>` — wip = shortcut for in-progress
  - `rename.ts` — `tasker rename <id> <description>` — single task, re-parses metadata, preserves raw date markers
  - `get.ts` — `tasker get <id> [--json] [-r/--recursive]` — shows relationships, full tree
  - `move.ts` — `tasker move <ids...> <list>` — validates target list, moves descendants
  - `due.ts` — `tasker due <id> <date> [--clear]`
  - `priority.ts` — `tasker priority <id> high|medium|low|none`
  - `lists.ts` — `tasker lists`, `tasker lists create|delete|rename|set-default` — subcommands
  - `trash.ts` — `tasker trash list|restore|clear [-l] [--all]` — subcommands
  - `system.ts` — `tasker system status` — per-list statistics
  - `init.ts` — `tasker init` — creates list named after current directory
  - `backup.ts` — `tasker backup list|restore` — subcommands
  - `deps.ts` — `tasker deps set-parent|unset-parent|add-blocker|remove-blocker` — subcommands
  - `undo.ts` — `tasker undo`, `tasker redo`, `tasker history`
- [ ] Implement directory auto-detection in `src/helpers.ts`
  - `resolveListFilter(options, cwd)` — match `process.cwd()` directory name to list name
  - `--all` flag bypasses auto-detection
- [ ] Write CLI integration tests
  - Test command parsing (arguments, options, flags)
  - Test output formatting
  - Test each command against in-memory SQLite
  - Test batch operations (multiple IDs)
  - Test directory auto-detection
  - Test error handling (not found, no change, invalid input)

**Success criteria:** All 17 commands + undo/redo/history work. `pnpm test --filter @tasker/cli` passes. `tasker list` produces output matching the C# version. Existing task data loads correctly.

#### Phase 3: Desktop App (Electron + React + shadcn)

Build the tray app following `currency-exchange-widget` architecture.

**Tasks:**

- [ ] Create `apps/desktop/` package scaffold
  - `package.json` with `"name": "@tasker/desktop"`, dependency on `"@tasker/core": "workspace:*"`
  - `vite.config.ts` with `vite-plugin-electron`, `better-sqlite3` marked as external
  - `electron-builder.json5` — macOS: `LSUIElement: true`, dmg + zip targets
  - `tsconfig.json` — separate configs for renderer (JSX) and main process (Node)
- [ ] Set up Electron main process (`electron/main.ts`)
  - App lifecycle: `app.dock.hide()` on macOS
  - Single instance lock
  - Create tray icon and popup window
- [ ] Set up Electron preload (`electron/preload.ts`)
  - `contextBridge.exposeInMainWorld('taskerAPI', { ... })`
  - Sandbox enabled, no `nodeIntegration`
  - Type definitions in `electron-env.d.ts`
- [ ] Implement Electron lib (`electron/lib/`)
  - `window.ts` — frameless BrowserWindow, popup positioning, blur-to-hide
  - `tray.ts` — tray icon, toggle, context menu
  - `config.ts` — window position/size persistence
- [ ] Implement modular IPC channels (`electron/ipc/`)
  - `tasks/` — CRUD operations, batch operations, status changes, reorder
  - `lists/` — list CRUD, collapse/expand, default management
  - `undo/` — undo, redo, history
  - `window/` — resize, drag, position
  - Central `register.ts` combining all channel registrations
  - IPC type definitions in `types.d.ts`
  - **Pattern per feature dir:** `channels.ts` (string constants), `main.ts` (handlers + registration), `preload.ts` (invoker factory), `utils.ts` (helpers)
  - **Serialization:** Use timestamps not Date objects, `null` not `undefined`, plain objects not class instances
  - **Result pattern:** Return `Result<T>` objects, never throw across IPC boundary
  - **Broadcasting:** `BrowserWindow.getAllWindows().forEach(win => win.webContents.send(channel, data))` for main→renderer events
- [ ] Set up IPC contracts in `@tasker/core`
  - `shared/core/src/ipc/contracts.ts` — channel names, parameter types, return types
  - Type-safe handler helper: `handleIpc<T>(channel, handler)` enforces correct types
  - Type-safe invoke helper: `invoke<T>(channel, params): Promise<ReturnType>`
  - `window.taskerAPI` typed via `declare global { interface Window { taskerAPI: TaskerAPI } }`
  - Drizzle types flow end-to-end: Schema → `$inferSelect` → IPC contract → component
- [ ] Set up React renderer (`src/`)
  - Tailwind CSS V4 with `@tailwindcss/vite` plugin — no config file, no PostCSS
  - CSS variables for theming (dark mode support)
  - shadcn/ui component installation (Button, Input, Collapsible, DropdownMenu, etc.)
  - `cn()` utility with `clsx` + `tailwind-merge`
- [ ] Build core components
  - `TaskList.tsx` — renders grouped tasks per list, collapsible sections
  - `TaskItem.tsx` — checkbox, priority indicator, due date, tags, inline editing
  - `QuickAdd.tsx` — inline task creation with metadata parsing
  - `SearchBar.tsx` — filter tasks across lists
  - List headers with collapse/expand, task count, add button, menu
- [ ] Implement drag-and-drop with `@dnd-kit`
  - Task reorder within list
  - Task move between lists
  - List reorder
- [ ] Implement file watching with `chokidar`
  - Watch `tasker.db` for external changes (CLI modifications)
  - Debounce and refresh UI on change
- [ ] Set up auto-launch with `node-auto-launch`
  - macOS: Login Item
  - Windows: Registry
  - Linux: `.desktop` file
- [ ] Set up tray positioning with `electron-traywindow-positioner`
  - Linux fallback: cursor position when `getBounds()` returns `{0,0,0,0}`
- [ ] Set up CSS with `backdrop-filter: blur(20px)` for native popup feel
- [ ] Write desktop tests
  - Vitest with `environment: 'jsdom'` for component tests
  - React Testing Library for component behavior
  - IPC handler unit tests (mock better-sqlite3)

**Success criteria:** Tray icon shows in menu bar, popup displays tasks grouped by list, tasks can be added/edited/checked/deleted through the UI, drag-and-drop reorder works, CLI changes are reflected in real-time via file watcher.

#### Phase 4: Build, Distribution & Polish

Package everything for installation and distribution.

**Tasks:**

- [ ] Configure npm global package for CLI
  - `apps/cli/package.json`: `"bin": { "tasker": "./dist/index.js" }`
  - Build script: `tsc && chmod +x dist/index.js`
  - Shebang line: `#!/usr/bin/env node`
  - Test: `npm install -g @tasker/cli && tasker list`
- [ ] Configure electron-builder for desktop
  - macOS: DMG + ZIP, code signing (if applicable)
  - Windows: NSIS installer
  - Linux: AppImage + deb
  - `LSUIElement: true` for macOS (no dock icon)
  - `window.setSkipTaskbar(true)` for Windows
- [ ] Create `update.sh` equivalent
  - Version bumping across all packages (pnpm workspace versioning)
  - Build all packages
  - Install CLI globally
  - Build and install desktop app
- [ ] Cross-platform path resolution
  - macOS: `~/Library/Application Support/cli-tasker/`
  - Linux: `$XDG_DATA_HOME/cli-tasker/` (fallback `~/.local/share/cli-tasker/`)
  - Windows: `%APPDATA%/cli-tasker/`
- [ ] End-to-end testing
  - CLI against real DB (read-only verification)
  - Desktop app manual testing checklist
  - Cross-platform smoke tests

**Success criteria:** `npm install -g @tasker/cli` works, `tasker` command works globally, desktop app builds and runs from electron-builder output, existing data loads correctly.

## Alternative Approaches Considered

| Alternative | Why Rejected |
|-------------|-------------|
| **Tauri** instead of Electron | 3-10MB vs 150MB bundle, 30MB vs 500MB RAM, <0.5s vs 1-2s startup. But requires Rust, and user prefers all-JS stack. |
| **Incremental migration** | ~17,740 lines across 102 files. Interop between C# and TS adds more complexity than a clean rewrite. |
| **Keep TUI (Ink)** | TUI was a stepping stone to desktop. Humans use tray, agents use CLI. Not worth porting. |
| **tRPC for IPC** | 39% overhead for simple operations. Typed contracts achieve the same safety with less weight. |
| **Prisma** instead of Drizzle | Drizzle is lighter, SQL-first, better for existing schema introspection. |
| **Svelte** for desktop renderer | User chose React for consistency with shadcn/ui ecosystem and Ink potential (dropped with TUI). |

## Acceptance Criteria

### Functional Requirements

- [ ] All 17 CLI commands produce identical behavior to C# version
- [ ] CLI output formatting matches (checkboxes, priority indicators, due dates, tags, relationships)
- [ ] Existing SQLite database loads without modification — user data preserved
- [ ] Undo/redo/history works for all 17 command types
- [ ] Cascade operations work (check/delete/move/restore propagate to descendants)
- [ ] Inline metadata parsing works: `p1`/`p2`/`p3`, `@date`, `#tag`, `^parentId`, `!blockerId`
- [ ] Directory auto-detection works (match cwd to list name, `--all` bypasses)
- [ ] Default list "tasks" cannot be deleted or renamed
- [ ] Desktop tray app shows in menu bar with popup window
- [ ] Desktop shows tasks grouped by list with collapse/expand
- [ ] Desktop supports inline editing, quick add, search
- [ ] Desktop supports drag-and-drop reorder (tasks and lists)
- [ ] Desktop reflects CLI changes in real-time (file watcher)
- [ ] Batch operations work with multiple task IDs
- [ ] `tasker get --json` outputs valid JSON
- [ ] `tasker get -r` shows full subtask tree
- [ ] Backup list/restore works

### Non-Functional Requirements

- [ ] TypeScript strict mode, no `any` types in production code
- [ ] All DB writes go through main process in Electron (never in renderer)
- [ ] Electron sandbox enabled, no `nodeIntegration`
- [ ] WAL mode for concurrent CLI + desktop access
- [ ] Sort order convention preserved: highest `sort_order` = newest
- [ ] Task ordering preserved: InProgress → Pending → Done, then by priority → due → newest

### Quality Gates

- [ ] `pnpm test` passes across all packages
- [ ] TypeScript compiles with zero errors (`pnpm build`)
- [ ] No `any` types in production code
- [ ] Core test coverage >= 80% (matching current xUnit coverage)
- [ ] All 17 undo command types have round-trip tests

## Success Metrics

1. **Feature parity** — every CLI command produces the same result as the C# version
2. **Data continuity** — existing `tasker.db` loads and works without migration
3. **Desktop usability** — tray app is responsive (<100ms IPC round-trips) and reflects changes in real-time
4. **Cross-platform** — CLI works on macOS, Linux, and Windows (desktop: macOS first, others follow)

## Dependencies & Prerequisites

| Dependency | Version | Purpose |
|------------|---------|---------|
| Node.js | >= 20.0 | Runtime |
| pnpm | >= 9.0 | Package manager + workspaces |
| TypeScript | >= 5.4 | Language |
| Commander.js | ^12 | CLI framework |
| Drizzle ORM | ^0.35 | Database ORM |
| better-sqlite3 | ^11 | SQLite driver |
| drizzle-kit | ^0.28 | Schema introspection |
| chalk | ^5 | CLI output coloring |
| Electron | ^33 | Desktop framework |
| React | ^18 | UI framework |
| Vite | ^6 | Build tool |
| vite-plugin-electron | ^0.28 | Electron + Vite integration |
| @tailwindcss/vite | ^4 | Tailwind CSS V4 plugin |
| shadcn/ui | latest | UI components (Radix-based) |
| @dnd-kit/core | ^6 | Drag and drop |
| chokidar | ^4 | File watching |
| node-auto-launch | ^5 | Auto-start on login |
| electron-traywindow-positioner | ^1 | Cross-platform tray positioning |
| electron-builder | ^25 | Desktop packaging |
| Vitest | ^3 | Testing |
| @testing-library/react | ^16 | Component testing |

## Risk Analysis & Mitigation

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| Electron bundle size (~150MB) | Low | Certain | Acceptable tradeoff for cross-platform + ecosystem |
| better-sqlite3 native module compilation | Medium | Medium | electron-rebuild handles it; pin known-good version |
| Drizzle can't abstract `json_each()` | Low | Certain | Use raw SQL for complex JSON queries (tags search) |
| Linux `tray.getBounds()` returns `{0,0,0,0}` | Low | Likely | electron-traywindow-positioner falls back to cursor position |
| IPC serialization: class instances lose methods | High | Certain | Use plain objects + utility functions; `Result<T>` pattern for errors |
| Concurrent CLI + desktop writes | Medium | Likely | WAL mode handles reads; writes through main process + file watcher for sync |
| Undo history format compatibility | Medium | Low | JSON format is schema-agnostic; discriminated unions map to same JSON |
| Date object serialization across IPC | Medium | Likely | Use Unix timestamps or ISO strings; convert at boundaries |

## Resource Requirements

- **Reference project:** `currency-exchange-widget` for Electron/IPC/shadcn patterns
- **Existing codebase:** C# source serves as specification for all behavior
- **24 xUnit test files:** Serve as behavioral specification for TypeScript tests

## Future Considerations

- **Windows/Linux tray testing** — macOS first, then expand
- **Auto-update** — electron-updater for desktop app
- **Plugin system** — Commander.js supports custom commands; @tasker/core exports could enable third-party integrations
- **Sync** — SQLite + WAL makes the DB file sync-friendly (Dropbox, iCloud)
- **Mobile** — React Native could reuse @tasker/core (Drizzle supports expo-sqlite)

## Documentation Plan

- Update `CLAUDE.md` to reflect new stack, build commands, and project structure
- Update `docs/reference/` files for TypeScript equivalents
- Create `README.md` for each package with setup instructions
- Document IPC contracts and type-safe patterns

## References & Research

### Internal References

- Brainstorm: `docs/brainstorms/2026-02-08-nodejs-electron-migration-brainstorm.md`
- Models and schema: `docs/reference/models-and-schema.md`
- Commands reference: `docs/reference/commands.md`
- Inline metadata: `docs/reference/inline-metadata.md`
- Conventions: `docs/reference/conventions.md`
- Current data layer: `src/TaskerCore/Data/TodoTaskList.cs` (1,953 lines)
- Current undo system: `src/TaskerCore/Undo/` (17 command types)
- Current parser: `src/TaskerCore/Parsing/TaskDescriptionParser.cs` (299 lines)
- Current output: `Output.cs` (Spectre.Console formatting)
- Current build: `update.sh`

### External References

- Reference Electron project: `/Users/carlos/self-development/currency-exchange-widget/`
- Drizzle ORM docs: https://orm.drizzle.team
- Commander.js docs: https://github.com/tj/commander.js
- shadcn/ui docs: https://ui.shadcn.com
- Tailwind CSS V4: https://tailwindcss.com/docs
- Electron docs: https://www.electronjs.org/docs
- @dnd-kit docs: https://dndkit.com
- better-sqlite3 docs: https://github.com/WiseLibs/better-sqlite3
- electron-builder docs: https://www.electron.build

### Institutional Learnings (Gotchas to Preserve)

These are hard-won lessons from the C# codebase that must be carried forward:

1. **Sort order:** Highest value = newest. Display uses `DESC`. Don't bump on status changes.
2. **Done task sorting:** Split sort — active by priority/due/created, done by `CompletedAt DESC`.
3. **Undo recursion:** Always pass `recordUndo: false` when executing from undo/redo.
4. **Undo state capture:** Record state BEFORE modification, not after.
5. **Tag regex:** Use `[\w-]+` not `\w+` — hyphenated tags must parse.
6. **Date preservation:** Store raw date text alongside resolved value; compare raw markers before overwriting on rename.
7. **Default list protection:** "tasks" list cannot be deleted or renamed.
8. **Cascade operations:** Check/delete/move/restore must propagate to descendants.
9. **Circular dependency detection:** Must check before creating parent/blocker relationships.
10. **Test isolation:** Unique temp dirs per test, never hardcode production paths, cleanup in afterEach.
11. **Relationship markers:** Follow 8-step checklist when adding new marker types.
12. **Atomic writes:** Write to `.tmp` then rename for backup files.
13. **SQLite pragmas:** WAL mode + foreign keys ON must be set on every connection.
14. **Status change sort:** Don't bump sort_order — sort on open, not during interaction.

### Related Work

- Task: `wh1` — "migrate to nodejs, electron, commander.js, react"
- Current version: 2.42.4 (C#/.NET 10.0)
- Target version: 3.0.0 (TypeScript/Node.js)
