/**
 * Manages undo/redo stacks with SQLite persistence.
 * Two-stack architecture: undo and redo, persisted to undo_history table.
 */

import type { TaskerDb } from '../db.js';
import { getRawDb } from '../db.js';
import type { UndoCommand, CompositeCmd } from './undo-commands.js';
import { getCommandDescription } from './undo-commands.js';
import { executeCommand, undoCommand } from './undo-executor.js';

const MAX_UNDO = 50;
const MAX_REDO = 50;
const RETENTION_DAYS = 30;

export class UndoManager {
  private db: TaskerDb;
  private undoStack: UndoCommand[] = [];
  private redoStack: UndoCommand[] = [];
  private currentBatch: CompositeCmd | null = null;

  constructor(db: TaskerDb) {
    this.db = db;
    this.loadHistory();
  }

  get canUndo(): boolean { return this.undoStack.length > 0; }
  get canRedo(): boolean { return this.redoStack.length > 0; }
  get undoCount(): number { return this.undoStack.length; }
  get redoCount(): number { return this.redoStack.length; }
  get undoHistory(): readonly UndoCommand[] { return this.undoStack; }
  get redoHistory(): readonly UndoCommand[] { return this.redoStack; }

  /** Record a command. If batching, adds to current batch; otherwise pushes to undo stack. */
  recordCommand(command: UndoCommand): void {
    if (this.currentBatch) {
      this.currentBatch = {
        ...this.currentBatch,
        commands: [...this.currentBatch.commands, command],
      };
    } else {
      this.undoStack.unshift(command);
      this.redoStack = [];
      this.enforceSizeLimit();
    }
  }

  /** Persist history to DB */
  saveHistory(): void {
    this.save();
  }

  /** Begin a batch (composite) command */
  beginBatch(description: string): void {
    this.currentBatch = {
      $type: 'batch',
      batchDescription: description,
      commands: [],
      executedAt: new Date().toISOString(),
    };
  }

  /** End the current batch, adding it to the undo stack */
  endBatch(): void {
    if (this.currentBatch && this.currentBatch.commands.length > 0) {
      this.undoStack.unshift(this.currentBatch);
      this.redoStack = [];
      this.enforceSizeLimit();
    }
    this.currentBatch = null;
  }

  /** Cancel the current batch without recording it */
  cancelBatch(): void {
    this.currentBatch = null;
  }

  /** Undo the most recent command. Returns the command description, or null if nothing to undo. */
  undo(): string | null {
    if (this.undoStack.length === 0) return null;

    const cmd = this.undoStack.shift()!;
    undoCommand(this.db, cmd);
    this.redoStack.unshift(cmd);
    this.save();

    return getCommandDescription(cmd);
  }

  /** Redo the most recently undone command. Returns the command description, or null if nothing to redo. */
  redo(): string | null {
    if (this.redoStack.length === 0) return null;

    const cmd = this.redoStack.shift()!;
    executeCommand(this.db, cmd);
    this.undoStack.unshift(cmd);
    this.save();

    return getCommandDescription(cmd);
  }

  /** Clear all undo/redo history */
  clearHistory(): void {
    this.undoStack = [];
    this.redoStack = [];
    this.save();
  }

  /** Reload history from database (for syncing with external changes) */
  reloadHistory(): void {
    this.undoStack = [];
    this.redoStack = [];
    this.loadHistory();
  }

  private enforceSizeLimit(): void {
    if (this.undoStack.length > MAX_UNDO) {
      this.undoStack.length = MAX_UNDO;
    }
    if (this.redoStack.length > MAX_REDO) {
      this.redoStack.length = MAX_REDO;
    }
  }

  private loadHistory(): void {
    try {
      const raw = getRawDb(this.db);
      const cutoff = new Date();
      cutoff.setDate(cutoff.getDate() - RETENTION_DAYS);
      const cutoffStr = cutoff.toISOString();

      const undoRows = raw.prepare(
        "SELECT command_json FROM undo_history WHERE stack_type = 'undo' AND created_at > ? ORDER BY id ASC",
      ).all(cutoffStr) as any[];

      const redoRows = raw.prepare(
        "SELECT command_json FROM undo_history WHERE stack_type = 'redo' AND created_at > ? ORDER BY id ASC",
      ).all(cutoffStr) as any[];

      this.undoStack = undoRows.map(r => JSON.parse(r.command_json) as UndoCommand);
      this.redoStack = redoRows.map(r => JSON.parse(r.command_json) as UndoCommand);
    } catch {
      // Corrupted history — start fresh
      this.undoStack = [];
      this.redoStack = [];
    }
  }

  private save(): void {
    const raw = getRawDb(this.db);

    const run = raw.transaction(() => {
      raw.prepare('DELETE FROM undo_history').run();

      const insert = raw.prepare(
        "INSERT INTO undo_history (stack_type, command_json, created_at) VALUES (?, ?, ?)",
      );

      for (const cmd of this.undoStack) {
        insert.run('undo', JSON.stringify(cmd), cmd.executedAt);
      }

      for (const cmd of this.redoStack) {
        insert.run('redo', JSON.stringify(cmd), cmd.executedAt);
      }
    });
    run();
  }
}
