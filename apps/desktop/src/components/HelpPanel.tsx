interface HelpPanelProps {
  onClose: () => void;
}

export function HelpPanel({ onClose }: HelpPanelProps) {
  return (
    <div className="absolute inset-0 z-40 bg-background/95 backdrop-blur-sm overflow-auto p-4">
      <div className="flex items-center justify-between mb-4">
        <h2 className="text-sm font-semibold">Help</h2>
        <button
          onClick={onClose}
          className="text-muted-foreground hover:text-foreground text-sm"
        >
          Close
        </button>
      </div>

      <div className="space-y-4 text-xs">
        <section>
          <h3 className="font-medium text-sm mb-1.5">Metadata Prefixes</h3>
          <div className="grid grid-cols-2 gap-1 text-muted-foreground">
            <span className="font-mono">p1, p2, p3</span>
            <span>Priority (high, medium, low)</span>
            <span className="font-mono">@date</span>
            <span>Due date</span>
            <span className="font-mono">#tag</span>
            <span>Tag</span>
            <span className="font-mono">^abc</span>
            <span>Set parent task</span>
            <span className="font-mono">!abc</span>
            <span>Blocks task</span>
            <span className="font-mono">-^abc</span>
            <span>Has subtask</span>
            <span className="font-mono">-!abc</span>
            <span>Blocked by task</span>
            <span className="font-mono">~abc</span>
            <span>Related task</span>
          </div>
        </section>

        <section>
          <h3 className="font-medium text-sm mb-1.5">Date Formats</h3>
          <div className="grid grid-cols-2 gap-1 text-muted-foreground">
            <span className="font-mono">today, tomorrow</span>
            <span>Relative days</span>
            <span className="font-mono">mon, tue, ... sun</span>
            <span>Next weekday</span>
            <span className="font-mono">jan15, feb3</span>
            <span>Month + day</span>
            <span className="font-mono">+3d</span>
            <span>Days from now</span>
            <span className="font-mono">2026-02-15</span>
            <span>Exact date</span>
          </div>
        </section>

        <section>
          <h3 className="font-medium text-sm mb-1.5">Search Filters</h3>
          <div className="grid grid-cols-2 gap-1 text-muted-foreground">
            <span className="font-mono">tag:name</span>
            <span>Filter by tag</span>
            <span className="font-mono">status:done</span>
            <span>pending, wip, done</span>
            <span className="font-mono">priority:high</span>
            <span>high/p1, medium/p2, low/p3</span>
            <span className="font-mono">due:today</span>
            <span>today, overdue, week, month</span>
            <span className="font-mono">list:name</span>
            <span>Filter by list</span>
            <span className="font-mono">has:subtasks</span>
            <span>subtasks, parent, due, tags</span>
          </div>
        </section>

        <section>
          <h3 className="font-medium text-sm mb-1.5">Keyboard Shortcuts</h3>
          <div className="grid grid-cols-2 gap-1 text-muted-foreground">
            <span className="font-mono">&#8984;K</span>
            <span>Focus search</span>
            <span className="font-mono">&#8984;R</span>
            <span>Refresh</span>
            <span className="font-mono">&#8984;Z</span>
            <span>Undo</span>
            <span className="font-mono">&#8984;&#8679;Z</span>
            <span>Redo</span>
            <span className="font-mono">&#8984;E</span>
            <span>Collapse/expand all</span>
            <span className="font-mono">&#8984;J</span>
            <span>Apply system sort</span>
            <span className="font-mono">&#8984;W</span>
            <span>Close popup</span>
            <span className="font-mono">&#8984;/</span>
            <span>Toggle help</span>
            <span className="font-mono">Esc</span>
            <span>Close / Cancel</span>
          </div>
        </section>
      </div>
    </div>
  );
}
