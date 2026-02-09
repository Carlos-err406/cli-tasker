# Brainstorm: Migrate to Node.js, Electron, Commander.js, React

**Date:** 2026-02-08
**Status:** Decided
**Task:** wh1

## What We're Building

A greenfield rewrite of cli-tasker from .NET/C# to a TypeScript-based stack with two surfaces: CLI (for humans and agents) and a desktop tray app. The TUI is dropped — it was a stepping stone to the desktop app and is no longer needed. The existing SQLite database/schema is preserved.

### Current Stack → New Stack

| Layer | Current | New |
|-------|---------|-----|
| Language | C# (.NET 10.0) | TypeScript |
| Core library | TaskerCore | @tasker/core |
| CLI framework | System.CommandLine | Commander.js |
| ~~TUI rendering~~ | ~~Spectre.Console + custom ANSI~~ | ~~Dropped~~ |
| Desktop app | Avalonia (macOS) | Electron + React |
| UI components | — | shadcn/ui (Radix-based) |
| Styling | — | Tailwind CSS V4 (CSS variables, no config, no PostCSS) |
| Database | Microsoft.Data.Sqlite | better-sqlite3 (via Drizzle ORM) |
| ORM | — (raw SQL) | Drizzle ORM |
| Test framework | xUnit | Vitest |
| Build tooling | MSBuild / dotnet CLI | Vite + pnpm workspaces |
| Distribution | dotnet global tool | npm global package |

## Why This Approach

**Motivations:**
1. **Web tech familiarity** — JS/TS/React is a more comfortable development environment
2. **Cross-platform goals** — Electron runs on macOS, Windows, and Linux out of the box
3. **Ecosystem & tooling** — npm ecosystem, richer component libraries, faster iteration

**Why drop the TUI:** The TUI was a middle step toward a graphical interface. Now that the desktop app exists, human users interact through the tray app and agents interact through the CLI. The TUI serves neither audience well enough to justify porting.

**Why greenfield over incremental:** The codebase is ~17,740 lines across 102 C# files. A fresh start allows cleaner architecture decisions without legacy constraints.

**Why monorepo:** Mirrors the current TaskerCore shared library pattern. Both surfaces import from the same core, so atomic changes are essential for consistency.

**Template reference:** The Electron desktop app follows the architecture of `currency-exchange-widget` — including its main/preload/renderer split, modular IPC channels, and shadcn/Tailwind V4 styling.

## Key Decisions

### 1. Project Structure: pnpm Monorepo

Shared libraries in `shared/`, applications in `apps/`:

```
tasker/
├── shared/
│   └── core/              # @tasker/core — models, data layer (Drizzle), undo, parsers
│       ├── src/
│       │   ├── schema/    # Drizzle table definitions (introspected)
│       │   ├── queries/   # Shared query functions (TaskQueries, ListQueries, etc.)
│       │   ├── types/     # Exported types (Task, NewTask, TaskStatus, etc.)
│       │   ├── ipc/       # IPC contracts (shared type-safe channel definitions)
│       │   ├── undo/      # Undo/redo command types and handlers
│       │   ├── parsers/   # Metadata parser, date parser
│       │   └── db.ts      # Connection factory
│       ├── drizzle/       # Generated migrations
│       ├── package.json
│       └── tsconfig.json
├── apps/
│   ├── cli/               # @tasker/cli — Commander.js commands
│   └── desktop/           # @tasker/desktop — Electron + React + shadcn tray app
├── pnpm-workspace.yaml    # packages: ['shared/*', 'apps/*']
├── .npmrc                 # auto-install-peers, link-workspace-packages
├── tsconfig.base.json     # Shared TS config (ES2022, strict, composite)
├── tsconfig.json          # Solution file with references
└── vitest.workspace.ts    # ['shared/*/vitest.config.ts', 'apps/*/vitest.config.ts']
```

**Workspace protocol:** Apps reference core via `"@tasker/core": "workspace:*"` in package.json.
**Build order:** TypeScript project references ensure core builds before apps. `tsc --build` resolves automatically.
**pnpm-workspace.yaml:**
```yaml
packages:
  - 'shared/*'
  - 'apps/*'
```

### 2. TypeScript Throughout

TypeScript for type safety, matching the strong typing of the current C# codebase. Key mappings:
- C# immutable records → TypeScript `readonly` interfaces or branded types
- C# enums → TypeScript const enums or union types
- LINQ → Array methods
- `IUndoableCommand` → TypeScript interface with discriminated unions

### 3. Drizzle ORM + better-sqlite3: Same Schema, Same DB File

- **Drizzle ORM** as the data access layer over better-sqlite3
- **Introspect** the existing database to generate Drizzle schema (`drizzle-kit pull`)
- Read the existing database at `~/Library/Application Support/cli-tasker/tasker.db`
- No schema changes — existing data works immediately
- WAL mode for concurrent access (CLI + desktop running simultaneously)
- Cross-platform: XDG paths on Linux, AppData on Windows
- Drizzle handles all write serialization; funnel writes through main process in Electron

### 4. Commander.js for CLI

- 17 commands to port (add, list, check, uncheck, status, wip, delete, clear, rename, get, move, due, priority, deps, lists, trash, system)
- Plus: undo, redo, history
- Subcommand pattern for grouped commands (deps, lists, trash, system)

### 5. Electron + React + shadcn for Desktop App

Following the `currency-exchange-widget` architecture:

**Electron structure:**
```
apps/desktop/
├── electron/
│   ├── main.ts              # App lifecycle, window creation, tray setup
│   ├── preload.ts           # contextBridge, exposes IPC to renderer
│   ├── electron-env.d.ts    # TypeScript definitions
│   ├── lib/
│   │   ├── index.ts
│   │   ├── window.ts        # Window creation & popup positioning
│   │   ├── tray.ts          # Tray icon, toggle, context menu
│   │   └── config.ts        # Config & position persistence
│   └── ipc/                 # Modular IPC channels (one dir per feature)
│       ├── register.ts      # Central IPC registration hub
│       ├── types.d.ts       # IPC type definitions
│       ├── tasks/           # Task CRUD operations
│       │   ├── channels.ts  # Channel name constants
│       │   ├── main.ts      # Handler functions + registration
│       │   ├── preload.ts   # Invoker factory for renderer
│       │   └── utils.ts     # Shared helpers for this channel
│       ├── lists/           # List operations
│       │   ├── channels.ts
│       │   ├── main.ts
│       │   ├── preload.ts
│       │   └── utils.ts
│       ├── undo/            # Undo/redo operations
│       │   ├── channels.ts
│       │   ├── main.ts
│       │   ├── preload.ts
│       │   └── utils.ts
│       └── window/          # Window resize, drag, position
│           ├── channels.ts
│           ├── main.ts
│           ├── preload.ts
│           └── utils.ts
├── src/                     # Renderer (React app)
│   ├── main.tsx
│   ├── app.tsx
│   ├── styles.css           # Tailwind V4 + CSS variables
│   ├── components/
│   │   ├── TaskList.tsx
│   │   ├── TaskItem.tsx
│   │   ├── QuickAdd.tsx
│   │   ├── SearchBar.tsx
│   │   └── ui/              # shadcn/ui components
│   └── lib/
│       ├── services/        # IPC wrapper functions
│       └── utils.ts         # cn() helper
├── vite.config.ts
└── electron-builder.json5
```

**UI stack:**
- **React** for components
- **shadcn/ui** (Radix-based) for accessible, styled components
- **Tailwind CSS V4** with CSS variables — no tailwind.config, no PostCSS
- **electron-builder** for packaging macOS/Windows/Linux

**IPC pattern** (from currency-exchange-widget):
- Each feature gets its own directory under `ipc/`
- `channels.ts` — string constants for channel names
- `main.ts` — handler functions + registration
- `preload.ts` — invoker factory for safe renderer access
- `utils.ts` — shared helpers specific to that channel
- Central `register.ts` combines all channel registrations
- Central `types.d.ts` for shared IPC type definitions

**Security:**
- Preload script with `contextBridge` (no `nodeIntegration`)
- Sandbox enabled
- No direct Node.js in renderer

### 6. Full Undo/Redo System Port

- All 17 undo command types will be recreated
- Command pattern with TypeScript discriminated unions
- JSON serialization compatible with existing `undo_history` table
- Composite commands for batch operations
- Same two-stack (undo/redo) architecture stored in SQLite via Drizzle

### 7. npm Global Package Distribution

- CLI: `npm install -g @tasker/cli`
- Desktop app: Built and distributed separately (Electron binary via electron-builder)
- Build script similar to current `update.sh`

### 8. Styling: Tailwind V4 + CSS Variables

- Tailwind CSS V4 with `@tailwindcss/vite` plugin
- CSS variables for theming (matches currency-exchange-widget approach)
- No `tailwind.config.ts` — V4 uses CSS-first configuration
- No PostCSS — Vite plugin handles everything
- Dark mode support via CSS variables

### 9. Testing: Vitest

- Vitest across all packages (workspace configuration)
- In-memory SQLite for isolated data layer tests (mirrors current xUnit approach)
- React Testing Library for desktop app component tests
- 24 existing test files serve as specification — port test logic to validate parity

## Mapping: Current Features → New Implementation

### Core (shared/core)

| Feature | C# Implementation | TypeScript Implementation |
|---------|-------------------|--------------------------|
| Models | `TodoTask` record | `readonly` interface + factory |
| Data layer | `TodoTaskList.cs` (1,953 lines) | Repository class with Drizzle ORM |
| Schema | Raw SQL migrations | Drizzle schema (introspected from existing DB) |
| Metadata parser | `TaskDescriptionParser.cs` | Regex-based parser (port logic directly) |
| Date parser | `DateParser.cs` | dayjs or custom parser |
| Undo system | 17 `IUndoableCommand` types | Discriminated union + command handlers |
| Config | `ConfigManager.cs` | Config class reading from SQLite via Drizzle |
| Backup | SQLite backup API | better-sqlite3 `.backup()` method |

### CLI (apps/cli)

| Feature | C# Implementation | TypeScript Implementation |
|---------|-------------------|--------------------------|
| Commands | 17 files in AppCommands/ | Commander.js command definitions |
| Output formatting | `Output.cs` + Spectre.Console | chalk + custom formatters |
| Batch operations | Multiple ID support | Same pattern with Commander variadic args |
| Directory detection | `DirectoryListDetector.cs` | `process.cwd()` + path matching |

### Desktop (apps/desktop)

| Feature | C# Implementation | TypeScript Implementation |
|---------|-------------------|--------------------------|
| Menu bar | Avalonia TrayIcon | Electron Tray API |
| Popup window | `TaskListPopup.axaml` | Electron BrowserWindow + React + shadcn |
| Drag & drop | Avalonia behaviors | @dnd-kit |
| Inline editing | Click-to-edit pattern | React state + shadcn Input |
| File watcher | FileSystemWatcher | chokidar |
| Quick add | Separate window | Electron BrowserWindow (frameless) |
| Collapsible lists | Avalonia animation | shadcn Collapsible + CSS transitions |

### Dropped (TUI)

The following TUI features are **not** being ported. They were specific to the terminal interface:
- `TuiRenderer.cs` (658 lines) — terminal rendering
- `TuiKeyHandler.cs` (774 lines) — keyboard shortcuts
- `TuiState.cs` — immutable TUI state
- Modal dialogs, viewport scrolling, alternate screen buffer
- Multi-select mode with keyboard navigation

The desktop app replaces all of these with richer equivalents (React components, shadcn UI, drag-and-drop, etc.).

## Resolved Questions

1. **Desktop framework:** Electron — all-JS stack, no Rust, mature ecosystem
2. **UI components:** shadcn/ui (Radix-based) with Tailwind V4
3. **ORM:** Drizzle ORM over better-sqlite3
4. **Schema approach:** Introspect existing DB with `drizzle-kit pull`
5. **Build tooling:** Vite + pnpm workspaces
6. **Testing:** Vitest across all packages
7. **Version numbering:** Start at 3.0.0 (continuation of the product after current 2.42.4)
8. **Package naming:** Scoped `@tasker/*` namespace — `@tasker/cli`, `@tasker/desktop`. CLI command stays `tasker`.
9. **Drag & drop:** @dnd-kit (modern, TypeScript-first, actively maintained — react-beautiful-dnd is archived)
10. **File watching:** chokidar (cross-platform, debounced, handles atomic writes)
11. **Auto-start:** node-auto-launch (macOS Login Item, Windows registry, Linux .desktop)
12. **Tray positioning:** electron-traywindow-positioner (handles Linux `getBounds()` returning 0,0)
13. **IPC type safety:** Shared contract interface in `@tasker/core/ipc/contracts.ts` — lightweight, no tRPC dependency
14. **Monorepo layout:** `shared/` for core library, `apps/` for CLI and desktop
15. **TUI:** Dropped. Two surfaces only: CLI (humans + agents) and desktop (humans).

## Research Findings

### Drizzle + SQLite

- **WAL mode:** Set via `sqlite.pragma('journal_mode = WAL')` on the raw better-sqlite3 connection before passing to Drizzle. Drizzle doesn't handle pragmas directly.
- **Foreign keys:** Must enable `sqlite.pragma('foreign_keys = ON')` — SQLite disables by default. Drizzle supports `{ onDelete: 'cascade' }` on `.references()`.
- **JSON columns:** Use `text('tags', { mode: 'json' }).$type<string[]>()`. Drizzle auto-serializes/deserializes. Complex JSON queries (like `json_each`) require raw SQL.
- **In-memory testing:** `new Database(':memory:')` works with Drizzle. Run migrations via `migrate(db, { migrationsFolder: './drizzle' })`.
- **Self-referencing FK (subtasks):** Use standalone `foreignKey()` operator in table definition.
- **Monorepo pattern:** Shared `@tasker/core` package exports schema, connection factory, and query classes. CLI uses directly, Electron uses via IPC handlers in main process.

### Electron Desktop App

- **DB access:** better-sqlite3 (via Drizzle) runs only in main process. Renderer accesses via IPC invoke/handle. Never expose raw ipcRenderer.
- **Popup window:** Frameless + transparent BrowserWindow. `window.on('blur', () => window.hide())` for auto-dismiss. CSS `backdrop-filter: blur(20px)` for native feel.
- **macOS dock:** `app.dock.hide()` + `LSUIElement: true` in electron-builder config
- **Windows taskbar:** `window.setSkipTaskbar(true)`
- **Linux tray caveat:** `tray.getBounds()` returns `{0,0,0,0}` — use electron-traywindow-positioner which falls back to cursor position
- **IPC events (main→renderer):** Use `BrowserWindow.getAllWindows().forEach(win => win.webContents.send('tasks:updated', data))` for broadcasting changes

### IPC Type Safety

Typed IPC contracts pattern (lightweight, no tRPC overhead):

- **Shared contracts** in `@tasker/core/ipc/contracts.ts` — defines channel names, parameter types, and return types
- **Type-safe handler helper** in Electron main process — `handleIpc<T extends IpcChannel>(channel, handler)` enforces correct types
- **Type-safe invoke helper** in preload — `invoke<T extends IpcChannel>(channel, params): Promise<ReturnType>` ensures renderer gets correct types
- **`window.taskerAPI`** typed via `declare global { interface Window { taskerAPI: TaskerAPI } }` in `preload.d.ts`
- **Drizzle types flow end-to-end:** Schema → `$inferSelect` → IPC contract → renderer component (full autocomplete)

**Serialization gotchas:**
- Electron uses Structured Clone Algorithm — class instances lose methods, use plain objects + utility functions
- Date objects may serialize inconsistently — use Unix timestamps or ISO strings, convert at boundaries
- `undefined` vs `null` — explicitly use `null` for optional values to avoid cross-process quirks
- Error objects only preserve `message` — return `Result<T>` objects (`{ success: true, data }` / `{ success: false, error }`) instead of throwing

### Monorepo Configuration

Key config files (exact content documented in research):
- **tsconfig.base.json:** ES2022 target, strict mode, composite, declaration, bundler moduleResolution
- **Per-package tsconfig.json:** Extends base, adds `references` to `@tasker/core`
- **apps/desktop tsconfig:** Separate configs for renderer (JSX) and main process (Node)
- **apps/desktop vite.config.ts:** `vite-plugin-electron` with `better-sqlite3` marked as external in rollup
- **Vitest:** Workspace config runs all package tests. Core uses `environment: 'node'`, desktop uses `environment: 'jsdom'`
- **.npmrc:** `link-workspace-packages=true`, `auto-install-peers=true`, `node-linker=isolated`

## All Questions Resolved

Migration verification: Unit tests with seeded data that mirrors the existing schema. No integration tests against the real DB — the introspected Drizzle schema + seeded fixtures provide sufficient confidence.

## Risks

- **Electron bundle size:** ~150MB+ vs current ~30MB Avalonia app. Acceptable tradeoff for cross-platform and ecosystem benefits.
- **Feature parity scope:** Reduced from three surfaces to two. Core + CLI + desktop is significantly less work than the original three-surface port.
- **SQLite in Electron:** All DB writes through main process IPC. WAL mode enables concurrent reads from CLI while desktop is running.
- **Drizzle JSON queries:** `json_each()` and similar SQLite JSON functions require raw SQL — Drizzle's query builder doesn't abstract them.
- **Linux tray:** `getBounds()` broken — mitigated by electron-traywindow-positioner but worth testing.
