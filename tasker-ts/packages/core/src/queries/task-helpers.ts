import type { TaskId, Task } from '../types/task.js';
import { TaskStatus } from '../types/task-status.js';
import { parse as parseDescription } from '../parsers/task-description-parser.js';

const ID_CHARS = '0123456789abcdefghijklmnopqrstuvwxyz';
const ID_LENGTH = 3;

/** Generate a random 3-character task ID */
export function generateId(): TaskId {
  let id = '';
  for (let i = 0; i < ID_LENGTH; i++) {
    id += ID_CHARS[Math.floor(Math.random() * ID_CHARS.length)];
  }
  return id;
}

/** Create a new Task object from a description and list name */
export function createTask(description: string, listName: string, now?: Date): Task {
  const trimmed = description.trim();
  const parsed = parseDescription(trimmed, now);

  return {
    id: generateId(),
    description: trimmed,
    status: TaskStatus.Pending,
    createdAt: (now ?? new Date()).toISOString(),
    listName,
    dueDate: parsed.dueDate,
    priority: parsed.priority,
    tags: parsed.tags.length > 0 ? parsed.tags : null,
    isTrashed: 0,
    sortOrder: 0,
    completedAt: null,
    parentId: parsed.parentId,
  };
}

/** Return a copy of the task with a new status (sets completedAt for Done) */
export function withStatus(task: Task, status: TaskStatus): Task {
  return {
    ...task,
    status,
    completedAt: status === TaskStatus.Done ? new Date().toISOString() : null,
  };
}

/** Return a copy of the task moved to a different list */
export function moveToList(task: Task, listName: string): Task {
  return { ...task, listName };
}

/** Sort order for display: InProgress(0), Pending(1), Done(2) */
export function statusSortOrder(status: TaskStatus): number {
  switch (status) {
    case TaskStatus.InProgress: return 0;
    case TaskStatus.Pending: return 1;
    case TaskStatus.Done: return 2;
    default: return 1;
  }
}

/** Days-until sort order for due dates. Past dates = 0 (most urgent), null = 99 */
export function dueDateSortOrder(dueDate: string | null, todayStr: string): number {
  if (!dueDate) return 99;
  if (dueDate < todayStr) return 0;
  // Rough days difference for sorting
  const due = new Date(dueDate + 'T00:00:00');
  const today = new Date(todayStr + 'T00:00:00');
  return Math.round((due.getTime() - today.getTime()) / 86400000);
}

/** Sort tasks for display: active (InProgress, Pending) sorted by status/priority/due, then done by completedAt DESC */
export function sortTasksForDisplay(
  tasks: Task[],
  todayStr?: string,
): Task[] {
  const today = todayStr ?? formatDate(new Date());

  const active = tasks
    .filter(t => t.status !== TaskStatus.Done)
    .sort((a, b) => {
      // Status: InProgress first
      const s = statusSortOrder(a.status) - statusSortOrder(b.status);
      if (s !== 0) return s;
      // Priority: lower number = higher priority, null sorts last
      const pa = a.priority ?? 99;
      const pb = b.priority ?? 99;
      if (pa !== pb) return pa - pb;
      // Due date: sooner first
      const da = dueDateSortOrder(a.dueDate, today);
      const db = dueDateSortOrder(b.dueDate, today);
      if (da !== db) return da - db;
      // Created: newer first
      return b.createdAt.localeCompare(a.createdAt);
    });

  const done = tasks
    .filter(t => t.status === TaskStatus.Done)
    .sort((a, b) => {
      // Most recently completed first
      const ca = a.completedAt ?? '';
      const cb = b.completedAt ?? '';
      return cb.localeCompare(ca);
    });

  return [...active, ...done];
}

function formatDate(d: Date): string {
  const y = d.getFullYear();
  const m = String(d.getMonth() + 1).padStart(2, '0');
  const day = String(d.getDate()).padStart(2, '0');
  return `${y}-${m}-${day}`;
}

/** Serialize tags array to JSON for storage, or null if empty */
export function serializeTags(tags: string[] | null): string | null {
  if (!tags || tags.length === 0) return null;
  return JSON.stringify(tags);
}

/** Deserialize tags from JSON string, or null if empty/malformed */
export function deserializeTags(json: string | null): string[] | null {
  if (!json) return null;
  try {
    const arr = JSON.parse(json) as unknown;
    if (Array.isArray(arr) && arr.length > 0) return arr as string[];
    return null;
  } catch {
    return null;
  }
}

/** Status label for messages */
export function statusLabel(status: TaskStatus): string {
  switch (status) {
    case TaskStatus.Pending: return 'pending';
    case TaskStatus.InProgress: return 'in-progress';
    case TaskStatus.Done: return 'done';
    default: return String(status);
  }
}
