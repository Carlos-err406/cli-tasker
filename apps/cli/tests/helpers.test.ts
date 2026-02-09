import { describe, it, expect, vi, beforeEach } from 'vitest';
import { createTestDb, createList, listExists, setDefaultList } from '@tasker/core';
import type { TaskerDb } from '@tasker/core';
import { TaskStatus, Priority } from '@tasker/core';
import {
  resolveListFilter,
  resolveListForAdd,
  parseStatus,
  parsePriorityArg,
  $try,
} from '../src/helpers.js';

describe('resolveListFilter', () => {
  let db: TaskerDb;

  beforeEach(() => {
    db = createTestDb();
  });

  it('returns explicit list when provided', () => {
    expect(resolveListFilter(db, 'mylist', false)).toBe('mylist');
  });

  it('returns null when showAll is true', () => {
    expect(resolveListFilter(db, undefined, true)).toBeNull();
  });

  it('explicit list takes precedence over showAll', () => {
    expect(resolveListFilter(db, 'mylist', true)).toBe('mylist');
  });

  it('auto-detects list from cwd basename', () => {
    createList(db, 'my-project');
    expect(resolveListFilter(db, undefined, false, '/home/user/my-project')).toBe('my-project');
  });

  it('returns null when cwd list does not exist', () => {
    expect(resolveListFilter(db, undefined, false, '/home/user/unknown-dir')).toBeNull();
  });
});

describe('resolveListForAdd', () => {
  let db: TaskerDb;

  beforeEach(() => {
    db = createTestDb();
  });

  it('uses explicit list when provided', () => {
    expect(resolveListForAdd(db, 'mylist', false)).toBe('mylist');
  });

  it('falls back to default list when no explicit and showAll', () => {
    expect(resolveListForAdd(db, undefined, true)).toBe('tasks');
  });

  it('falls back to default list when no list resolved', () => {
    expect(resolveListForAdd(db, undefined, false, )).toBe('tasks');
  });
});

describe('parseStatus', () => {
  it('parses "pending"', () => {
    expect(parseStatus('pending')).toBe(TaskStatus.Pending);
  });

  it('parses "in-progress"', () => {
    expect(parseStatus('in-progress')).toBe(TaskStatus.InProgress);
  });

  it('parses "inprogress"', () => {
    expect(parseStatus('inprogress')).toBe(TaskStatus.InProgress);
  });

  it('parses "wip"', () => {
    expect(parseStatus('wip')).toBe(TaskStatus.InProgress);
  });

  it('parses "done"', () => {
    expect(parseStatus('done')).toBe(TaskStatus.Done);
  });

  it('parses "complete"', () => {
    expect(parseStatus('complete')).toBe(TaskStatus.Done);
  });

  it('parses "completed"', () => {
    expect(parseStatus('completed')).toBe(TaskStatus.Done);
  });

  it('is case-insensitive', () => {
    expect(parseStatus('DONE')).toBe(TaskStatus.Done);
    expect(parseStatus('In-Progress')).toBe(TaskStatus.InProgress);
  });

  it('returns null for unknown status', () => {
    expect(parseStatus('invalid')).toBeNull();
  });
});

describe('parsePriorityArg', () => {
  it('parses "high"', () => {
    expect(parsePriorityArg('high')).toBe(Priority.High);
  });

  it('parses "1"', () => {
    expect(parsePriorityArg('1')).toBe(Priority.High);
  });

  it('parses "p1"', () => {
    expect(parsePriorityArg('p1')).toBe(Priority.High);
  });

  it('parses "medium"', () => {
    expect(parsePriorityArg('medium')).toBe(Priority.Medium);
  });

  it('parses "low"', () => {
    expect(parsePriorityArg('low')).toBe(Priority.Low);
  });

  it('returns null for "clear"', () => {
    expect(parsePriorityArg('clear')).toBeNull();
  });

  it('returns null for unknown', () => {
    expect(parsePriorityArg('critical')).toBeNull();
  });
});

describe('$try', () => {
  it('calls the wrapped function', () => {
    const fn = vi.fn();
    $try(fn);
    expect(fn).toHaveBeenCalledOnce();
  });

  it('catches errors and logs them', () => {
    const consoleSpy = vi.spyOn(console, 'log').mockImplementation(() => {});
    $try(() => {
      throw new Error('test error');
    });
    expect(consoleSpy).toHaveBeenCalled();
    consoleSpy.mockRestore();
  });
});
