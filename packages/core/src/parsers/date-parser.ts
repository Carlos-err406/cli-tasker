/**
 * Parses human-friendly date strings into yyyy-MM-dd format.
 * Supports: today, tomorrow, yesterday, relative (+3d/+2w/+1m),
 * day-of-week names (mon-sunday), month+day (jan15), and ISO format.
 */

const RELATIVE_RE = /^\+(\d+)([dwm])$/;
const MONTH_DAY_RE = /^(jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec)(\d{1,2})$/;

const DAY_MAP: Record<string, number> = {
  sun: 0, sunday: 0,
  mon: 1, monday: 1,
  tue: 2, tuesday: 2,
  wed: 3, wednesday: 3,
  thu: 4, thursday: 4,
  fri: 5, friday: 5,
  sat: 6, saturday: 6,
};

const MONTH_MAP: Record<string, number> = {
  jan: 0, feb: 1, mar: 2, apr: 3,
  may: 4, jun: 5, jul: 6, aug: 7,
  sep: 8, oct: 9, nov: 10, dec: 11,
};

/** Format a Date as yyyy-MM-dd */
export function formatDate(d: Date): string {
  const y = d.getFullYear();
  const m = String(d.getMonth() + 1).padStart(2, '0');
  const day = String(d.getDate()).padStart(2, '0');
  return `${y}-${m}-${day}`;
}

/** Add days to a date (returns new Date) */
export function addDays(d: Date, n: number): Date {
  const r = new Date(d);
  r.setDate(r.getDate() + n);
  return r;
}

/** Add months to a date (returns new Date) */
function addMonths(d: Date, n: number): Date {
  const r = new Date(d);
  r.setMonth(r.getMonth() + n);
  return r;
}

function tryParseRelative(input: string, today: Date): string | null {
  const m = RELATIVE_RE.exec(input);
  if (!m) return null;

  const count = parseInt(m[1]!, 10);
  switch (m[2]) {
    case 'd': return formatDate(addDays(today, count));
    case 'w': return formatDate(addDays(today, count * 7));
    case 'm': return formatDate(addMonths(today, count));
    default: return null;
  }
}

function tryParseDayOfWeek(input: string, today: Date): string | null {
  const target = DAY_MAP[input];
  if (target === undefined) return null;

  let daysUntil = (target - today.getDay() + 7) % 7;
  if (daysUntil === 0) daysUntil = 7; // Next week if today
  return formatDate(addDays(today, daysUntil));
}

function tryParseMonthDay(input: string, today: Date): string | null {
  const m = MONTH_DAY_RE.exec(input);
  if (!m) return null;

  const month = MONTH_MAP[m[1]!]!;
  const day = parseInt(m[2]!, 10);

  // Validate the day is valid for that month
  const candidate = new Date(today.getFullYear(), month, day);
  if (candidate.getMonth() !== month || candidate.getDate() !== day) {
    return null; // Invalid date (e.g. feb30)
  }

  // If the date is in the past, use next year
  const todayStr = formatDate(today);
  const candidateStr = formatDate(candidate);
  if (candidateStr < todayStr) {
    candidate.setFullYear(candidate.getFullYear() + 1);
  }
  return formatDate(candidate);
}

/** yyyy-MM-dd pattern */
const ISO_DATE_RE = /^\d{4}-\d{2}-\d{2}$/;

function tryParseStandard(input: string): string | null {
  // Accept yyyy-MM-dd format
  if (!ISO_DATE_RE.test(input)) return null;

  const d = new Date(input + 'T00:00:00');
  if (isNaN(d.getTime())) return null;

  // Verify the parsed date matches the input (rejects invalid dates like 2026-02-30)
  return formatDate(d) === input ? input : null;
}

/**
 * Parse a human-friendly date string into yyyy-MM-dd format.
 * Returns null if the input can't be parsed.
 *
 * @param input - Date string (e.g. "today", "+3d", "friday", "jan15", "2026-03-01")
 * @param now - Override "today" for testing. Defaults to current date.
 */
export function parseDate(input: string | null | undefined, now?: Date): string | null {
  if (!input?.trim()) return null;

  const today = now ?? new Date();
  // Zero out time component for consistent date math
  today.setHours(0, 0, 0, 0);

  const normalized = input.trim().toLowerCase();

  switch (normalized) {
    case 'today': return formatDate(today);
    case 'tomorrow': return formatDate(addDays(today, 1));
    case 'yesterday': return formatDate(addDays(today, -1));
    default:
      return tryParseRelative(normalized, today)
        ?? tryParseDayOfWeek(normalized, today)
        ?? tryParseMonthDay(normalized, today)
        ?? tryParseStandard(input.trim());
  }
}
