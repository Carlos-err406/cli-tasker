import fs from 'node:fs';
import path from 'node:path';
import { app } from 'electron';
import type { SyncableReminder } from './types.js';

function escapeIcs(text: string): string {
  return text
    .replace(/\\/g, '\\\\')
    .replace(/;/g, '\\;')
    .replace(/,/g, '\\,')
    .replace(/\n/g, '\\n');
}

function formatDateValue(date: string): string {
  // date is yyyy-MM-dd, convert to yyyyMMdd
  return date.replace(/-/g, '');
}

function generateTimestamp(): string {
  return new Date().toISOString().replace(/[-:]/g, '').replace(/\.\d+/, '');
}

export function generateIcs(events: SyncableReminder[], calendarName: string): string {
  const timestamp = generateTimestamp();
  const lines: string[] = [
    'BEGIN:VCALENDAR',
    'VERSION:2.0',
    'PRODID:-//Tasker//Reminder Sync//EN',
    `X-WR-CALNAME:${escapeIcs(calendarName)}`,
    'CALSCALE:GREGORIAN',
    'METHOD:PUBLISH',
  ];

  for (const event of events) {
    const dateVal = formatDateValue(event.date);
    lines.push(
      'BEGIN:VEVENT',
      `UID:tasker-${event.taskId}@tasker`,
      `DTSTAMP:${timestamp}`,
      `DTSTART;VALUE=DATE:${dateVal}`,
      `SUMMARY:${escapeIcs(event.title)}`,
      `DESCRIPTION:${escapeIcs(event.notes)}`,
      'END:VEVENT',
    );
  }

  lines.push('END:VCALENDAR');
  return lines.join('\r\n') + '\r\n';
}

export function writeIcsFile(content: string): string {
  const dir = app.getPath('userData');
  if (!fs.existsSync(dir)) {
    fs.mkdirSync(dir, { recursive: true });
  }
  const filePath = path.join(dir, 'tasker-calendar.ics');
  fs.writeFileSync(filePath, content, 'utf8');
  return filePath;
}
