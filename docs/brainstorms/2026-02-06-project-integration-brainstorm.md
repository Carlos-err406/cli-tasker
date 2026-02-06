# Brainstorm: Tasker Project Integration

**Date:** 2026-02-06
**Task:** #9f5

## What We're Building

Automatic project-list association based on directory name. When running `tasker` commands inside a directory whose name matches an existing list, the CLI auto-filters to that list. No init command or config files needed.

## Why This Approach

Convention-based (directory name = list name) was chosen over `.tasker/` folder because:
- Zero files added to project directories
- No init step required — works as soon as a matching list exists
- Simpler implementation — just check `cwd` name against existing lists

## Key Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Detection method | Directory name matches list name | No config files, no init ceremony |
| Init command | Not needed | Auto-detect always; if user wants a list they can `tasker lists create <name>` |
| UX indicator | Subtle indicator in output | Show something like `(project: cli-tasker)` so user knows filtering is active |
| Opt-out | `--all` / `-a` global flag | Overrides auto-filter to show all lists |
| `-l` interaction | `-l` overrides auto-detect | Explicit user intent always wins |
| Priority | `-l` > auto-detect > default (all lists) | Clear precedence chain |

## Behavior Summary

```
# In ~/projects/my-app/ where "my-app" list exists:
tasker list              → shows only "my-app" tasks (with indicator)
tasker list --all        → shows all lists (ignores auto-detect)
tasker list -l work      → shows "work" list (-l overrides)
tasker add "fix bug"     → adds to "my-app" list
tasker add "x" -l work   → adds to "work" (-l overrides)

# In ~/random-dir/ where no matching list exists:
tasker list              → shows all lists (normal behavior)
```

## Open Questions

- Should the indicator appear on every command or only on `list`?
- Should `tasker add` without `-l` use the auto-detected list or the configured default list? (Likely: auto-detected wins over default)
