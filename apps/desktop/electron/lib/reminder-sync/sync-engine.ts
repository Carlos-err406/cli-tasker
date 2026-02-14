import { getAllTasks, getDisplayDescription, getTaskById, setStatus, TaskStatus } from '@tasker/core';
import type { TaskerDb, Task } from '@tasker/core';
import type { ReminderSyncSettings, SyncableReminder, SyncStatus } from './types.js';
import { generateIcs, writeIcsFile } from './ics-generator.js';

const log: typeof console.log = (...args) =>
  console.log('[REMINDER-SYNC]:', ...args);

function buildSyncableReminders(tasks: Task[]): SyncableReminder[] {
  return tasks
    .filter((t) => t.dueDate != null && !t.isTrashed)
    .map((t) => ({
      taskId: t.id,
      listName: t.listName,
      title: getDisplayDescription(t.description),
      date: t.dueDate!,
      notes: buildNotes(t),
      completed: t.status === TaskStatus.Done,
      priority: t.priority,
    }));
}

function buildNotes(t: Task): string {
  const parts: string[] = [];
  if (t.priority != null) parts.push(`Priority: ${t.priority}`);
  if (t.tags && t.tags.length > 0) parts.push(`Tags: ${t.tags.join(', ')}`);
  return parts.join('\n');
}

function groupByList(events: SyncableReminder[]): Map<string, SyncableReminder[]> {
  const map = new Map<string, SyncableReminder[]>();
  for (const e of events) {
    const group = map.get(e.listName) ?? [];
    group.push(e);
    map.set(e.listName, group);
  }
  return map;
}

function reminderListName(prefix: string, listName: string): string {
  return `${prefix}: ${listName}`;
}

async function syncMacOS(
  db: TaskerDb,
  prefix: string,
): Promise<void> {
  log('macOS sync: importing eventkit-node...');
  const mac = await import('./macos-calendar.js');

  log('macOS sync: requesting reminder access...');
  const hasAccess = await mac.requestAccess();
  log('macOS sync: access granted:', hasAccess);
  if (!hasAccess) {
    throw new Error('Reminders access denied. Grant permission in System Settings > Privacy & Security > Reminders.');
  }

  const tasks = getAllTasks(db);
  const events = buildSyncableReminders(tasks);
  const byList = groupByList(events);

  // Collect all list names we've synced to (for stale list cleanup)
  const activeListNames = new Set<string>();

  for (const [listName, listEvents] of byList) {
    const remListName = reminderListName(prefix, listName);
    activeListNames.add(remListName);

    log(`macOS sync: ensuring reminder list "${remListName}" exists...`);
    const listId = await mac.ensureListExists(remListName);

    // Get existing reminders to detect completions from Reminders.app
    const existing = await mac.getExistingReminders(listId);
    log(`macOS sync: "${remListName}" — ${existing.size} existing, ${listEvents.length} to sync`);

    // Reverse sync: completed in Reminders.app → mark done in DB
    for (const [taskId, info] of existing) {
      if (info.completed) {
        const task = getTaskById(db, taskId);
        if (task && task.status !== TaskStatus.Done) {
          log(`macOS sync: task ${taskId} completed in Reminders.app, marking done`);
          setStatus(db, taskId, TaskStatus.Done);
        }
      }
    }

    // Re-read this list's events (statuses may have changed from reverse sync)
    const freshTasks = getAllTasks(db, listName);
    const freshEvents = buildSyncableReminders(freshTasks);
    const freshActiveIds = new Set(freshEvents.map((e) => e.taskId));

    for (const event of freshEvents) {
      const existingInfo = existing.get(event.taskId);
      await mac.upsertReminder(listId, event, existingInfo?.reminderId);
    }

    // Delete stale reminders in this list
    const removed = await mac.deleteStaleReminders(listId, freshActiveIds);
    if (removed > 0) log(`macOS sync: "${remListName}" — removed ${removed} stale reminders`);
  }

  // Clean up Tasker-managed lists that no longer have any tasks with due dates
  await mac.deleteEmptyTaskerLists(prefix, activeListNames);
}

function syncIcs(events: SyncableReminder[], prefix: string): void {
  const content = generateIcs(events, prefix);
  const filePath = writeIcsFile(content);
  log(`ICS sync: wrote ${events.length} events to ${filePath}`);
}

export async function performSync(
  db: TaskerDb,
  settings: ReminderSyncSettings,
): Promise<SyncStatus> {
  log(`starting sync (prefix: "${settings.listPrefix}")`);
  const tasks = getAllTasks(db);
  const events = buildSyncableReminders(tasks);
  log(`found ${events.length} tasks with due dates across ${groupByList(events).size} lists`);

  try {
    if (process.platform === 'darwin') {
      await syncMacOS(db, settings.listPrefix);
    } else {
      syncIcs(events, settings.listPrefix);
    }

    // Re-count after potential reverse sync
    const finalTasks = getAllTasks(db);
    const finalEvents = buildSyncableReminders(finalTasks);
    log(`sync complete: ${finalEvents.length} reminders synced`);
    return {
      lastSyncAt: new Date().toISOString(),
      lastError: null,
      eventCount: finalEvents.length,
    };
  } catch (err) {
    const message = err instanceof Error ? err.message : String(err);
    log('sync failed:', message);
    return {
      lastSyncAt: new Date().toISOString(),
      lastError: message,
      eventCount: 0,
    };
  }
}

// Exported for testing
export { buildSyncableReminders };
