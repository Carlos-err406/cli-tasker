import { Command } from 'commander';
import chalk from 'chalk';
import type { BackupManager, UndoManager } from '@tasker/core';
import * as out from '../output.js';

export function createBackupCommand(backup: BackupManager, undo: UndoManager): Command {
  const backupCommand = new Command('backup')
    .description('Manage task backups');

  backupCommand.addCommand(
    new Command('list')
      .description('List available backups')
      .action(() => {
        try {
          const backups = backup.listBackups();
          if (backups.length === 0) {
            out.info('No backups available.');
            return;
          }

          console.log(`${chalk.bold('Available backups:')}\n`);
          for (let i = 0; i < backups.length; i++) {
            const b = backups[i]!;
            const age = out.getTimeAgo(b.timestamp);
            const ts = formatTimestamp(b.timestamp);
            const type = b.isDaily ? ` ${chalk.dim('(daily)')}` : '';
            console.log(`  ${String(i + 1).padStart(2)}. ${age.padEnd(14)} (${ts})${type}`);
          }
        } catch (err: unknown) {
          out.error(err instanceof Error ? err.message : String(err));
        }
      }),
  );

  backupCommand.addCommand(
    new Command('restore')
      .description('Restore from a backup')
      .argument('[index]', 'Backup number from "backup list" (1 = most recent)', '1')
      .option('--force', 'Skip confirmation prompt')
      .action((indexStr: string, opts: { force?: boolean }) => {
        try {
          const index = parseInt(indexStr, 10) || 1;
          const backups = backup.listBackups();

          if (backups.length === 0) {
            out.error('No backups available. Backups are created automatically when you modify tasks.');
            return;
          }

          if (index < 1 || index > backups.length) {
            out.error(`Backup #${index} not found. Use 'tasker backup list' to see available backups (1-${backups.length}).`);
            return;
          }

          const chosen = backups[index - 1]!;

          if (!opts.force) {
            out.warning(`This will restore from backup dated ${formatTimestamp(chosen.timestamp)}`);
            out.info('Current tasks will be backed up before restore.');
            out.info('Use --force to skip this confirmation.');
            return;
          }

          backup.restoreBackup(chosen.timestamp, undo);
          out.success(`Restored from backup dated ${formatTimestamp(chosen.timestamp)}`);
        } catch (err: unknown) {
          out.error(err instanceof Error ? err.message : String(err));
        }
      }),
  );

  return backupCommand;
}

function formatTimestamp(d: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}`;
}
