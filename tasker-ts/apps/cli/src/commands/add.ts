import { Command } from 'commander';
import type { TaskerDb } from '@tasker/core';
import { addTask } from '@tasker/core';
import * as out from '../output.js';
import { resolveListForAdd } from '../helpers.js';

export function createAddCommand(db: TaskerDb): Command {
  return new Command('add')
    .description('Add a new task')
    .argument('<description>', 'Task description (supports: p1/p2/p3, @date, #tag)')
    .action((description: string, _opts: unknown, cmd: Command) => {
      try {
        const g = cmd.optsWithGlobals() as { list?: string; all?: boolean };
        const listName = resolveListForAdd(db, g.list, g.all ?? false);

        const { task, warnings } = addTask(db, description, listName);
        for (const w of warnings) out.warning(w);
        out.success(`Task saved to '${task.listName}'. Use the list command to see your tasks`);
      } catch (err: unknown) {
        out.error(err instanceof Error ? err.message : String(err));
      }
    });
}
