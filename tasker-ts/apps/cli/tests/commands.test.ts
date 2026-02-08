import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { createTestDb, addTask, getTaskById, getAllTasks, getTrash } from '@tasker/core';
import { setStatuses, deleteTasks, renameTask, moveTask, createList } from '@tasker/core';
import { setTaskDueDate, setTaskPriority, restoreFromTrash, clearTrash } from '@tasker/core';
import { setParent, unsetParent, addBlocker, removeBlocker, addRelated, removeRelated } from '@tasker/core';
import { getAllListNames, deleteList, renameList as renameListFn, isValidListName } from '@tasker/core';
import { getDefaultList, setDefaultList, getStats } from '@tasker/core';
import { TaskStatus, Priority } from '@tasker/core';
import type { TaskerDb } from '@tasker/core';

// These tests verify the core functions that the CLI commands wrap.
// This ensures the CLI will behave correctly.

describe('CLI command operations', () => {
  let db: TaskerDb;

  beforeEach(() => {
    db = createTestDb();
  });

  describe('add', () => {
    it('adds a task with metadata markers', () => {
      const { task } = addTask(db, 'Buy groceries\n@tomorrow #shopping p1', 'tasks');
      expect(task.tags).toContain('shopping');
      expect(task.dueDate).not.toBeNull();
      expect(task.priority).toBe(Priority.High);
      expect(task.listName).toBe('tasks');
    });

    it('adds to specified list', () => {
      createList(db, 'work');
      const { task } = addTask(db, 'Finish report', 'work');
      expect(task.listName).toBe('work');
    });
  });

  describe('check/uncheck', () => {
    it('checks a task (sets status to Done)', () => {
      const { task } = addTask(db, 'Test task', 'tasks');
      const result = setStatuses(db, [task.id], TaskStatus.Done);
      expect(result.results[0]!.type).toBe('success');

      const updated = getTaskById(db, task.id)!;
      expect(updated.status).toBe(TaskStatus.Done);
    });

    it('unchecks a task (sets status to Pending)', () => {
      const { task } = addTask(db, 'Test task', 'tasks');
      setStatuses(db, [task.id], TaskStatus.Done);
      const result = setStatuses(db, [task.id], TaskStatus.Pending);
      expect(result.results[0]!.type).toBe('success');

      const updated = getTaskById(db, task.id)!;
      expect(updated.status).toBe(TaskStatus.Pending);
    });

    it('handles not-found task', () => {
      const result = setStatuses(db, ['nonexistent'], TaskStatus.Done);
      expect(result.results[0]!.type).toBe('not-found');
    });
  });

  describe('delete', () => {
    it('soft-deletes tasks', () => {
      const { task } = addTask(db, 'Delete me', 'tasks');
      const result = deleteTasks(db, [task.id]);
      expect(result.results[0]!.type).toBe('success');
      expect(getTaskById(db, task.id)).toBeNull();
    });
  });

  describe('rename', () => {
    it('renames a task', () => {
      const { task } = addTask(db, 'Old name', 'tasks');
      const result = renameTask(db, task.id, 'New name');
      expect(result.type).toBe('success');

      const updated = getTaskById(db, task.id)!;
      expect(updated.description).toBe('New name');
    });
  });

  describe('move', () => {
    it('moves a task between lists', () => {
      createList(db, 'work');
      const { task } = addTask(db, 'Task to move', 'tasks');
      const result = moveTask(db, task.id, 'work');
      expect(result.type).toBe('success');

      const updated = getTaskById(db, task.id)!;
      expect(updated.listName).toBe('work');
    });
  });

  describe('due', () => {
    it('sets a due date', () => {
      const { task } = addTask(db, 'Task', 'tasks');
      const result = setTaskDueDate(db, task.id, '2026-12-31');
      expect(result.type).toBe('success');

      const updated = getTaskById(db, task.id)!;
      expect(updated.dueDate).toBe('2026-12-31');
    });

    it('clears a due date', () => {
      const { task } = addTask(db, 'Task @tomorrow', 'tasks');
      const result = setTaskDueDate(db, task.id, null);
      expect(result.type).toBe('success');

      const updated = getTaskById(db, task.id)!;
      expect(updated.dueDate).toBeNull();
    });
  });

  describe('priority', () => {
    it('sets priority', () => {
      const { task } = addTask(db, 'Task', 'tasks');
      const result = setTaskPriority(db, task.id, Priority.High);
      expect(result.type).toBe('success');

      const updated = getTaskById(db, task.id)!;
      expect(updated.priority).toBe(Priority.High);
    });

    it('clears priority', () => {
      const { task } = addTask(db, 'Task p1', 'tasks');
      const result = setTaskPriority(db, task.id, null);
      expect(result.type).toBe('success');

      const updated = getTaskById(db, task.id)!;
      expect(updated.priority).toBeNull();
    });
  });

  describe('status/wip', () => {
    it('sets status to in-progress', () => {
      const { task } = addTask(db, 'Task', 'tasks');
      const result = setStatuses(db, [task.id], TaskStatus.InProgress);
      expect(result.results[0]!.type).toBe('success');

      const updated = getTaskById(db, task.id)!;
      expect(updated.status).toBe(TaskStatus.InProgress);
    });

    it('batch sets status for multiple tasks', () => {
      const { task: t1 } = addTask(db, 'Task 1', 'tasks');
      const { task: t2 } = addTask(db, 'Task 2', 'tasks');
      const result = setStatuses(db, [t1.id, t2.id], TaskStatus.Done);
      expect(result.results.length).toBe(2);
      expect(result.results.every(r => r.type === 'success')).toBe(true);
    });
  });

  describe('trash', () => {
    it('lists trashed tasks', () => {
      const { task } = addTask(db, 'Trash me', 'tasks');
      deleteTasks(db, [task.id]);
      const trash = getTrash(db);
      expect(trash.length).toBe(1);
      expect(trash[0]!.id).toBe(task.id);
    });

    it('restores from trash', () => {
      const { task } = addTask(db, 'Restore me', 'tasks');
      deleteTasks(db, [task.id]);
      const result = restoreFromTrash(db, task.id);
      expect(result.type).toBe('success');
      expect(getTaskById(db, task.id)).not.toBeNull();
    });

    it('clears trash', () => {
      const { task } = addTask(db, 'Clear me', 'tasks');
      deleteTasks(db, [task.id]);
      const count = clearTrash(db);
      expect(count).toBe(1);
      expect(getTrash(db).length).toBe(0);
    });
  });

  describe('lists management', () => {
    it('creates and lists lists', () => {
      createList(db, 'work');
      const names = getAllListNames(db);
      expect(names).toContain('tasks');
      expect(names).toContain('work');
    });

    it('deletes a list', () => {
      createList(db, 'temp');
      deleteList(db, 'temp');
      const names = getAllListNames(db);
      expect(names).not.toContain('temp');
    });

    it('renames a list', () => {
      createList(db, 'old');
      renameListFn(db, 'old', 'new');
      const names = getAllListNames(db);
      expect(names).toContain('new');
      expect(names).not.toContain('old');
    });

    it('manages default list', () => {
      expect(getDefaultList(db)).toBe('tasks');
      createList(db, 'work');
      setDefaultList(db, 'work');
      expect(getDefaultList(db)).toBe('work');
    });

    it('validates list names', () => {
      expect(isValidListName('valid-name')).toBe(true);
      expect(isValidListName('also_valid')).toBe(true);
      expect(isValidListName('has spaces')).toBe(false);
      expect(isValidListName('')).toBe(false);
    });
  });

  describe('system status', () => {
    it('returns stats', () => {
      addTask(db, 'Task 1', 'tasks');
      addTask(db, 'Task 2', 'tasks');
      const { task: t3 } = addTask(db, 'Task 3', 'tasks');
      setStatuses(db, [t3.id], TaskStatus.Done);

      const stats = getStats(db, 'tasks');
      expect(stats.total).toBe(3);
      expect(stats.pending).toBe(2);
      expect(stats.done).toBe(1);
    });
  });

  describe('deps', () => {
    it('sets and unsets parent', () => {
      const { task: parent } = addTask(db, 'Parent', 'tasks');
      const { task: child } = addTask(db, 'Child', 'tasks');

      const setResult = setParent(db, child.id, parent.id);
      expect(setResult.type).toBe('success');
      expect(getTaskById(db, child.id)!.parentId).toBe(parent.id);

      const unsetResult = unsetParent(db, child.id);
      expect(unsetResult.type).toBe('success');
      expect(getTaskById(db, child.id)!.parentId).toBeNull();
    });

    it('adds and removes blocker', () => {
      const { task: blocker } = addTask(db, 'Blocker', 'tasks');
      const { task: blocked } = addTask(db, 'Blocked', 'tasks');

      const addResult = addBlocker(db, blocker.id, blocked.id);
      expect(addResult.type).toBe('success');

      const removeResult = removeBlocker(db, blocker.id, blocked.id);
      expect(removeResult.type).toBe('success');
    });

    it('adds and removes related', () => {
      const { task: t1 } = addTask(db, 'Task 1', 'tasks');
      const { task: t2 } = addTask(db, 'Task 2', 'tasks');

      const addResult = addRelated(db, t1.id, t2.id);
      expect(addResult.type).toBe('success');

      const removeResult = removeRelated(db, t1.id, t2.id);
      expect(removeResult.type).toBe('success');
    });
  });
});
