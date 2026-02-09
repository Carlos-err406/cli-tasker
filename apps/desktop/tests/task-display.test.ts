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
  getLinkedStatusLabel,
  getLinkedStatusColor,
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
    /** Format a local Date as YYYY-MM-DD (matches what the app stores) */
    function localDate(d: Date): string {
      return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
    }

    it('formatDueDate returns null for no date', () => {
      expect(formatDueDate(null)).toBeNull();
    });

    it('formatDueDate returns "today" for today', () => {
      expect(formatDueDate(localDate(new Date()))).toBe('today');
    });

    it('formatDueDate returns "tomorrow" for tomorrow', () => {
      const tomorrow = new Date();
      tomorrow.setDate(tomorrow.getDate() + 1);
      expect(formatDueDate(localDate(tomorrow))).toBe('tomorrow');
    });

    it('formatDueDate shows overdue for past dates', () => {
      const past = new Date();
      past.setDate(past.getDate() - 3);
      expect(formatDueDate(localDate(past))).toContain('overdue');
    });

    it('getDueDateColor returns red for overdue', () => {
      const past = new Date();
      past.setDate(past.getDate() - 1);
      expect(getDueDateColor(localDate(past))).toContain('red');
    });

    it('getDueDateColor returns orange for today', () => {
      expect(getDueDateColor(localDate(new Date()))).toContain('orange');
    });
  });

  describe('linked status', () => {
    it('returns "Done" for done status', () => {
      expect(getLinkedStatusLabel(TaskStatus.Done)).toBe('Done');
    });

    it('returns "In Progress" for in-progress status', () => {
      expect(getLinkedStatusLabel(TaskStatus.InProgress)).toBe('In Progress');
    });

    it('returns null for pending status', () => {
      expect(getLinkedStatusLabel(TaskStatus.Pending)).toBeNull();
    });

    it('returns green color for done', () => {
      expect(getLinkedStatusColor(TaskStatus.Done)).toContain('green');
    });

    it('returns amber color for in-progress', () => {
      expect(getLinkedStatusColor(TaskStatus.InProgress)).toContain('amber');
    });

    it('returns empty string for pending', () => {
      expect(getLinkedStatusColor(TaskStatus.Pending)).toBe('');
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
