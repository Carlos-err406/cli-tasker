---
title: "feat: Migrate cli-tasker to Node.js, Electron, Commander.js, React"
type: feat
date: 2026-02-08
deepened: 2026-02-08
---

# feat: Migrate cli-tasker to Node.js, Electron, Commander.js, React (Deepened)

## Enhancement Summary

**Deepened on:** 2026-02-08
**Reviewed by:** DHH Rails Reviewer, Kieran Rails Reviewer, Code Simplicity Reviewer (2026-02-08)
**Sections enhanced:** Architecture, Type Mappings, Phase 1 (Core), Phase 3 (Desktop), Risks, Testing Strategy, Schema Evolution
**Research agents used:** Architecture Strategist, TypeScript Patterns Reviewer, pnpm Monorepo Best Practices, Performance Oracle, Framework Docs Researcher

### Key Improvements (Updated After Review)

1. **TypeScript type safety patterns** - Replaced `const enum` with `as const` pattern, made branded types optional (deferred to Phase 1 evaluation), added runtime validation with Zod at IPC boundaries
2. **Architecture boundaries** - Moved IPC contracts from `@tasker/core` to desktop app to preserve transport-agnostic core
3. **Concurrency handling** - Added `PRAGMA busy_timeout` + exponential backoff retry wrapper, documented WAL file watching
4. **IPC error handling** - Added top-level error wrapper for all IPC handlers to prevent main process crashes
5. **Database connection management** - Singleton connection with retry strategy for SQLITE_BUSY errors
6. **Integration testing layer** - Added concurrent CLI + desktop access test scenarios
7. **Schema evolution strategy** - Documented migration workflow with drizzle-kit, version checks, and startup validation
8. **Undo system design** - Separated undo command data from execution logic using switch-based executor pattern
9. **Native module setup** - Added explicit electron-rebuild configuration and asar exclusion from day one
10. **Monorepo conventions** - Changed `shared/` to `packages/` to match ecosystem conventions, documented TypeScript project references

### New Considerations Discovered (Updated After Review)

- better-sqlite3 native module requires electron-rebuild and asar exclusion configuration immediately, not as an afterthought
- pnpm's symlinked node_modules structure can break native module bindings—requires `node-linker=hoisted` in .npmrc
- TypeScript `const enum` is incompatible with `--isolatedModules` (required by Vite/esbuild)—use `as const` pattern instead
- IPC boundaries need runtime validation (Zod) since TypeScript types are compile-time only
- **IPC handlers need error wrapper to prevent main process crashes** - uncaught exceptions in handlers will crash the entire Electron app
- **busy_timeout alone insufficient for long operations** - exponential backoff retry wrapper required on top of busy_timeout
- **Branded types add ceremony before encountering actual bugs** - defer to optional post-Phase 1 evaluation
- **Integration tests essential** - unit tests don't catch cross-process race conditions
- **Schema evolution needs explicit strategy** - migrations, version checks, and desktop startup validation required
- WAL mode writes to `tasker.db-wal`, not `tasker.db`—file watcher must watch both files

---

## Execution Strategy

1. **All four phases executed sequentially** — Phase 1 → 2 → 3 → 4
2. **Separate branches per phase** — `feat/phase-1-core`, `feat/phase-2-cli`, `feat/phase-3-desktop`, `feat/phase-4-distribution`
3. **Pause for review after each phase** — Phase 1 completed first, then user reviews before proceeding
4. **New database for development** — Create a fresh database for the migration. Production data at `~/Library/Application Support/cli-tasker/tasker.db` is **never touched**. Use a copy of the production DB for schema reference/testing if needed. Final data migration happens only after all phases are complete and verified.

---

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
| `@tasker/core` (`packages/core/`) | Models, Drizzle schema, queries, parsers, undo system |
| `@tasker/cli` (`apps/cli/`) | Commander.js commands, chalk output formatting |
| `@tasker/desktop` (`apps/desktop/`) | Electron + React + shadcn/ui tray app |

### Research Insights: Monorepo Structure

**Best Practice:** Use `packages/` instead of `shared/` for shared code. Most TypeScript monorepos use `packages/` as the conventional directory name, making the structure immediately recognizable to other developers. [Source: Managing TypeScript Packages in Monorepos](https://nx.dev/blog/managing-ts-packages-in-monorepos)

**Dependency Protocol:** Use `"@tasker/core": "workspace:*"` in dependent packages. The `workspace:` protocol makes it explicit that dependencies are resolved locally and prevents accidental publication. [Source: pnpm workspaces](https://pnpm.io/workspaces)

## Technical Approach

### Architecture

```
tasker/
├── packages/
│   └── core/                  # @tasker/core
│       ├── src/
│       │   ├── schema/        # Drizzle table definitions (introspected)
│       │   ├── queries/       # TaskQueries, ListQueries, UndoQueries, etc.
│       │   ├── types/         # Task, NewTask, TaskStatus, Priority, Result<T>
│       │   ├── undo/          # Discriminated union command types + executor
│       │   ├── parsers/       # TaskDescriptionParser, DateParser
│       │   └── db.ts          # Connection factory (WAL, FK, busy_timeout pragmas)
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
│       │   └── ipc/           # Modular IPC channels (contracts live here, NOT in core)
│       │       ├── register.ts
│       │       ├── types.d.ts
│       │       ├── contracts.ts     # Channel names, param types, return types
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

### Research Insights: Architecture Boundaries

**Critical:** IPC contracts **must live in `apps/desktop/electron/ipc/contracts.ts`**, NOT in `@tasker/core`. The core library should be transport-agnostic—it has no concept of Electron IPC. The CLI consumes core directly; only the desktop app needs IPC. Placing contracts in core violates the Dependency Inversion Principle and pollutes the core with Electron-specific concerns. [Source: Architecture Review]

**Pattern:** Core exports domain types (Task, Result<T>), desktop app imports them and defines IPC payload shapes:
```typescript
// apps/desktop/electron/ipc/contracts.ts
import type { Task, TaskResult } from '@tasker/core';

export const ipcChannels = {
  'task:add': {
    input: z.object({ description: z.string(), listName: z.string() }),
    output: {} as TaskResult,  // Re-export core type
  },
} as const;
```

### Stack Mapping

| Layer | C# Current | TypeScript New |
|-------|-----------|----------------|
| Language | C# (.NET 10.0) | TypeScript (ES2022, strict) |
| Core library | `TaskerCore/` | `@tasker/core` (packages/core) |
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

| C# Pattern | TypeScript Equivalent | Notes |
|-------------|----------------------|-------|
| `record TodoTask(...)` | `interface Task` with `readonly` fields | Use ISO strings for dates, not Date objects |
| `enum TaskStatus` | `as const` object + derived type | **NOT** `const enum` (breaks `--isolatedModules`) |
| `TaskResult` (Success/NotFound/NoChange/Error) | Four-variant discriminated union with `type` field | Keep all four variants, not binary success/failure |
| `IUndoableCommand` with `[JsonDerivedType]` | Discriminated union with `type` field + switch-based executor | Data separated from execution logic |
| LINQ queries | Array methods (`.filter()`, `.map()`, `.sort()`) | Use prepared statements for repeated queries |
| `ImmutableArray<T>` mutation methods | Spread + override: `{ ...task, status: 2 }` | No helper functions needed—spread is idiomatic |

### Research Insights: TypeScript Type Patterns

**Enums:** Do **NOT** use `const enum`. It is incompatible with `--isolatedModules` (required by Vite, esbuild, SWC) and causes issues across package boundaries. Use the `as const` pattern instead:

```typescript
// packages/core/src/types/task-status.ts
export const TaskStatus = {
  Pending: 0,
  InProgress: 1,
  Done: 2,
} as const;

export type TaskStatus = (typeof TaskStatus)[keyof typeof TaskStatus];
// Resolves to: 0 | 1 | 2

// Usage: TaskStatus.Pending  (runtime value)
// Type: TaskStatus            (compile-time type)
```

This gives named constants at runtime, full type narrowing, perfect tree-shaking, and works across monorepo packages. [Source: TypeScript Patterns Review]

**Result Types:** Keep all four C# TaskResult variants (Success, NotFound, NoChange, Error), not a binary success/failure. Your CLI differentiates these cases with different messages and exit codes:

```typescript
export type TaskResult =
  | { readonly type: 'success'; readonly message: string }
  | { readonly type: 'not-found'; readonly taskId: string }
  | { readonly type: 'no-change'; readonly message: string }
  | { readonly type: 'error'; readonly message: string };
```

Use TypeScript's exhaustive switch checking instead of a Result monad with `.map()`/`.flatMap()` methods. Discriminated unions work better with TypeScript's native control flow narrowing. [Source: TypeScript Patterns Review]

**Branded Types (Optional - Evaluate After Phase 1):** Consider branded types for `TaskId` and `ListName` to prevent accidentally passing a list name where a task ID is expected:

```typescript
// packages/core/src/types/branded.ts
declare const brand: unique symbol;
type Brand<T, B extends string> = T & { readonly [brand]: B };

export type TaskId = Brand<string, 'TaskId'>;
export type ListName = Brand<string, 'ListName'>;

export function taskId(raw: string): TaskId {
  if (!/^[0-9a-z]{3}$/.test(raw)) throw new Error(`Invalid task ID: "${raw}"`);
  return raw as TaskId;
}

export function listName(raw: string): ListName {
  if (raw.length === 0) throw new Error('List name cannot be empty');
  return raw as ListName;
}
```

**Decision:** Start with simple type aliases (`type TaskId = string; type ListName = string;`) for Phase 1 and 2. Add branded types only if you encounter actual bugs where task IDs and list names are confused. The validation functions are valuable; the branded types add ceremony that may not be worth the ergonomic cost. [Source: Kieran's Review - Critical Issue #3]

**Dates:** Use ISO strings in the data model, not `Date` objects. SQLite stores dates as TEXT, IPC serializes to JSON, and Drizzle returns strings. `Date` objects don't survive JSON serialization. Parse to `Date` only at the display boundary. [Source: TypeScript Patterns Review]

### Implementation Phases

#### Phase 1: Monorepo Scaffold + Core/Schema

Set up the project skeleton and port the data layer — everything depends on this.

**Tasks:**

- [ ] Initialize pnpm monorepo with workspace config
  - `pnpm-workspace.yaml`: `packages: ['packages/*', 'apps/*']`
  - `.npmrc`: `link-workspace-packages=true`, `auto-install-peers=true`, `node-linker=hoisted`
  - Root `package.json` with workspace scripts
  - **Research Insight:** Add `node-linker=hoisted` to prevent pnpm's symlinked node_modules from breaking better-sqlite3 binary resolution [Source: better-sqlite3 + pnpm issues]
- [ ] Create `tsconfig.base.json` with shared settings
  - ES2022 target, strict mode, composite, declaration, bundler moduleResolution
  - **Critical additions:** `noUncheckedIndexedAccess: true`, `verbatimModuleSyntax: true`, `exactOptionalPropertyTypes: true`
  - `isolatedModules: true` (required for Vite/esbuild)
  - **Research Insight:** `noUncheckedIndexedAccess` forces null checks on map lookups (task IDs are map keys—without this, `tasks[id]` has type `Task` instead of `Task | undefined`) [Source: TypeScript Best Practices]
- [ ] Create `packages/core/` package scaffold
  - `package.json` with `"name": "@tasker/core"`, `"private": true`, exports map
  - `tsconfig.json` extending base
- [ ] Introspect existing SQLite schema with `drizzle-kit pull`
  - Point at `~/Library/Application Support/cli-tasker/tasker.db`
  - Generate `src/schema/` files: `tasks.ts`, `lists.ts`, `taskDependencies.ts`, `config.ts`, `undoHistory.ts`
- [ ] Create database connection factory with retry strategy (`src/db.ts`)
  - `createDb(path?: string)` — singleton connection, defaults to platform-appropriate path
  - Enable WAL mode: `db.run('PRAGMA journal_mode = WAL')`
  - Enable foreign keys: `db.run('PRAGMA foreign_keys = ON')`
  - **Critical addition:** `db.run('PRAGMA busy_timeout = 5000')`
  - Export Drizzle instance wrapping better-sqlite3
  - **Research Insight:** `busy_timeout` is the TypeScript equivalent of C#'s `CrossProcessLock`. WAL mode alone does not prevent `SQLITE_BUSY` on concurrent writes—timeout tells SQLite to retry for 5 seconds instead of failing immediately [Source: Architecture Review, better-sqlite3 performance docs]
  - **Critical addition:** Implement retry wrapper with exponential backoff for all write operations [Source: Kieran's Review - Critical Issue #2]

```typescript
// packages/core/src/db.ts
export class DatabaseConnection {
  private static instance: ReturnType<typeof drizzle>;

  static get() {
    if (!this.instance) {
      const sqlite = new Database(dbPath);
      sqlite.pragma('journal_mode = WAL');
      sqlite.pragma('foreign_keys = ON');
      sqlite.pragma('busy_timeout = 5000');
      this.instance = drizzle(sqlite);
    }
    return this.instance;
  }

  // Wrap all writes with exponential backoff
  static async withRetry<T>(fn: () => T, maxRetries = 3): Promise<T> {
    for (let i = 0; i < maxRetries; i++) {
      try {
        return fn();
      } catch (err: any) {
        if (err.code === 'SQLITE_BUSY' && i < maxRetries - 1) {
          await sleep(100 * Math.pow(2, i)); // 100ms, 200ms, 400ms
          continue;
        }
        throw err;
      }
    }
    throw new Error('Max retries exceeded');
  }
}
```
- [ ] Define TypeScript types (`src/types/`)
  - `Task` interface (from Drizzle `$inferSelect`)—use ISO strings for dates, `readonly string[]` for tags
  - `NewTask` type (from Drizzle `$inferInsert`)
  - `TaskStatus` using `as const` pattern (NOT `const enum`)
  - `Priority` using `as const` pattern
  - `TaskId` and `ListName` type aliases (branded types deferred to later if needed)
  - `TaskResult` discriminated union (four variants: success/not-found/no-change/error)
  - `DataResult<T>` for operations that return data on success
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
  - **Research Insight:** Use Drizzle's `.prepare()` for repeated queries with parameters—much faster than re-parsing SQL each time [Source: Drizzle performance docs]
- [ ] Port undo system to `src/undo/`
  - `commands.ts` — discriminated union with `type` field for all 17 command types (pure data, JSON-serializable)
  - `executor.ts` — `executeUndo(cmd, repo)` and `executeRedo(cmd, repo)` with exhaustive switch statements
  - `undoManager.ts` — two-stack architecture (undo/redo) stored in SQLite via Drizzle
  - **Gotcha:** Use `recordUndo: false` parameter when calling from undo/redo to prevent recursion
  - **Gotcha:** Capture state BEFORE modification for undo data
  - JSON serialization compatible with existing `undo_history` table format
  - **Research Insight:** Separate command data from execution logic. Commands are plain objects with no methods. Executor receives repository as a parameter, making it trivially testable. No `new TodoTaskList()` instantiation inside data records. [Source: TypeScript Patterns Review]
  - **Research Insight:** Add Zod schemas for undo commands deserialized from the database to protect against corrupt/outdated entries after schema migrations [Source: TypeScript Patterns Review]
- [ ] Port backup system
  - Use better-sqlite3 `.backup()` method (maps to SQLite hot backup API)
  - Atomic writes: write to `.tmp` then rename
- [ ] Set up Vitest workspace
  - `vitest.workspace.ts` at root: `['packages/*/vitest.config.ts', 'apps/*/vitest.config.ts']`
  - Core tests: `environment: 'node'`, in-memory SQLite (`:memory:`)
  - Test isolation: unique temp dirs per test, never hardcode production paths, cleanup in afterEach
  - **Research Insight:** Use `beforeEach` to create fresh in-memory DB with `new Database(':memory:')`, apply schema with `pushSchema` from `drizzle-kit/api`, then seed test data. Avoids migration files in tests. [Source: Drizzle + Vitest patterns]
- [ ] Write core tests (port from 24 existing xUnit test files)
  - Schema validation tests
  - TaskDescriptionParser tests (metadata parsing, display description, sync)
  - DateParser tests (all date formats)
  - Query tests (CRUD, batch, sort order, cascades)
  - Undo tests (all 17 command types, composite operations, redo)
  - Backup tests

**Success criteria:** `pnpm test --filter @tasker/core` passes, Drizzle schema matches existing DB, all query operations work against in-memory SQLite.

### Research Insights: Core Performance

**WAL Mode Best Practices:**
- Enable WAL mode with `db.run('PRAGMA journal_mode = WAL')` on every connection
- Set `busy_timeout` to handle concurrent writes: `db.run('PRAGMA busy_timeout = 5000')`
- Monitor WAL file size and trigger checkpoint restart if it exceeds threshold (prevents infinite growth during sustained reads)

```typescript
// Check WAL size every 5 seconds
setInterval(() => {
  const walPath = `${dbPath}-wal`;
  if (fs.existsSync(walPath)) {
    const stats = fs.statSync(walPath);
    if (stats.size > 10 * 1024 * 1024) { // 10MB threshold
      db.run('PRAGMA wal_checkpoint(RESTART)');
    }
  }
}, 5000);
```

[Source: better-sqlite3 performance docs](https://github.com/WiseLibs/better-sqlite3/blob/master/docs/performance.md)

**Prepared Statements:**
Use Drizzle's `.prepare()` for queries executed multiple times:

```typescript
const prepared = db.select().from(tasks).where(eq(tasks.listName, placeholder('list'))).prepare();
const results1 = prepared.execute({ list: 'work' });
const results2 = prepared.execute({ list: 'personal' });
```

This avoids repeated SQL parsing and can be 2-3x faster for hot paths. [Source: Drizzle performance guide]

**Raw Mode for Bulk Operations:**
Use `.raw()` to return rows as arrays instead of objects when processing large result sets:

```typescript
const stmt = db.select().from(tasks).prepare();
stmt.raw(); // Enable raw mode
const rows = stmt.all(); // Returns [[id, description, ...], [...]]
```

This reduces allocation overhead when you don't need named properties. [Source: better-sqlite3 API docs]

#### Phase 2: CLI Commands

Port all 17 commands + undo/redo/history using Commander.js.

**Tasks:**

- [ ] Create `apps/cli/` package scaffold
  - `package.json` with `"name": "@tasker/cli"`, `"bin": { "tasker": "./dist/index.js" }`, dependency on `"@tasker/core": "workspace:*"`
  - `tsconfig.json` extending base with reference to `../../packages/core`
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
- [ ] Write CLI + Desktop integration tests (new layer)
  - **Critical:** Test concurrent access scenarios [Source: Kieran's Review - Critical Issue #4]

```typescript
// tests/integration/concurrent-access.test.ts
describe('concurrent CLI + desktop access', () => {
  it('desktop sees CLI changes within 500ms', async () => {
    // 1. Desktop loads task list via IPC
    const initialTasks = await ipc.invoke('task:list', { listName: 'work' });

    // 2. CLI adds task via direct DB access
    execSync('tasker add "Test task" -l work');

    // 3. Wait for file watcher to trigger
    await sleep(500);

    // 4. Assert desktop shows new task
    const updatedTasks = await ipc.invoke('task:list', { listName: 'work' });
    expect(updatedTasks.length).toBe(initialTasks.length + 1);
  });

  it('CLI sees desktop changes immediately', async () => {
    // 1. Desktop adds task via IPC
    await ipc.invoke('task:add', { description: 'IPC task', listName: 'work' });

    // 2. CLI reads immediately
    const output = execSync('tasker list -l work').toString();

    // 3. Assert CLI shows new task
    expect(output).toContain('IPC task');
  });

  it('desktop handles SQLITE_BUSY during long CLI operation', async () => {
    // 1. Start long CLI operation (batch delete with 100 tasks)
    const cliProcess = spawn('tasker', ['delete', ...taskIds100]);

    // 2. Desktop attempts write during CLI operation
    const result = await ipc.invoke('task:add', {
      description: 'Concurrent write',
      listName: 'work'
    });

    // 3. Assert no crash, either success or retry succeeded
    expect(result.type).not.toBe('error');

    await cliProcess;
  });

  it('file watcher triggers on WAL file changes', async () => {
    const changeEvents: string[] = [];
    watcher.on('change', (path) => changeEvents.push(path));

    // CLI write should trigger WAL file change
    execSync('tasker add "WAL test" -l work');
    await sleep(100);

    // Assert watcher saw tasker.db-wal change
    expect(changeEvents).toContain(expect.stringMatching(/tasker\.db-wal$/));
  });
});
```

**Success criteria:** All 17 commands + undo/redo/history work. `pnpm test --filter @tasker/cli` passes. `tasker list` produces output matching the C# version. Existing task data loads correctly.

#### Phase 3: Desktop App (Electron + React + shadcn)

Build the tray app following `currency-exchange-widget` architecture.

**Tasks:**

- [ ] Create `apps/desktop/` package scaffold
  - `package.json` with `"name": "@tasker/desktop"`, dependency on `"@tasker/core": "workspace:*"`
  - `vite.config.ts` with `vite-plugin-electron`, `better-sqlite3` marked as external
  - `electron-builder.json5` — macOS: `LSUIElement: true`, dmg + zip targets
  - **Critical:** Configure asar exclusion for better-sqlite3 immediately:
    ```json5
    {
      asarUnpack: [
        "node_modules/better-sqlite3/**/*",
        "out/main/chunks/*.node"
      ]
    }
    ```
  - `tsconfig.json` — separate configs for renderer (JSX) and main process (Node)
  - **Research Insight:** better-sqlite3 is a native addon that MUST be excluded from asar archives and rebuilt with electron-rebuild. Configure this on day one, not as an afterthought. [Source: Electron + better-sqlite3 integration guide]
- [ ] Set up Electron main process (`electron/main.ts`)
  - App lifecycle: `app.dock.hide()` on macOS
  - Single instance lock
  - Create tray icon and popup window
  - **Research Insight:** Run `npx @electron/rebuild -f -w better-sqlite3` after `npm install` to ensure native module is compiled against Electron's Node headers [Source: better-sqlite3 + Electron docs]
- [ ] Set up Electron preload (`electron/preload.ts`)
  - `contextBridge.exposeInMainWorld('taskerAPI', { ... })`
  - Sandbox enabled, no `nodeIntegration`
  - Type definitions in `electron-env.d.ts`
- [ ] Implement Electron lib (`electron/lib/`)
  - `window.ts` — frameless BrowserWindow, popup positioning, blur-to-hide
  - `tray.ts` — tray icon, toggle, context menu
  - `config.ts` — window position/size persistence
- [ ] Implement modular IPC channels (`electron/ipc/`)
  - `error-handler.ts` — **Critical:** Top-level error wrapper for all IPC handlers [Source: Kieran's Review - Critical Issue #1]
  - `contracts.ts` — **Central contracts file with Zod schemas and type definitions**
  - `tasks/` — CRUD operations, batch operations, status changes, reorder
  - `lists/` — list CRUD, collapse/expand, default management
  - `undo/` — undo, redo, history
  - `window/` — resize, drag, position
  - Central `register.ts` combining all channel registrations
  - IPC type definitions in `types.d.ts`
  - **Pattern per feature dir:** `channels.ts` (string constants), `main.ts` (handlers + registration wrapped with `wrapIpcHandler`), `preload.ts` (invoker factory), `utils.ts` (helpers)
  - **Serialization:** Use timestamps not Date objects, `null` not `undefined`, plain objects not class instances
  - **Result pattern:** Return `Result<T>` objects, never throw across IPC boundary (enforced by error wrapper)
  - **Broadcasting:** `BrowserWindow.getAllWindows().forEach(win => win.webContents.send(channel, data))` for main→renderer events

### Research Insights: IPC Type Safety with Zod

**Critical:** Add runtime validation at the IPC boundary using Zod. TypeScript types are compile-time only—the renderer can send arbitrary data. Validate in the main process to prevent crashes.

```typescript
// apps/desktop/electron/ipc/contracts.ts
import { z } from 'zod';
import type { TaskResult } from '@tasker/core';

// Runtime validation schema
export const AddTaskInput = z.object({
  description: z.string().min(1).max(1000),
  listName: z.string().min(1),
});

// Inferred type (no duplication)
export type AddTaskInput = z.infer<typeof AddTaskInput>;

// Channel map
export const ipcChannels = {
  'task:add': {
    input: AddTaskInput,
    output: {} as TaskResult,  // Phantom type for inference
  },
  'task:list': {
    input: z.object({ listName: z.string().optional() }),
    output: {} as Task[],
  },
} as const;

export type IpcChannels = typeof ipcChannels;
export type ChannelName = keyof IpcChannels;
```

**Main process handler with error handling wrapper:**

```typescript
// apps/desktop/electron/ipc/error-handler.ts
import type { IpcMainInvokeEvent } from 'electron';
import { ZodError } from 'zod';

export function wrapIpcHandler<T>(
  handler: (event: IpcMainInvokeEvent, input: unknown) => Promise<T>
) {
  return async (event: IpcMainInvokeEvent, input: unknown) => {
    try {
      return await handler(event, input);
    } catch (err) {
      if (err instanceof ZodError) {
        return { type: 'error', message: err.issues[0].message };
      }
      if (err instanceof Error && 'code' in err) {
        // SQLite errors
        return { type: 'error', message: err.message };
      }
      console.error('Unhandled IPC error:', err);
      return { type: 'error', message: 'Unknown error occurred' };
    }
  };
}

// apps/desktop/electron/ipc/tasks/main.ts
import { ipcMain } from 'electron';
import { ipcChannels } from '../contracts';
import { wrapIpcHandler } from '../error-handler';

export function registerTaskHandlers() {
  ipcMain.handle('task:add', wrapIpcHandler(async (_event, rawInput: unknown) => {
    // Runtime validation at the trust boundary
    const input = ipcChannels['task:add'].input.parse(rawInput);

    // input is now type-safe
    return taskRepository.add(input.description, input.listName);
  }));
}
```

**Critical:** Without this wrapper, uncaught errors in the main process (Zod validation failures, SQLite errors, unexpected exceptions) will crash the entire Electron app. The wrapper ensures all errors are serialized back to the renderer as Result<T> objects. [Source: Kieran's Review - Critical Issue #1, TypeScript Patterns Review, Zod best practices]

- [ ] Set up React renderer (`src/`)
  - Tailwind CSS V4 with `@tailwindcss/vite` plugin — no config file, no PostCSS
  - CSS variables for theming (dark mode support)
  - **Research Insight:** Tailwind V4 uses `@theme` directive in CSS for custom colors, `@custom-variant` for dark mode selector override [Source: Tailwind CSS V4 docs]
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
  - **Critical:** Watch `tasker.db-wal` in addition to `tasker.db`
  - **Research Insight:** In WAL mode, writes go to `tasker.db-wal`, not to `tasker.db` itself. The main database file is only modified during checkpoints. Watching `tasker.db` alone will miss most CLI writes. [Source: Architecture Review, SQLite WAL documentation]
  - Debounce and refresh UI on change (250-500ms debounce)
  - Consider watching all three files: `tasker.db`, `tasker.db-wal`, `tasker.db-shm`
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

### Research Insights: Tailwind CSS V4 Dark Mode

**Dark Mode with Custom Selector:**

Tailwind V4 uses the `@custom-variant` directive to override the default `dark:` variant:

```css
@import "tailwindcss";

/* Use class-based dark mode instead of media query */
@custom-variant dark (&:where(.dark, .dark *));
```

This makes `dark:bg-gray-800` apply when an ancestor has `class="dark"`, giving you manual dark mode control. [Source: Tailwind CSS V4 docs]

**Custom Theme Colors:**

Define colors with `@theme` directive:

```css
@theme {
  --color-canvas: oklch(0.967 0.003 264.542);
  --color-primary: oklch(0.72 0.11 178);
}
```

This generates utilities like `bg-canvas`, `text-primary`, etc. [Source: Tailwind CSS V4 docs]

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
  - **npmRebuild: false** in electron-builder config (use manual rebuild)
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
| **IPC contracts in core** | Violates transport-agnostic principle. Core should not know about Electron IPC. [Added after research] |
| **const enum for TypeScript enums** | Incompatible with --isolatedModules (required by Vite). Use `as const` pattern instead. [Added after research] |

## Acceptance Criteria

### Functional Requirements (Updated with Integration Tests)

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
- [ ] Desktop reflects CLI changes in real-time (file watcher on both `.db` and `.db-wal`)
- [ ] Batch operations work with multiple task IDs
- [ ] `tasker get --json` outputs valid JSON
- [ ] `tasker get -r` shows full subtask tree
- [ ] Backup list/restore works
- [ ] **Integration:** Desktop sees CLI changes within 500ms (file watcher works)
- [ ] **Integration:** CLI sees desktop changes immediately (no stale reads)
- [ ] **Integration:** Concurrent writes don't crash (retry strategy works)
- [ ] **Integration:** File watcher triggers on WAL file changes, not just main DB

### Non-Functional Requirements

- [ ] TypeScript strict mode, no `any` types in production code
- [ ] All DB writes go through main process in Electron (never in renderer)
- [ ] Electron sandbox enabled, no `nodeIntegration`
- [ ] WAL mode + `busy_timeout` pragma + retry wrapper for concurrent CLI + desktop access
- [ ] Sort order convention preserved: highest `sort_order` = newest
- [ ] Task ordering preserved: InProgress → Pending → Done, then by priority → due → newest
- [ ] IPC boundaries have runtime validation with Zod [Added after research]
- [ ] IPC handlers wrapped with error handler to prevent main process crashes [Added after Kieran review]
- [ ] Branded types are optional (deferred to post-Phase 1 evaluation) [Updated after Kieran review]
- [ ] Integration tests cover concurrent CLI + desktop access scenarios [Added after Kieran review]
- [ ] Schema evolution strategy documented (migrations + version checks) [Added after Kieran review]
- [ ] better-sqlite3 properly rebuilt for Electron and excluded from asar [Added after research]

### Quality Gates

- [ ] `pnpm test` passes across all packages
- [ ] TypeScript compiles with zero errors (`pnpm build`)
- [ ] No `any` types in production code
- [ ] Core test coverage >= 80% (matching current xUnit coverage)
- [ ] All 17 undo command types have round-trip tests
- [ ] Integration test suite passes (CLI + desktop concurrent access)
- [ ] Error handling wrapper catches all IPC exceptions (no main process crashes during testing)

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
| zod | ^3 | Runtime validation at IPC boundaries [Added after research] |

## Risk Analysis & Mitigation (Updated After Reviews)

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| Electron bundle size (~150MB) | Low | Certain | Acceptable tradeoff for cross-platform + ecosystem |
| better-sqlite3 native module compilation | **High** | Medium | **Critical:** Configure electron-rebuild and asar exclusion on day one. Add to package.json scripts: `"rebuild": "electron-rebuild -f -w better-sqlite3"`. Set `node-linker=hoisted` in .npmrc. [Updated priority after research] |
| Drizzle can't abstract `json_each()` | Low | Certain | Use raw SQL for complex JSON queries (tags search) |
| Linux `tray.getBounds()` returns `{0,0,0,0}` | Low | Likely | electron-traywindow-positioner falls back to cursor position |
| IPC serialization: class instances lose methods | High | Certain | Use plain objects + utility functions; `Result<T>` pattern for errors |
| **Uncaught IPC exceptions crash main process** | **Critical** | High | **NEW:** Wrap all IPC handlers with error handler (`wrapIpcHandler`) that catches Zod, SQLite, and unexpected errors. [Kieran's Review - Critical Issue #1] |
| **SQLITE_BUSY despite busy_timeout** | **High** | Medium | **UPDATED:** WAL mode + `PRAGMA busy_timeout = 5000` + exponential backoff retry wrapper on all writes. busy_timeout alone may not be enough for long CLI operations. [Kieran's Review - Critical Issue #2] |
| **Branded types add friction without clear benefit** | Medium | High | **NEW:** Make branded types optional. Start with simple type aliases. Add brands only if actual bugs occur. [Kieran's Review - Critical Issue #3] |
| **Missing integration test coverage** | High | Medium | **NEW:** Add integration test layer for concurrent CLI + desktop access. Unit tests don't catch cross-process race conditions. [Kieran's Review - Critical Issue #4] |
| **Schema evolution undefined** | Medium | High | **NEW:** Document migration workflow (drizzle-kit generate, CLI migrate command, version checks). Desktop refuses to start on schema mismatch. [Kieran's Review - Architectural Concern #19] |
| Undo history format compatibility | Medium | Low | JSON format is schema-agnostic; discriminated unions map to same JSON |
| Date object serialization across IPC | High | Certain | **Critical:** Use ISO strings in data model, never `Date` objects. Parse to Date only at display boundary. [Updated priority after research] |
| File watcher missing CLI changes | **Medium** | High | **Critical:** Watch `tasker.db-wal` in addition to `tasker.db`. In WAL mode, writes go to the WAL file. [Added after research] |
| TypeScript const enum breaking Vite build | Medium | Certain | **Critical:** Use `as const` pattern instead of `const enum`. const enum is incompatible with `--isolatedModules`. [Added after research] |
| IPC type safety at runtime | Medium | High | **Critical:** Add Zod validation in main process handlers. TypeScript types don't exist at runtime. [Added after research] |

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
- **Add** `docs/reference/electron-rebuild.md` for native module troubleshooting [Added after research]
- **Add** `docs/reference/typescript-patterns.md` documenting branded types (optional), as const pattern, Zod validation [Added after research, updated after Kieran review]
- **Add** `docs/reference/ipc-error-handling.md` documenting error wrapper pattern and Result<T> serialization [Added after Kieran review]
- **Add** `docs/reference/schema-migrations.md` documenting migration workflow, version checks, and drizzle-kit usage [Added after Kieran review]
- **Add** `docs/reference/concurrent-access.md` documenting WAL mode, busy_timeout, retry strategy, and integration testing approach [Added after Kieran review]

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
- better-sqlite3 performance: https://github.com/WiseLibs/better-sqlite3/blob/master/docs/performance.md
- electron-builder docs: https://www.electron.build
- pnpm workspaces: https://pnpm.io/workspaces
- TypeScript project references: https://www.typescriptlang.org/docs/handbook/project-references
- Zod documentation: https://zod.dev

### Research Findings

**Critical discoveries from /deepen-plan research (2026-02-08):**

1. **better-sqlite3 + pnpm + Electron**: Native module requires `node-linker=hoisted` in .npmrc, electron-rebuild configuration, and asar exclusion. Common failure mode: `node-gyp rebuild` errors on Windows, symlink issues on macOS.

2. **TypeScript enum patterns**: `const enum` breaks `--isolatedModules` (required by Vite). Use `as const` object pattern instead for runtime values + type narrowing.

3. **IPC boundaries are trust boundaries**: Zod validation at main process handlers is non-negotiable. Renderer can send arbitrary data.

4. **WAL mode file structure**: Writes go to `tasker.db-wal`, not `tasker.db`. File watchers must watch the WAL file to detect CLI changes.

5. **Date serialization**: ISO strings in data model, Date objects only at display layer. JSON serialization loses Date methods.

6. **Architecture boundaries**: IPC contracts belong in desktop app, not in core. Core should be transport-agnostic.

7. **Undo system patterns**: Separate command data (pure objects) from execution logic (switch-based executor with injected dependencies).

8. **Monorepo conventions**: `packages/` is more conventional than `shared/` for shared code directory. Use TypeScript project references + composite builds for incremental compilation.

9. **Concurrent write handling**: `PRAGMA busy_timeout = 5000` is the TypeScript equivalent of C#'s CrossProcessLock. WAL mode alone doesn't prevent `SQLITE_BUSY`.

10. **Prepared statements**: Drizzle's `.prepare()` method is essential for performance in hot paths. Can be 2-3x faster for repeated queries.

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
13. **SQLite pragmas:** WAL mode + foreign keys ON + busy_timeout must be set on every connection.
14. **Status change sort:** Don't bump sort_order — sort on open, not during interaction.

### Related Work

- Task: `wh1` — "migrate to nodejs, electron, commander.js, react"
- Current version: 2.42.4 (C#/.NET 10.0)
- Target version: 3.0.0 (TypeScript/Node.js)
