import { Command } from 'commander';
import chalk from 'chalk';
import type { TaskerDb } from '@tasker/core';
import type { Task } from '@tasker/core';
import {
  TaskStatus, Priority, getAllListNames, getSortedTasks,
  getTaskById, getDisplayDescription, parseTaskDescription,
} from '@tasker/core';
import * as out from '../output.js';
import { resolveListFilter, parsePriorityArg, $try } from '../helpers.js';

export function createListCommand(db: TaskerDb): Command {
  return new Command('list')
    .description('List all tasks')
    .option('-c, --checked', 'Show only checked tasks')
    .option('-u, --unchecked', 'Show only unchecked tasks')
    .option('-p, --priority <level>', 'Filter by priority (high, medium, low)')
    .option('--overdue', 'Show only overdue tasks')
    .action((_opts: unknown, cmd: Command) => $try(() => {
      const g = cmd.optsWithGlobals() as {
        list?: string; all?: boolean;
        checked?: boolean; unchecked?: boolean;
        priority?: string; overdue?: boolean;
      };

      if (g.checked && g.unchecked) {
        out.error('Cannot use both --checked and --unchecked at the same time');
        return;
      }

      const listName = resolveListFilter(db, g.list, g.all ?? false);
      const filterStatus = g.checked ? TaskStatus.Done : g.unchecked ? TaskStatus.Pending : null;
      const filterPriority = g.priority ? parsePriorityArg(g.priority) as Priority | null : null;

      // Show auto-detection indicator
      if (!g.list && !g.all && listName != null) {
        out.info(chalk.dim(`(auto: ${listName})`));
      }

      if (listName == null) {
        const listNames = getAllListNames(db);
        for (const name of listNames) {
          const opts: Parameters<typeof getSortedTasks>[1] = { listName: name };
          if (filterStatus != null) opts!.status = filterStatus;
          if (filterPriority != null) opts!.priority = filterPriority;
          if (g.overdue) opts!.overdue = true;
          const tasks = getSortedTasks(db, opts);
          console.log(chalk.bold.underline(name));
          displayTasks(db, tasks, g.checked ?? null);
          console.log();
        }
      } else {
        const opts: Parameters<typeof getSortedTasks>[1] = { listName };
        if (filterStatus != null) opts!.status = filterStatus;
        if (filterPriority != null) opts!.priority = filterPriority;
        if (g.overdue) opts!.overdue = true;
        const tasks = getSortedTasks(db, opts);
        displayTasks(db, tasks, g.checked ?? null);
      }
    }));
}

function displayTasks(db: TaskerDb, tasks: Task[], filterChecked: boolean | null): void {
  if (tasks.length === 0) {
    const message = filterChecked === true
      ? 'No checked tasks found'
      : filterChecked === false
        ? 'No unchecked tasks found'
        : 'No tasks saved yet... use the add command to create one';
    out.info(message);
    return;
  }

  for (const td of tasks) {
    const indent = '          '; // id(5) + space + priority(3) + space
    const displayDesc = getDisplayDescription(td.description);
    const lines = displayDesc.split('\n').filter(l => l.trim().length > 0);
    const firstLine = chalk.bold(lines[0]);
    const restLines = lines.slice(1).map(l => `\n${indent}${chalk.dim(l)}`).join('');

    const checkbox = out.formatCheckbox(td.status);
    const taskId = chalk.dim(`(${td.id})`);
    const priority = out.formatPriority(td.priority);
    const dueDate = out.formatDueDate(td.dueDate, td.status, td.completedAt);
    const tags = out.formatTags(td.tags);

    console.log(`${taskId} ${priority} ${checkbox} ${firstLine}${dueDate}${tags}${restLines}`);

    // Relationship indicators
    const parsed = parseTaskDescription(td.description);

    if (parsed.parentId) {
      const parent = getTaskById(db, parsed.parentId);
      const parentTitle = parent
        ? out.truncate(getDisplayDescription(parent.description).split('\n')[0]!, 40)
        : '?';
      const parentStatus = parent ? out.formatLinkedStatus(parent.status) : '';
      console.log(`${indent}${chalk.dim(`\u2191 Subtask of (${parsed.parentId}) ${parentTitle}`)}${parentStatus}`);
    }

    if (parsed.hasSubtaskIds?.length) {
      for (const subId of parsed.hasSubtaskIds) {
        const sub = getTaskById(db, subId);
        const subTitle = sub
          ? out.truncate(getDisplayDescription(sub.description).split('\n')[0]!, 40)
          : '?';
        const subStatus = sub ? out.formatLinkedStatus(sub.status) : '';
        console.log(`${indent}${chalk.dim(`\u21B3 Subtask (${subId}) ${subTitle}`)}${subStatus}`);
      }
    }

    if (parsed.blocksIds?.length) {
      for (const bId of parsed.blocksIds) {
        const b = getTaskById(db, bId);
        const bTitle = b
          ? out.truncate(getDisplayDescription(b.description).split('\n')[0]!, 40)
          : '?';
        const bStatus = b ? out.formatLinkedStatus(b.status) : '';
        console.log(`${indent}${chalk.yellow.dim(`\u2298 Blocks (${bId}) ${bTitle}`)}${bStatus}`);
      }
    }

    if (parsed.blockedByIds?.length) {
      for (const bbId of parsed.blockedByIds) {
        const bb = getTaskById(db, bbId);
        const bbTitle = bb
          ? out.truncate(getDisplayDescription(bb.description).split('\n')[0]!, 40)
          : '?';
        const bbStatus = bb ? out.formatLinkedStatus(bb.status) : '';
        console.log(`${indent}${chalk.yellow.dim(`\u2298 Blocked by (${bbId}) ${bbTitle}`)}${bbStatus}`);
      }
    }

    if (parsed.relatedIds?.length) {
      for (const rId of parsed.relatedIds) {
        const r = getTaskById(db, rId);
        const rTitle = r
          ? out.truncate(getDisplayDescription(r.description).split('\n')[0]!, 40)
          : '?';
        const rStatus = r ? out.formatLinkedStatus(r.status) : '';
        console.log(`${indent}${chalk.cyan.dim(`~ Related to (${rId}) ${rTitle}`)}${rStatus}`);
      }
    }
  }
}
