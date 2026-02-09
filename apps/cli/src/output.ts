/**
 * chalk-based output formatting matching the C# Spectre.Console output.
 */

import chalk from 'chalk';
import { TaskStatus, Priority } from '@tasker/core';
import type { TaskResult, BatchResult } from '@tasker/core';

// --- Tag colors (deterministic from tag name) ---

const TAG_COLORS = [
  chalk.cyan, chalk.magenta, chalk.blue, chalk.yellow,
  chalk.green, chalk.red, chalk.white, chalk.gray,
];

function tagColor(tag: string): (s: string) => string {
  let hash = 0;
  for (let i = 0; i < tag.length; i++) {
    hash = ((hash << 5) - hash + tag.charCodeAt(i)) | 0;
  }
  return TAG_COLORS[Math.abs(hash) % TAG_COLORS.length]!;
}

// --- Formatting functions ---

export function formatCheckbox(status: number): string {
  switch (status) {
    case TaskStatus.Done: return chalk.green('[x]');
    case TaskStatus.InProgress: return chalk.yellow('[-]');
    default: return chalk.gray('[ ]');
  }
}

export function formatLinkedStatus(status: number): string {
  switch (status) {
    case TaskStatus.Done: return chalk.green(' Done');
    case TaskStatus.InProgress: return chalk.yellow(' In Progress');
    default: return '';
  }
}

export function formatPriority(priority: number | null): string {
  switch (priority) {
    case Priority.High: return chalk.red.bold('>>>');
    case Priority.Medium: return chalk.yellow('>> ');
    case Priority.Low: return chalk.blue('>  ');
    default: return chalk.dim('·  ');
  }
}

export function formatDueDate(
  dueDate: string | null,
  status: number = TaskStatus.Pending,
  completedAt: string | null = null,
): string {
  if (!dueDate) return '';

  const dueParts = dueDate.split('-').map(Number);
  const dueD = new Date(dueParts[0]!, dueParts[1]! - 1, dueParts[2]!);

  // For completed tasks, freeze the label based on completion time
  if (status === TaskStatus.Done && completedAt) {
    const completedDate = new Date(completedAt);
    const compD = new Date(completedDate.getFullYear(), completedDate.getMonth(), completedDate.getDate());
    const lateDays = Math.floor((compD.getTime() - dueD.getTime()) / 86400000);
    return lateDays > 0
      ? chalk.dim(`  Completed ${lateDays}d late`)
      : chalk.dim(`  Due: ${formatMonthDay(dueD)}`);
  }

  const today = new Date();
  const todayD = new Date(today.getFullYear(), today.getMonth(), today.getDate());
  const diff = Math.floor((dueD.getTime() - todayD.getTime()) / 86400000);

  if (diff < 0) return chalk.red(`  OVERDUE (${-diff}d)`);
  if (diff === 0) return chalk.yellow('  Due: Today');
  if (diff === 1) return chalk.dim('  Due: Tomorrow');
  if (diff < 7) return chalk.dim(`  Due: ${dueD.toLocaleDateString('en-US', { weekday: 'long' })}`);
  return chalk.dim(`  Due: ${formatMonthDay(dueD)}`);
}

function formatMonthDay(d: Date): string {
  return d.toLocaleDateString('en-US', { month: 'short', day: 'numeric' });
}

export function formatTags(tags: string[] | null): string {
  if (!tags || tags.length === 0) return '';
  const formatted = tags.map(t => tagColor(t)(`#${t}`));
  return '  ' + formatted.join(' ');
}

// --- Result output ---

export function printResult(result: TaskResult): void {
  switch (result.type) {
    case 'success': success(result.message); break;
    case 'not-found': error(`Could not find task with id ${result.taskId}`); break;
    case 'no-change': info(result.message); break;
    case 'error': error(result.message); break;
  }
}

export function printBatchResults(batch: BatchResult): void {
  for (const result of batch.results) {
    printResult(result);
  }
}

// --- Basic output ---

export function success(message: string): void {
  console.log(chalk.green(message));
}

export function error(message: string): void {
  console.log(chalk.red(message));
}

export function warning(message: string): void {
  console.log(chalk.yellow(message));
}

export function info(message: string): void {
  console.log(message);
}

// --- Utilities ---

export function truncate(s: string, maxLen: number): string {
  if (s.length <= maxLen) return s;
  return s.slice(0, maxLen - 1) + '…';
}

export function getTimeAgo(timestamp: string | Date): string {
  const time = typeof timestamp === 'string' ? new Date(timestamp) : timestamp;
  const diff = Date.now() - time.getTime();
  const mins = diff / 60000;
  if (mins < 1) return 'just now';
  if (mins < 60) return `${Math.floor(mins)}m ago`;
  const hours = mins / 60;
  if (hours < 24) return `${Math.floor(hours)}h ago`;
  const days = hours / 24;
  if (days < 7) return `${Math.floor(days)}d ago`;
  return formatMonthDay(time);
}
