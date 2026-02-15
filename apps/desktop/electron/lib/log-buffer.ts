export interface LogEntry {
  timestamp: number;
  level: 'log' | 'warn' | 'error';
  message: string;
}

const MAX_ENTRIES = 200;
const buffer: LogEntry[] = [];
const listeners: Array<(entry: LogEntry) => void> = [];

function push(level: LogEntry['level'], args: unknown[]) {
  const message = args.map((a) => (typeof a === 'string' ? a : JSON.stringify(a))).join(' ');
  const entry: LogEntry = { timestamp: Date.now(), level, message };
  buffer.push(entry);
  if (buffer.length > MAX_ENTRIES) buffer.shift();
  for (const cb of listeners) cb(entry);
}

export function initLogCapture() {
  const origLog = console.log.bind(console);
  const origWarn = console.warn.bind(console);
  const origError = console.error.bind(console);

  console.log = (...args: unknown[]) => {
    origLog(...args);
    push('log', args);
  };
  console.warn = (...args: unknown[]) => {
    origWarn(...args);
    push('warn', args);
  };
  console.error = (...args: unknown[]) => {
    origError(...args);
    push('error', args);
  };
}

export function getLogHistory(): LogEntry[] {
  return [...buffer];
}

export function clearLogs() {
  buffer.length = 0;
}

export function onLog(callback: (entry: LogEntry) => void): () => void {
  listeners.push(callback);
  return () => {
    const idx = listeners.indexOf(callback);
    if (idx >= 0) listeners.splice(idx, 1);
  };
}
