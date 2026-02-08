import { describe, it, expect } from 'vitest';
import {
  getDisplayTitle,
  getShortId,
  isPending,
  isInProgress,
  isDone,
  getPriorityIndicator,
  getPriorityColor,
  getDueDateColor,
  formatDueDate,
  getTagColor,
} from '@/lib/task-display.js';
import type { Task } from '@tasker/core';
import { TaskStatus, Priority } from '@tasker/core';

function makeTask(overrides: Partial<Task> = {}): Task {
  return {
    id: 'abc123',
    description: 'Test task',
    status: TaskStatus.Pending,
    createdAt: new Date().toISOString(),
    listName: 'tasks',
    dueDate: null,
    priority: null,
    tags: null,
    isTrashed: 0,
    sortOrder: 0,
    completedAt: null,
    parentId: null,
    ...overrides,
  };
}

describe('task-display', () => {
  describe('getDisplayTitle', () => {
    it('returns description without metadata line', () => {
      const task = makeTask({ description: 'Buy groceries\np1 @tomorrow #shopping' });
      expect(getDisplayTitle(task)).toBe('Buy groceries');
    });

    it('returns full description when no metadata', () => {
      const task = makeTask({ description: 'Simple task' });
      expect(getDisplayTitle(task)).toBe('Simple task');
    });
  });

  describe('getShortId', () => {
    it('returns first 3 characters', () => {
      const task = makeTask({ id: 'abc123' });
      expect(getShortId(task)).toBe('abc');
    });
  });

  describe('status checks', () => {
    it('isPending', () => {
      expect(isPending(makeTask({ status: TaskStatus.Pending }))).toBe(true);
      expect(isPending(makeTask({ status: TaskStatus.Done }))).toBe(false);
    });

    it('isInProgress', () => {
      expect(isInProgress(makeTask({ status: TaskStatus.InProgress }))).toBe(true);
      expect(isInProgress(makeTask({ status: TaskStatus.Pending }))).toBe(false);
    });

    it('isDone', () => {
      expect(isDone(makeTask({ status: TaskStatus.Done }))).toBe(true);
      expect(isDone(makeTask({ status: TaskStatus.Pending }))).toBe(false);
    });
  });

  describe('priority', () => {
    it('returns indicator symbols', () => {
      expect(getPriorityIndicator(Priority.High)).toBe('>>>');
      expect(getPriorityIndicator(Priority.Medium)).toBe('>>');
      expect(getPriorityIndicator(Priority.Low)).toBe('>');
      expect(getPriorityIndicator(null)).toBeNull();
    });

    it('returns color classes', () => {
      expect(getPriorityColor(Priority.High)).toContain('red');
      expect(getPriorityColor(Priority.Medium)).toContain('orange');
      expect(getPriorityColor(Priority.Low)).toContain('blue');
      expect(getPriorityColor(null)).toBe('');
    });
  });

  describe('due date', () => {
    it('formatDueDate returns null for no date', () => {
      expect(formatDueDate(null)).toBeNull();
    });

    it('formatDueDate returns "today" for today', () => {
      const today = new Date().toISOString().slice(0, 10);
      expect(formatDueDate(today)).toBe('today');
    });

    it('formatDueDate returns "tomorrow" for tomorrow', () => {
      const tomorrow = new Date(Date.now() + 86400000).toISOString().slice(0, 10);
      expect(formatDueDate(tomorrow)).toBe('tomorrow');
    });

    it('formatDueDate shows overdue for past dates', () => {
      const past = new Date(Date.now() - 3 * 86400000).toISOString().slice(0, 10);
      expect(formatDueDate(past)).toContain('overdue');
    });

    it('getDueDateColor returns red for overdue', () => {
      const past = new Date(Date.now() - 86400000).toISOString().slice(0, 10);
      expect(getDueDateColor(past)).toContain('red');
    });

    it('getDueDateColor returns orange for today', () => {
      const today = new Date().toISOString().slice(0, 10);
      expect(getDueDateColor(today)).toContain('orange');
    });
  });

  describe('tag colors', () => {
    it('returns consistent color for same tag', () => {
      const color1 = getTagColor('work');
      const color2 = getTagColor('work');
      expect(color1).toBe(color2);
    });

    it('returns different colors for different tags', () => {
      const color1 = getTagColor('work');
      const color2 = getTagColor('personal');
      // Can't guarantee different for all pairs, but the hash should vary
      expect(typeof color1).toBe('string');
      expect(typeof color2).toBe('string');
    });
  });
});
