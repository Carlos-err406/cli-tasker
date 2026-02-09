import { describe, it, expect, beforeEach } from 'vitest';
import { createTestDb, type TaskerDb } from '../../src/db.js';
import {
  getAllListNames,
  listHasTasks,
  listExists,
  createList,
  deleteList,
  renameList,
  isListCollapsed,
  setListCollapsed,
  reorderList,
  getListIndex,
} from '../../src/queries/list-queries.js';
import { addTask, getAllTasks } from '../../src/queries/task-queries.js';

let db: TaskerDb;

beforeEach(() => {
  db = createTestDb();
});

describe('getAllListNames', () => {
  it('returns default "tasks" list', () => {
    const names = getAllListNames(db);
    expect(names).toContain('tasks');
  });

  it('returns lists in sort order', () => {
    createList(db, 'work');
    createList(db, 'personal');
    const names = getAllListNames(db);
    expect(names[0]).toBe('tasks');
    expect(names).toContain('work');
    expect(names).toContain('personal');
  });
});

describe('listExists', () => {
  it('returns true for default list', () => {
    expect(listExists(db, 'tasks')).toBe(true);
  });

  it('returns false for nonexistent list', () => {
    expect(listExists(db, 'nope')).toBe(false);
  });

  it('returns true after creating list', () => {
    createList(db, 'work');
    expect(listExists(db, 'work')).toBe(true);
  });
});

describe('listHasTasks', () => {
  it('returns false for empty list', () => {
    expect(listHasTasks(db, 'tasks')).toBe(false);
  });

  it('returns true after adding a task', () => {
    addTask(db, 'test', 'tasks');
    expect(listHasTasks(db, 'tasks')).toBe(true);
  });
});

describe('deleteList', () => {
  it('deletes list and its tasks via cascade', () => {
    createList(db, 'temp');
    addTask(db, 'task in temp', 'temp');
    deleteList(db, 'temp');
    expect(listExists(db, 'temp')).toBe(false);
    expect(getAllTasks(db, 'temp')).toHaveLength(0);
  });
});

describe('renameList', () => {
  it('renames a list (cascade updates tasks)', () => {
    createList(db, 'old');
    addTask(db, 'task in old', 'old');
    renameList(db, 'old', 'new');
    expect(listExists(db, 'new')).toBe(true);
    expect(listExists(db, 'old')).toBe(false);
    expect(getAllTasks(db, 'new')).toHaveLength(1);
  });
});

describe('collapsed state', () => {
  it('defaults to not collapsed', () => {
    expect(isListCollapsed(db, 'tasks')).toBe(false);
  });

  it('sets and unsets collapsed', () => {
    setListCollapsed(db, 'tasks', true);
    expect(isListCollapsed(db, 'tasks')).toBe(true);

    setListCollapsed(db, 'tasks', false);
    expect(isListCollapsed(db, 'tasks')).toBe(false);
  });
});

describe('reorderList', () => {
  it('reorders lists', () => {
    createList(db, 'alpha');
    createList(db, 'beta');

    // Initial: tasks(0), alpha(1), beta(2)
    reorderList(db, 'beta', 0);

    const names = getAllListNames(db);
    expect(names[0]).toBe('beta');
  });
});

describe('getListIndex', () => {
  it('returns the index of a list', () => {
    createList(db, 'work');
    const idx = getListIndex(db, 'work');
    expect(idx).toBeGreaterThan(0);
  });

  it('returns -1 for nonexistent list', () => {
    expect(getListIndex(db, 'nope')).toBe(-1);
  });
});
