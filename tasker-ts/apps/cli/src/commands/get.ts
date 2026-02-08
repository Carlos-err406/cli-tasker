import { Command } from 'commander';
import chalk from 'chalk';
import type { TaskerDb } from '@tasker/core';
import type { Task } from '@tasker/core';
import {
  TaskStatus, PriorityName,
  getTaskById, getDisplayDescription, parseTaskDescription,
} from '@tasker/core';
import * as out from '../output.js';

export function createGetCommand(db: TaskerDb): Command {
  return new Command('get')
    .description('Get detailed information about a task')
    .argument('<taskId>', 'The task ID to retrieve')
    .option('--json', 'Output in JSON format')
    .option('-r, --recursive', 'Recursively show all related tasks in a tree')
    .action((taskId: string, opts: { json?: boolean; recursive?: boolean }) => {
      try {
        const task = getTaskById(db, taskId);
        if (!task) {
          out.error(`Task not found: ${taskId}`);
          return;
        }

        if (opts.json) {
          outputJson(db, task, opts.recursive ?? false);
        } else if (opts.recursive) {
          outputRecursiveTree(db, task);
        } else {
          outputHumanReadable(db, task);
        }
      } catch (err: unknown) {
        out.error(err instanceof Error ? err.message : String(err));
      }
    });
}

function formatStatus(s: number | undefined): string {
  switch (s) {
    case TaskStatus.Done: return 'done';
    case TaskStatus.InProgress: return 'in-progress';
    default: return 'pending';
  }
}

function outputJson(db: TaskerDb, task: Task, recursive: boolean): void {
  if (recursive) {
    const visited = new Set<string>();
    const obj = buildJsonTree(db, task, visited);
    console.log(JSON.stringify(obj, null, 2));
    return;
  }

  const parsed = parseTaskDescription(task.description);

  const mapRef = (id: string) => {
    const t = getTaskById(db, id);
    return {
      id,
      description: t ? out.truncate(t.description, 50) : '?',
      status: formatStatus(t?.status),
    };
  };

  const obj = {
    id: task.id,
    description: task.description,
    status: formatStatus(task.status),
    priority: task.priority != null ? PriorityName[task.priority]?.toLowerCase() : null,
    dueDate: task.dueDate,
    tags: task.tags,
    listName: task.listName,
    createdAt: task.createdAt,
    completedAt: task.completedAt,
    parentId: parsed.parentId ?? null,
    parentStatus: parsed.parentId ? formatStatus(getTaskById(db, parsed.parentId)?.status) : null,
    subtasks: (parsed.hasSubtaskIds ?? []).map(mapRef),
    blocks: (parsed.blocksIds ?? []).map(mapRef),
    blockedBy: (parsed.blockedByIds ?? []).map(mapRef),
    related: (parsed.relatedIds ?? []).map(mapRef),
  };
  console.log(JSON.stringify(obj, null, 2));
}

function buildJsonTree(db: TaskerDb, task: Task, visited: Set<string>): Record<string, unknown> {
  visited.add(task.id);
  const parsed = parseTaskDescription(task.description);

  const buildRef = (id: string): Record<string, unknown> => {
    if (visited.has(id)) return { id, $ref: true };
    const t = getTaskById(db, id);
    if (!t) return { id, error: 'task not found' };
    return buildJsonTree(db, t, visited);
  };

  return {
    id: task.id,
    description: task.description,
    status: formatStatus(task.status),
    priority: task.priority != null ? PriorityName[task.priority]?.toLowerCase() : null,
    dueDate: task.dueDate,
    tags: task.tags,
    listName: task.listName,
    createdAt: task.createdAt,
    completedAt: task.completedAt,
    parent: parsed.parentId ? buildRef(parsed.parentId) : null,
    subtasks: (parsed.hasSubtaskIds ?? []).map(buildRef),
    blocks: (parsed.blocksIds ?? []).map(buildRef),
    blockedBy: (parsed.blockedByIds ?? []).map(buildRef),
    related: (parsed.relatedIds ?? []).map(buildRef),
  };
}

function outputRecursiveTree(db: TaskerDb, rootTask: Task): void {
  const visited = new Set<string>();
  printTreeNode(db, rootTask, visited, 0);
}

function printTreeNode(db: TaskerDb, task: Task, visited: Set<string>, depth: number): void {
  visited.add(task.id);
  const indent = '  '.repeat(depth);
  const checkbox = task.status === TaskStatus.Done ? '[x]' : task.status === TaskStatus.InProgress ? '[-]' : '[ ]';
  const desc = getDisplayDescription(task.description);

  console.log(`${indent}${chalk.bold(`(${task.id})`)} ${checkbox} ${desc}`);

  const priority = task.priority != null ? PriorityName[task.priority] ?? '-' : '-';
  const dueDate = task.dueDate ?? '-';
  const tags = task.tags?.length ? task.tags.map(t => `#${t}`).join(' ') : '-';
  console.log(`${indent}  ${chalk.dim(`List: ${task.listName} | Priority: ${priority} | Due: ${dueDate} | Tags: ${tags}`)}`);

  const parsed = parseTaskDescription(task.description);

  const printSection = (label: string, ids: string[] | null | undefined) => {
    if (!ids?.length) return;
    console.log(`${indent}  ${chalk.bold(label)}`);
    for (const id of ids) {
      if (visited.has(id)) {
        console.log(`${indent}    ${chalk.dim(`(${id}) (see above)`)}`);
        continue;
      }
      const t = getTaskById(db, id);
      if (!t) {
        console.log(`${indent}    ${chalk.dim(`(${id}) (task not found)`)}`);
        continue;
      }
      printTreeNode(db, t, visited, depth + 2);
    }
  };

  if (parsed.parentId) {
    console.log(`${indent}  ${chalk.bold('Parent:')}`);
    if (visited.has(parsed.parentId)) {
      console.log(`${indent}    ${chalk.dim(`(${parsed.parentId}) (see above)`)}`);
    } else {
      const p = getTaskById(db, parsed.parentId);
      if (p) printTreeNode(db, p, visited, depth + 2);
      else console.log(`${indent}    ${chalk.dim(`(${parsed.parentId}) (task not found)`)}`);
    }
  }

  printSection('Subtasks:', parsed.hasSubtaskIds);
  printSection('Blocks:', parsed.blocksIds);
  printSection('Blocked by:', parsed.blockedByIds);
  printSection('Related:', parsed.relatedIds);
}

function outputHumanReadable(db: TaskerDb, task: Task): void {
  const checkbox = task.status === TaskStatus.Done ? '[x]' : task.status === TaskStatus.InProgress ? '[-]' : '[ ]';
  const priority = task.priority != null ? PriorityName[task.priority] ?? '-' : '-';
  const dueDate = task.dueDate ?? '-';
  const tags = task.tags?.length ? task.tags.map(t => `#${t}`).join(' ') : '-';

  console.log(`${chalk.bold('ID:')}          ${task.id}`);
  console.log(`${chalk.bold('List:')}        ${task.listName}`);
  console.log(`${chalk.bold('Status:')}      ${checkbox}`);
  console.log(`${chalk.bold('Priority:')}    ${priority}`);
  console.log(`${chalk.bold('Due:')}         ${dueDate}`);
  console.log(`${chalk.bold('Tags:')}        ${tags}`);
  console.log(`${chalk.bold('Created:')}     ${task.createdAt.replace('T', ' ').slice(0, 16)}`);
  if (task.completedAt) {
    console.log(`${chalk.bold('Completed:')}   ${task.completedAt.replace('T', ' ').slice(0, 16)}`);
  }

  const parsed = parseTaskDescription(task.description);

  if (parsed.parentId) {
    const parent = getTaskById(db, parsed.parentId);
    const parentDesc = parent ? out.truncate(parent.description, 40) : '?';
    const parentStatus = parent ? out.formatLinkedStatus(parent.status) : '';
    console.log(`${chalk.bold('Parent:')}      ${chalk.dim(`(${parsed.parentId}) ${parentDesc}`)}${parentStatus}`);
  }

  const printRelSection = (label: string, ids: string[] | null | undefined) => {
    if (!ids?.length) return;
    console.log(`${chalk.bold(`${label}:`)}`);
    for (const id of ids) {
      const t = getTaskById(db, id);
      const desc = t ? out.truncate(t.description, 40) : '?';
      const status = t ? out.formatLinkedStatus(t.status) : '';
      console.log(`               ${chalk.dim(`(${id}) ${desc}`)}${status}`);
    }
  };

  printRelSection('Subtasks', parsed.hasSubtaskIds);
  printRelSection('Blocks', parsed.blocksIds);
  printRelSection('Blocked by', parsed.blockedByIds);
  printRelSection('Related', parsed.relatedIds);

  console.log(`${chalk.bold('Description:')}`);
  console.log(task.description);
}
