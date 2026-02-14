export interface ReminderSyncSettings {
  enabled: boolean;
  listPrefix: string;
}

export const DEFAULT_SETTINGS: ReminderSyncSettings = {
  enabled: false,
  listPrefix: 'Tasker',
};

export interface SyncableReminder {
  taskId: string;
  listName: string;
  title: string;
  date: string; // yyyy-MM-dd
  notes: string;
  completed: boolean;
  priority: number | null;
}

export interface SyncStatus {
  lastSyncAt: string | null;
  lastError: string | null;
  eventCount: number;
}
