import type { TaskStatus } from './task-status.js';
import type { Priority } from './priority.js';

/** Simple type aliases for documentation; branded types deferred per Kieran review */
export type TaskId = string;
export type ListName = string;

export interface Task {
  readonly id: TaskId;
  readonly description: string;
  readonly status: TaskStatus;
  readonly createdAt: string; // ISO string
  readonly listName: ListName;
  readonly dueDate: string | null; // yyyy-MM-dd
  readonly priority: Priority | null;
  readonly tags: string[] | null;
  readonly isTrashed: number;
  readonly sortOrder: number;
  readonly completedAt: string | null; // ISO string
  readonly parentId: TaskId | null;
}
