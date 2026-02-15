import { Notification } from 'electron';
import { getConfig, setConfig, searchTasks, TaskStatus } from '@tasker/core';
import type { TaskerDb, Task } from '@tasker/core';

const log: typeof console.log = (...args) =>
  console.log('[DUE-DATE-NOTIFIER]:', ...args);

// --- Settings ---

interface DueDateNotifierSettings {
  enabled: boolean;
}

const DEFAULT_SETTINGS: DueDateNotifierSettings = { enabled: true };
const CONFIG_KEY = 'desktop.due_date_notifier';

export function getSettings(db: TaskerDb): DueDateNotifierSettings {
  try {
    const raw = getConfig(db, CONFIG_KEY);
    if (raw) {
      const data = JSON.parse(raw);
      return {
        enabled: typeof data.enabled === 'boolean' ? data.enabled : DEFAULT_SETTINGS.enabled,
      };
    }
  } catch {
    // ignore
  }
  return { ...DEFAULT_SETTINGS };
}

function saveSettings(db: TaskerDb, settings: DueDateNotifierSettings): void {
  setConfig(db, CONFIG_KEY, JSON.stringify(settings));
}

// --- Notifier ---

const POLL_INTERVAL_MS = 15 * 60_000; // 15 minutes
const MAX_NAMES_PER_CATEGORY = 3;

let pollTimer: ReturnType<typeof setInterval> | null = null;
let midnightTimer: ReturnType<typeof setTimeout> | null = null;
let notifiedIds = new Set<string>();
let dbRef: TaskerDb | null = null;
let onClickCallback: ((searchQuery: string) => void) | null = null;

// Keep references to active notifications to prevent garbage collection.
// Without this, macOS will GC the handler and click events stop firing.
// See: https://blog.bloomca.me/2025/02/22/electron-mac-notifications
const activeNotifications: Notification[] = [];

export interface DueDateNotifierOptions {
  onNotificationClick: (searchQuery: string) => void;
}

export function startDueDateNotifier(db: TaskerDb, opts: DueDateNotifierOptions): void {
  dbRef = db;
  onClickCallback = opts.onNotificationClick;
  const settings = getSettings(db);
  log(`initialized (enabled: ${settings.enabled})`);
  if (settings.enabled) {
    startPolling(db);
  }
}

export function stopDueDateNotifier(): void {
  stopPolling();
  clearMidnightTimer();
  dbRef = null;
  onClickCallback = null;
  log('stopped');
}

export function setEnabled(db: TaskerDb, enabled: boolean): void {
  saveSettings(db, { enabled });
  log(`enabled: ${enabled}`);
  if (enabled) {
    dbRef = db;
    startPolling(db);
  } else {
    stopPolling();
  }
}

function startPolling(db: TaskerDb): void {
  stopPolling();
  log('polling started (interval: 15min)');
  // Run an initial check, then poll
  checkDueDates(db);
  pollTimer = setInterval(() => checkDueDates(db), POLL_INTERVAL_MS);
  scheduleMidnightReset();
}

function stopPolling(): void {
  if (pollTimer) {
    clearInterval(pollTimer);
    pollTimer = null;
  }
}

function clearMidnightTimer(): void {
  if (midnightTimer) {
    clearTimeout(midnightTimer);
    midnightTimer = null;
  }
}

function scheduleMidnightReset(): void {
  clearMidnightTimer();
  const now = new Date();
  const midnight = new Date(now);
  midnight.setDate(midnight.getDate() + 1);
  midnight.setHours(0, 0, 0, 0);
  const msUntilMidnight = midnight.getTime() - now.getTime();

  midnightTimer = setTimeout(() => {
    log('midnight reset — clearing notified IDs');
    notifiedIds.clear();
    // Re-check immediately after reset
    if (dbRef) checkDueDates(dbRef);
    // Schedule next midnight
    scheduleMidnightReset();
  }, msUntilMidnight);
}

function checkDueDates(db: TaskerDb): void {
  try {
    const dueToday = searchTasks(db, 'due:today')
      .filter((t) => t.status !== TaskStatus.Done && !notifiedIds.has(t.id));
    const overdue = searchTasks(db, 'due:overdue')
      .filter((t) => t.status !== TaskStatus.Done && !notifiedIds.has(t.id));

    log(`check: ${dueToday.length} new due today, ${overdue.length} new overdue`);

    if (dueToday.length === 0 && overdue.length === 0) return;

    // Track notified IDs
    for (const t of dueToday) notifiedIds.add(t.id);
    for (const t of overdue) notifiedIds.add(t.id);

    const body = buildNotificationBody(dueToday, overdue);
    // Determine which search filter to apply on click
    const searchQuery = overdue.length > 0 && dueToday.length === 0
      ? 'due:overdue'
      : 'due:today';

    log(`showing notification: "${body}"`);

    const notification = new Notification({
      title: 'Tasker',
      body,
      silent: false,
    });

    // Store reference to prevent GC from killing the click handler
    activeNotifications.push(notification);

    const removeRef = () => {
      const idx = activeNotifications.indexOf(notification);
      if (idx !== -1) activeNotifications.splice(idx, 1);
    };

    notification.on('click', () => {
      log(`notification clicked, opening popup with search: ${searchQuery}`);
      notification.close();
      onClickCallback?.(searchQuery);
      removeRef();
    });
    notification.on('close', removeRef);
    notification.show();
  } catch (err) {
    log('check error:', err);
  }
}

function formatTaskNames(tasks: Task[]): string {
  const names = tasks.map((t) => t.description.split('\n')[0]);
  if (names.length <= MAX_NAMES_PER_CATEGORY) {
    return names.join(', ');
  }
  const shown = names.slice(0, MAX_NAMES_PER_CATEGORY);
  return `${shown.join(', ')}, +${names.length - MAX_NAMES_PER_CATEGORY} more`;
}

function buildNotificationBody(dueToday: Task[], overdue: Task[]): string {
  const parts: string[] = [];

  if (dueToday.length === 1 && overdue.length === 0) {
    return `${dueToday[0].description.split('\n')[0]} is due today`;
  }
  if (overdue.length === 1 && dueToday.length === 0) {
    return `${overdue[0].description.split('\n')[0]} is overdue`;
  }

  if (dueToday.length > 0) {
    if (dueToday.length === 1) {
      parts.push(`Due today: ${dueToday[0].description.split('\n')[0]}`);
    } else {
      parts.push(`${dueToday.length} due today: ${formatTaskNames(dueToday)}`);
    }
  }

  if (overdue.length > 0) {
    if (overdue.length === 1) {
      parts.push(`Overdue: ${overdue[0].description.split('\n')[0]}`);
    } else {
      parts.push(`${overdue.length} overdue: ${formatTaskNames(overdue)}`);
    }
  }

  return parts.join(' · ');
}
