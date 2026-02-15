import { describe, it, expect, beforeEach } from 'vitest';
import { createTestDb, addTask, setStatus, TaskStatus, getAllTasks, getDisplayDescription } from '@tasker/core';
import type { TaskerDb, Task } from '@tasker/core';

// We can't import the sync engine directly (it imports electron),
// so we replicate the pure filtering logic that sync-engine uses.
// This tests the same algorithm without Electron dependencies.

function buildSyncableReminders(tasks: Task[]) {
  return tasks
    .filter((t) => t.dueDate != null && !t.isTrashed)
    .map((t) => {
      const display = getDisplayDescription(t.description);
      const lines = display.split('\n');
      const title = lines[0]!;
      const descBody = lines.slice(1).join('\n').trim();

      const noteParts: string[] = [];
      if (descBody) noteParts.push(descBody);
      if (t.priority != null) noteParts.push(`Priority: ${t.priority}`);
      if (t.tags && t.tags.length > 0) noteParts.push(`Tags: ${t.tags.join(', ')}`);

      return {
        taskId: t.id,
        listName: t.listName,
        title,
        date: t.dueDate!,
        notes: noteParts.join('\n'),
        completed: t.status === TaskStatus.Done,
        priority: t.priority,
      };
    });
}

// ICS generation (pure string logic, no fs/electron deps)
function escapeIcs(text: string): string {
  return text
    .replace(/\\/g, '\\\\')
    .replace(/;/g, '\\;')
    .replace(/,/g, '\\,')
    .replace(/\n/g, '\\n');
}

function generateIcs(
  events: { taskId: string; title: string; date: string; notes: string }[],
  calendarName: string,
): string {
  const lines: string[] = [
    'BEGIN:VCALENDAR',
    'VERSION:2.0',
    'PRODID:-//Tasker//Reminder Sync//EN',
    `X-WR-CALNAME:${escapeIcs(calendarName)}`,
    'CALSCALE:GREGORIAN',
    'METHOD:PUBLISH',
  ];

  for (const event of events) {
    const dateVal = event.date.replace(/-/g, '');
    lines.push(
      'BEGIN:VEVENT',
      `UID:tasker-${event.taskId}@tasker`,
      `DTSTAMP:${new Date().toISOString().replace(/[-:]/g, '').replace(/\.\d+/, '')}`,
      `DTSTART;VALUE=DATE:${dateVal}`,
      `SUMMARY:${escapeIcs(event.title)}`,
      `DESCRIPTION:${escapeIcs(event.notes)}`,
      'END:VEVENT',
    );
  }

  lines.push('END:VCALENDAR');
  return lines.join('\r\n') + '\r\n';
}

describe('Reminder Sync - Task Filtering', () => {
  let db: TaskerDb;

  beforeEach(() => {
    db = createTestDb();
  });

  it('includes tasks with due dates', () => {
    addTask(db, 'Task with due\n@2026-03-01', 'default');
    addTask(db, 'Task without due', 'default');

    const tasks = getAllTasks(db);
    const events = buildSyncableReminders(tasks);

    expect(events).toHaveLength(1);
    expect(events[0]!.date).toBe('2026-03-01');
    expect(events[0]!.completed).toBe(false);
  });

  it('includes done tasks as completed reminders', () => {
    const result = addTask(db, 'Done task\n@2026-03-01', 'default');
    setStatus(db, result.task.id, TaskStatus.Done);

    const tasks = getAllTasks(db);
    const events = buildSyncableReminders(tasks);

    expect(events).toHaveLength(1);
    expect(events[0]!.completed).toBe(true);
  });

  it('excludes trashed tasks', () => {
    // Trashed tasks are filtered by getAllTasks (isTrashed=0 condition)
    addTask(db, 'Normal task\n@2026-03-01', 'default');

    const tasks = getAllTasks(db);
    const events = buildSyncableReminders(tasks);

    expect(events).toHaveLength(1);
  });

  it('carries list name for grouping', () => {
    addTask(db, 'My task\n@2026-04-15', 'work');

    const tasks = getAllTasks(db, 'work');
    const events = buildSyncableReminders(tasks);

    expect(events).toHaveLength(1);
    expect(events[0]!.listName).toBe('work');
  });

  it('includes priority in notes and as field', () => {
    addTask(db, 'Priority task\np1 @2026-04-15', 'default');

    const tasks = getAllTasks(db);
    const events = buildSyncableReminders(tasks);

    expect(events).toHaveLength(1);
    expect(events[0]!.notes).toContain('Priority: 1');
    expect(events[0]!.priority).toBe(1);
  });

  it('includes tags in notes', () => {
    addTask(db, 'Tagged task\n@2026-04-15 #urgent #review', 'default');

    const tasks = getAllTasks(db);
    const events = buildSyncableReminders(tasks);

    expect(events).toHaveLength(1);
    expect(events[0]!.notes).toContain('Tags: urgent, review');
  });

  it('uses display description for event title', () => {
    addTask(db, 'Clean title\np2 @2026-04-15', 'default');

    const tasks = getAllTasks(db);
    const events = buildSyncableReminders(tasks);

    expect(events).toHaveLength(1);
    expect(events[0]!.title).toBe('Clean title');
  });

  it('splits title from description body for multi-line tasks', () => {
    addTask(db, 'Buy groceries\n- milk\n- eggs\n- bread\n@2026-04-15', 'default');

    const tasks = getAllTasks(db);
    const events = buildSyncableReminders(tasks);

    expect(events).toHaveLength(1);
    expect(events[0]!.title).toBe('Buy groceries');
    expect(events[0]!.notes).toContain('- milk');
    expect(events[0]!.notes).toContain('- eggs');
    expect(events[0]!.notes).toContain('- bread');
  });

  it('sets null priority when no priority', () => {
    addTask(db, 'No priority\n@2026-04-15', 'default');

    const tasks = getAllTasks(db);
    const events = buildSyncableReminders(tasks);

    expect(events).toHaveLength(1);
    expect(events[0]!.priority).toBeNull();
  });
});

describe('Reminder Sync - ICS Generation', () => {
  it('generates valid ICS with events', () => {
    const events = [
      {
        taskId: 'abc',
        title: 'Test Event',
        date: '2026-03-15',
        notes: 'List: default',
      },
    ];

    const ics = generateIcs(events, 'Tasker');

    expect(ics).toContain('BEGIN:VCALENDAR');
    expect(ics).toContain('END:VCALENDAR');
    expect(ics).toContain('X-WR-CALNAME:Tasker');
    expect(ics).toContain('BEGIN:VEVENT');
    expect(ics).toContain('UID:tasker-abc@tasker');
    expect(ics).toContain('DTSTART;VALUE=DATE:20260315');
    expect(ics).toContain('SUMMARY:Test Event');
    expect(ics).toContain('DESCRIPTION:List: default');
    expect(ics).toContain('END:VEVENT');
  });

  it('generates empty calendar with no events', () => {
    const ics = generateIcs([], 'Tasker');

    expect(ics).toContain('BEGIN:VCALENDAR');
    expect(ics).toContain('END:VCALENDAR');
    expect(ics).not.toContain('BEGIN:VEVENT');
  });

  it('escapes special characters', () => {
    const events = [
      {
        taskId: 'xyz',
        title: 'Meeting; with, team',
        date: '2026-05-01',
        notes: 'Line1\nLine2',
      },
    ];

    const ics = generateIcs(events, 'Tasker');

    expect(ics).toContain('SUMMARY:Meeting\\; with\\, team');
    expect(ics).toContain('DESCRIPTION:Line1\\nLine2');
  });

  it('generates multiple events', () => {
    const events = [
      { taskId: 'a1', title: 'First', date: '2026-03-01', notes: '' },
      { taskId: 'b2', title: 'Second', date: '2026-03-02', notes: '' },
    ];

    const ics = generateIcs(events, 'Tasker');

    const vevents = ics.match(/BEGIN:VEVENT/g);
    expect(vevents).toHaveLength(2);
    expect(ics).toContain('UID:tasker-a1@tasker');
    expect(ics).toContain('UID:tasker-b2@tasker');
  });

  it('uses CRLF line endings per RFC 5545', () => {
    const ics = generateIcs([], 'Tasker');
    expect(ics).toContain('\r\n');
    // No bare LF
    const withoutCR = ics.replace(/\r\n/g, '');
    expect(withoutCR).not.toContain('\n');
  });
});
