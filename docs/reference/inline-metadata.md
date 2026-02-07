# Inline Metadata Parsing

`TaskDescriptionParser` parses the **last line** of a task description if it contains **only** metadata markers:

| Marker | Example | Meaning |
|--------|---------|---------|
| `p1` / `p2` / `p3` | `p1` | Priority: High / Medium / Low |
| `@date` | `@today`, `@friday`, `@+3d`, `@jan15` | Due date (natural language) |
| `#tag` | `#work` | Tag |
| `^abc` | `^f4d` | Subtask of task f4d |
| `!abc` | `!h67` | Blocks task h67 |

## Key Behaviors

- Only the last line is checked; if it contains any non-metadata text, nothing is parsed
- Original description text is kept intact (markers are not stripped from storage)
- `GetDisplayDescription()` hides the metadata-only last line for display
- `SyncMetadataToDescription()` updates the metadata line when properties change via commands
- `DateParser.Parse()` handles natural language dates: today, tomorrow, weekday names, monthDay (jan15), relative (+3d), and ISO format

## Examples

```
Buy groceries\np2 @friday #shopping
→ Medium priority, due Friday, tag "shopping"

Write unit tests\n^abc
→ Subtask of task abc

Fix auth bug\n!def !ghi
→ Blocks tasks def and ghi
```
