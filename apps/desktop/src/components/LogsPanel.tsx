import { useState, useEffect, useRef } from 'react';
import type { LogEntry } from '../../electron/lib/log-buffer.js';

const ipc = window.ipc;

interface LogsPanelProps {
  onClose: () => void;
}

function formatTime(ts: number): string {
  const d = new Date(ts);
  return d.toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit', second: '2-digit' });
}

const levelColors: Record<LogEntry['level'], string> = {
  log: 'text-muted-foreground',
  warn: 'text-yellow-400',
  error: 'text-red-400',
};

export function LogsPanel({ onClose }: LogsPanelProps) {
  const [logs, setLogs] = useState<LogEntry[]>([]);
  const bottomRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    ipc['logs:getHistory']().then(setLogs);
  }, []);

  useEffect(() => {
    const unsub = ipc.onLogEntry((entry) => {
      setLogs((prev) => {
        const next = [...prev, entry];
        if (next.length > 200) next.shift();
        return next;
      });
    });
    return unsub;
  }, []);

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, [logs]);

  const handleClear = () => {
    ipc['logs:clear']();
    setLogs([]);
  };

  return (
    <div className="absolute inset-0 z-40 bg-background/95 backdrop-blur-sm overflow-auto p-4">
      <div className="flex items-center justify-between mb-3">
        <h2 className="text-sm font-semibold">Logs</h2>
        <div className="flex gap-3">
          <button
            onClick={handleClear}
            className="text-muted-foreground hover:text-foreground text-sm"
          >
            Clear
          </button>
          <button
            onClick={onClose}
            className="text-muted-foreground hover:text-foreground text-sm"
          >
            Close
          </button>
        </div>
      </div>

      <div className="font-mono text-[11px] leading-relaxed space-y-px">
        {logs.length === 0 && (
          <div className="text-muted-foreground text-center py-8 text-xs">No logs yet</div>
        )}
        {logs.map((entry, i) => (
          <div key={i} className="flex gap-2">
            <span className="text-muted-foreground/60 flex-shrink-0">{formatTime(entry.timestamp)}</span>
            <span className={levelColors[entry.level]}>{entry.message}</span>
          </div>
        ))}
        <div ref={bottomRef} />
      </div>
    </div>
  );
}
