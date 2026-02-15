import { describe, it, expect } from 'vitest';
import { parseSearchFilters } from '../../src/parsers/search-filter-parser.js';
import { TaskStatus } from '../../src/types/task-status.js';
import { Priority } from '../../src/types/priority.js';

describe('parseSearchFilters', () => {
  // --- Plain text (no filters) ---

  it('returns plain text as descriptionQuery when no filters', () => {
    const f = parseSearchFilters('fix the login bug');
    expect(f.descriptionQuery).toBe('fix the login bug');
    expect(f.tags).toEqual([]);
    expect(f.status).toBeNull();
    expect(f.priority).toBeNull();
    expect(f.dueFilter).toBeNull();
    expect(f.listName).toBeNull();
    expect(f.has).toEqual({});
  });

  it('returns empty descriptionQuery for filter-only input', () => {
    const f = parseSearchFilters('status:done');
    expect(f.descriptionQuery).toBe('');
    expect(f.status).toBe(TaskStatus.Done);
  });

  // --- Tag filters ---

  it('parses a single tag filter', () => {
    const f = parseSearchFilters('tag:ui');
    expect(f.tags).toEqual(['ui']);
    expect(f.descriptionQuery).toBe('');
  });

  it('parses multiple tag filters (AND logic)', () => {
    const f = parseSearchFilters('tag:ui tag:bug');
    expect(f.tags).toEqual(['ui', 'bug']);
  });

  it('tags are case-insensitive', () => {
    const f = parseSearchFilters('tag:UI');
    expect(f.tags).toEqual(['ui']);
  });

  // --- Status filters ---

  it('parses status:done', () => {
    expect(parseSearchFilters('status:done').status).toBe(TaskStatus.Done);
  });

  it('parses status:pending', () => {
    expect(parseSearchFilters('status:pending').status).toBe(TaskStatus.Pending);
  });

  it('parses status:wip', () => {
    expect(parseSearchFilters('status:wip').status).toBe(TaskStatus.InProgress);
  });

  it('parses status:in-progress', () => {
    expect(parseSearchFilters('status:in-progress').status).toBe(TaskStatus.InProgress);
  });

  it('keeps unknown status as description text', () => {
    const f = parseSearchFilters('status:unknown');
    expect(f.status).toBeNull();
    expect(f.descriptionQuery).toBe('status:unknown');
  });

  // --- Priority filters ---

  it('parses priority:high', () => {
    expect(parseSearchFilters('priority:high').priority).toBe(Priority.High);
  });

  it('parses priority:p1', () => {
    expect(parseSearchFilters('priority:p1').priority).toBe(Priority.High);
  });

  it('parses priority:medium / p2', () => {
    expect(parseSearchFilters('priority:medium').priority).toBe(Priority.Medium);
    expect(parseSearchFilters('priority:p2').priority).toBe(Priority.Medium);
  });

  it('parses priority:low / p3', () => {
    expect(parseSearchFilters('priority:low').priority).toBe(Priority.Low);
    expect(parseSearchFilters('priority:p3').priority).toBe(Priority.Low);
  });

  it('keeps unknown priority as description text', () => {
    const f = parseSearchFilters('priority:urgent');
    expect(f.priority).toBeNull();
    expect(f.descriptionQuery).toBe('priority:urgent');
  });

  // --- Due filters ---

  it('parses due:today', () => {
    expect(parseSearchFilters('due:today').dueFilter).toBe('today');
  });

  it('parses due:overdue', () => {
    expect(parseSearchFilters('due:overdue').dueFilter).toBe('overdue');
  });

  it('parses due:week', () => {
    expect(parseSearchFilters('due:week').dueFilter).toBe('week');
  });

  it('parses due:month', () => {
    expect(parseSearchFilters('due:month').dueFilter).toBe('month');
  });

  it('keeps unknown due value as description text', () => {
    const f = parseSearchFilters('due:2026-03-01');
    expect(f.dueFilter).toBeNull();
    expect(f.descriptionQuery).toBe('due:2026-03-01');
  });

  // --- List filter ---

  it('parses list:backlog', () => {
    expect(parseSearchFilters('list:backlog').listName).toBe('backlog');
  });

  it('preserves list name case', () => {
    expect(parseSearchFilters('list:MyList').listName).toBe('MyList');
  });

  it('supports quoted list names', () => {
    expect(parseSearchFilters('list:"my list"').listName).toBe('my list');
  });

  // --- Has filters ---

  it('parses has:subtasks', () => {
    expect(parseSearchFilters('has:subtasks').has.subtasks).toBe(true);
  });

  it('parses has:parent', () => {
    expect(parseSearchFilters('has:parent').has.parent).toBe(true);
  });

  it('parses has:due', () => {
    expect(parseSearchFilters('has:due').has.due).toBe(true);
  });

  it('parses has:tags', () => {
    expect(parseSearchFilters('has:tags').has.tags).toBe(true);
  });

  it('keeps unknown has value as description text', () => {
    const f = parseSearchFilters('has:blockers');
    expect(f.has).toEqual({});
    expect(f.descriptionQuery).toBe('has:blockers');
  });

  // --- Combinations ---

  it('combines filters with free text', () => {
    const f = parseSearchFilters('fix bug tag:ui status:done');
    expect(f.descriptionQuery).toBe('fix bug');
    expect(f.tags).toEqual(['ui']);
    expect(f.status).toBe(TaskStatus.Done);
  });

  it('handles filters at different positions', () => {
    const f = parseSearchFilters('tag:bug review the PR priority:high');
    expect(f.descriptionQuery).toBe('review the PR');
    expect(f.tags).toEqual(['bug']);
    expect(f.priority).toBe(Priority.High);
  });

  it('combines multiple filter types', () => {
    const f = parseSearchFilters('tag:ui tag:urgent status:wip priority:high due:today list:sprint');
    expect(f.tags).toEqual(['ui', 'urgent']);
    expect(f.status).toBe(TaskStatus.InProgress);
    expect(f.priority).toBe(Priority.High);
    expect(f.dueFilter).toBe('today');
    expect(f.listName).toBe('sprint');
    expect(f.descriptionQuery).toBe('');
  });

  // --- Case insensitivity ---

  it('filter prefixes are case-insensitive', () => {
    const f = parseSearchFilters('Status:Done Priority:HIGH Tag:UI');
    expect(f.status).toBe(TaskStatus.Done);
    expect(f.priority).toBe(Priority.High);
    expect(f.tags).toEqual(['ui']);
  });

  // --- Edge cases ---

  it('handles empty input', () => {
    const f = parseSearchFilters('');
    expect(f.descriptionQuery).toBe('');
    expect(f.tags).toEqual([]);
  });

  it('collapses extra whitespace in remaining text', () => {
    const f = parseSearchFilters('  hello   tag:ui   world  ');
    expect(f.descriptionQuery).toBe('hello world');
    expect(f.tags).toEqual(['ui']);
  });

  // --- Negation filters ---

  it('parses status:!done as notStatus', () => {
    const f = parseSearchFilters('status:!done');
    expect(f.notStatus).toBe(TaskStatus.Done);
    expect(f.status).toBeNull();
    expect(f.descriptionQuery).toBe('');
  });

  it('parses tag:!ui as notTags', () => {
    const f = parseSearchFilters('tag:!ui');
    expect(f.notTags).toEqual(['ui']);
    expect(f.tags).toEqual([]);
  });

  it('parses multiple negated tags', () => {
    const f = parseSearchFilters('tag:!ui tag:!bug');
    expect(f.notTags).toEqual(['ui', 'bug']);
  });

  it('parses priority:!high as notPriority', () => {
    const f = parseSearchFilters('priority:!high');
    expect(f.notPriority).toBe(Priority.High);
    expect(f.priority).toBeNull();
  });

  it('parses due:!today as notDueFilter', () => {
    const f = parseSearchFilters('due:!today');
    expect(f.notDueFilter).toBe('today');
    expect(f.dueFilter).toBeNull();
  });

  it('parses list:!backlog as notListName', () => {
    const f = parseSearchFilters('list:!backlog');
    expect(f.notListName).toBe('backlog');
    expect(f.listName).toBeNull();
  });

  it('preserves case for negated list names', () => {
    const f = parseSearchFilters('list:!MyList');
    expect(f.notListName).toBe('MyList');
  });

  it('parses has:!due as notHas.due', () => {
    const f = parseSearchFilters('has:!due');
    expect(f.notHas.due).toBe(true);
    expect(f.has.due).toBeUndefined();
  });

  it('parses has:!subtasks as notHas.subtasks', () => {
    const f = parseSearchFilters('has:!subtasks');
    expect(f.notHas.subtasks).toBe(true);
  });

  it('parses has:!parent as notHas.parent', () => {
    const f = parseSearchFilters('has:!parent');
    expect(f.notHas.parent).toBe(true);
  });

  it('parses has:!tags as notHas.tags', () => {
    const f = parseSearchFilters('has:!tags');
    expect(f.notHas.tags).toBe(true);
  });

  it('keeps unknown negated status as text', () => {
    const f = parseSearchFilters('status:!foobar');
    expect(f.notStatus).toBeNull();
    expect(f.descriptionQuery).toBe('status:!foobar');
  });

  it('keeps unknown negated priority as text', () => {
    const f = parseSearchFilters('priority:!urgent');
    expect(f.notPriority).toBeNull();
    expect(f.descriptionQuery).toBe('priority:!urgent');
  });

  it('combines negated and positive filters', () => {
    const f = parseSearchFilters('status:!done tag:ui');
    expect(f.notStatus).toBe(TaskStatus.Done);
    expect(f.tags).toEqual(['ui']);
    expect(f.descriptionQuery).toBe('');
  });

  it('combines negated and positive tags', () => {
    const f = parseSearchFilters('tag:ui tag:!bug');
    expect(f.tags).toEqual(['ui']);
    expect(f.notTags).toEqual(['bug']);
  });

  // --- ID filter ---

  it('parses id:abc as idPrefix', () => {
    const f = parseSearchFilters('id:abc');
    expect(f.idPrefix).toBe('abc');
    expect(f.descriptionQuery).toBe('');
  });

  it('preserves case for id filter', () => {
    const f = parseSearchFilters('id:AbC');
    expect(f.idPrefix).toBe('AbC');
  });

  it('combines id filter with other filters', () => {
    const f = parseSearchFilters('id:abc status:done');
    expect(f.idPrefix).toBe('abc');
    expect(f.status).toBe(TaskStatus.Done);
  });

  it('combines id filter with free text', () => {
    const f = parseSearchFilters('fix bug id:abc');
    expect(f.idPrefix).toBe('abc');
    expect(f.descriptionQuery).toBe('fix bug');
  });
});
