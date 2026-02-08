import type { Task } from '@tasker/core';
import { getDisplayDescription, TaskStatus, Priority, PriorityName } from '@tasker/core';

export function getDisplayTitle(task: Task): string {
  return getDisplayDescription(task.description);
}

export function getShortId(task: Task): string {
  return task.id.slice(0, 3);
}

export function isPending(task: Task): boolean {
  return task.status === TaskStatus.Pending;
}

export function isInProgress(task: Task): boolean {
  return task.status === TaskStatus.InProgress;
}

export function isDone(task: Task): boolean {
  return task.status === TaskStatus.Done;
}

export function getPriorityLabel(priority: number | null): string | null {
  if (priority === null) return null;
  return PriorityName[priority as keyof typeof PriorityName] ?? null;
}

export function getPriorityIndicator(priority: number | null): string | null {
  switch (priority) {
    case Priority.High:
      return '>>>';
    case Priority.Medium:
      return '>>';
    case Priority.Low:
      return '>';
    default:
      return null;
  }
}

export function getPriorityColor(priority: number | null): string {
  switch (priority) {
    case Priority.High:
      return 'text-red-500';
    case Priority.Medium:
      return 'text-orange-400';
    case Priority.Low:
      return 'text-blue-400';
    default:
      return '';
  }
}

export function getDueDateColor(dueDate: string | null): string {
  if (!dueDate) return '';
  const today = new Date();
  today.setHours(0, 0, 0, 0);
  const due = new Date(dueDate + 'T00:00:00');

  if (due < today) return 'text-red-500'; // overdue
  if (due.getTime() === today.getTime()) return 'text-orange-400'; // today
  return 'text-muted-foreground';
}

export function formatDueDate(dueDate: string | null): string | null {
  if (!dueDate) return null;
  const today = new Date();
  today.setHours(0, 0, 0, 0);
  const due = new Date(dueDate + 'T00:00:00');
  const diff = Math.round((due.getTime() - today.getTime()) / (1000 * 60 * 60 * 24));

  if (diff < 0) return `${Math.abs(diff)}d overdue`;
  if (diff === 0) return 'today';
  if (diff === 1) return 'tomorrow';
  if (diff <= 7) return `in ${diff}d`;
  return dueDate;
}

// Deterministic color for tags (same tag always gets same color)
const TAG_COLORS = [
  'bg-blue-500/20 text-blue-300',
  'bg-green-500/20 text-green-300',
  'bg-purple-500/20 text-purple-300',
  'bg-pink-500/20 text-pink-300',
  'bg-yellow-500/20 text-yellow-300',
  'bg-cyan-500/20 text-cyan-300',
  'bg-orange-500/20 text-orange-300',
  'bg-red-500/20 text-red-300',
];

export function getTagColor(tag: string): string {
  let hash = 0;
  for (let i = 0; i < tag.length; i++) {
    hash = (hash * 31 + tag.charCodeAt(i)) | 0;
  }
  return TAG_COLORS[Math.abs(hash) % TAG_COLORS.length]!;
}
