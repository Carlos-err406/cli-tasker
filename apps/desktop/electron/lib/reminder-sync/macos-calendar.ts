import type { SyncableReminder } from './types.js';

// eventkit-node is an optional native dependency (macOS only)
let eventkit: typeof import('eventkit-node') | null = null;

async function getEventKit(): Promise<typeof import('eventkit-node')> {
  if (!eventkit) {
    eventkit = await import('eventkit-node');
  }
  return eventkit;
}

const TASKER_TAG_RE = /\[tasker:([^\]]+)\]/;

function buildNotes(event: SyncableReminder): string {
  return `[tasker:${event.taskId}]\n${event.notes}`;
}

function extractTaskId(notes: string | null): string | null {
  if (!notes) return null;
  const match = TASKER_TAG_RE.exec(notes);
  return match ? match[1]! : null;
}

function parseDate(dateStr: string): Date {
  return new Date(`${dateStr}T00:00:00`);
}

// Tasker priority (1=High,2=Med,3=Low) → EventKit priority (1=High,5=Med,9=Low,0=none)
function toReminderPriority(priority: number | null): number {
  if (priority === 1) return 1;
  if (priority === 2) return 5;
  if (priority === 3) return 9;
  return 0;
}

export async function requestAccess(): Promise<boolean> {
  const ek = await getEventKit();
  return ek.requestFullAccessToReminders();
}

export async function ensureListExists(name: string): Promise<string> {
  const ek = await getEventKit();
  const calendars = ek.getCalendars('reminder');
  const existing = calendars.find(
    (c) => c.title === name,
  );
  if (existing) return existing.id;

  // Rank sources by likelihood of supporting reminder list creation
  const sources = ek.getSources();
  const ranked = [
    ...sources.filter((s) => s.sourceType === 'calDAV'),
    ...sources.filter((s) => s.sourceType === 'local'),
    ...sources.filter((s) => s.sourceType !== 'calDAV' && s.sourceType !== 'local'),
  ];

  const errors: string[] = [];
  for (const source of ranked) {
    try {
      return await ek.saveCalendar({
        title: name,
        entityType: 'reminder',
        sourceId: source.id,
        color: { hex: '#4A90D9FF' },
      });
    } catch (err) {
      errors.push(`${source.title} (${source.sourceType}): ${err instanceof Error ? err.message : String(err)}`);
    }
  }

  throw new Error(`Could not create reminder list in any source. Tried:\n${errors.join('\n')}`);
}

export interface ExistingReminder {
  reminderId: string;
  taskId: string;
  completed: boolean;
}

export async function getExistingReminders(
  listId: string,
): Promise<Map<string, ExistingReminder>> {
  const ek = await getEventKit();
  const predicate = ek.createReminderPredicate([listId]);
  const reminders = await ek.getRemindersWithPredicate(predicate);

  // Map taskId -> reminder info
  const map = new Map<string, ExistingReminder>();
  for (const r of reminders) {
    const taskId = extractTaskId(r.notes);
    if (taskId) {
      map.set(taskId, {
        reminderId: r.id,
        taskId,
        completed: r.completed,
      });
    }
  }
  return map;
}

export async function upsertReminder(
  listId: string,
  event: SyncableReminder,
  existingReminderId?: string,
): Promise<void> {
  const ek = await getEventKit();
  const notes = buildNotes(event);
  const dueDate = parseDate(event.date);
  const priority = toReminderPriority(event.priority);

  if (existingReminderId) {
    await ek.saveReminder({
      id: existingReminderId,
      title: event.title,
      dueDate,
      notes,
      calendarId: listId,
      completed: event.completed,
      priority,
    });
  } else {
    await ek.saveReminder({
      title: event.title,
      dueDate,
      notes,
      calendarId: listId,
      completed: event.completed,
      priority,
    });
  }
}

export async function deleteStaleReminders(
  listId: string,
  activeTaskIds: Set<string>,
): Promise<number> {
  const existing = await getExistingReminders(listId);
  const ek = await getEventKit();
  let removed = 0;

  for (const [taskId, info] of existing) {
    if (!activeTaskIds.has(taskId)) {
      await ek.removeReminder(info.reminderId);
      removed++;
    }
  }
  return removed;
}

/**
 * Remove Tasker-managed reminder lists that no longer have active tasks.
 * Only removes lists whose name starts with the prefix.
 */
export async function deleteEmptyTaskerLists(
  prefix: string,
  activeListNames: Set<string>,
): Promise<void> {
  const ek = await getEventKit();
  const calendars = ek.getCalendars('reminder');
  const taskerPrefix = `${prefix}: `;

  for (const cal of calendars) {
    if (cal.title.startsWith(taskerPrefix) && !activeListNames.has(cal.title)) {
      // Check if list is empty before removing
      const reminders = await getExistingReminders(cal.id);
      if (reminders.size === 0) {
        await ek.removeCalendar(cal.id);
      }
    }
  }
}
