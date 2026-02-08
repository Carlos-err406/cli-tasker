import { describe, it, expect } from 'vitest';
import { parseDate } from '../../src/parsers/date-parser.js';

/** Create a fixed "today" date for deterministic tests */
function today(y: number, m: number, d: number): Date {
  return new Date(y, m - 1, d);
}

describe('parseDate', () => {
  const fixed = today(2026, 2, 8); // Sunday Feb 8 2026

  it('returns null for empty/null/undefined', () => {
    expect(parseDate(null)).toBeNull();
    expect(parseDate(undefined)).toBeNull();
    expect(parseDate('')).toBeNull();
    expect(parseDate('  ')).toBeNull();
  });

  // --- Named dates ---

  it('parses "today"', () => {
    expect(parseDate('today', fixed)).toBe('2026-02-08');
  });

  it('parses "tomorrow"', () => {
    expect(parseDate('tomorrow', fixed)).toBe('2026-02-09');
  });

  it('parses "yesterday"', () => {
    expect(parseDate('yesterday', fixed)).toBe('2026-02-07');
  });

  it('is case-insensitive', () => {
    expect(parseDate('TODAY', fixed)).toBe('2026-02-08');
    expect(parseDate('Tomorrow', fixed)).toBe('2026-02-09');
  });

  // --- Relative dates ---

  it('parses +Nd for days', () => {
    expect(parseDate('+3d', fixed)).toBe('2026-02-11');
    expect(parseDate('+1d', fixed)).toBe('2026-02-09');
  });

  it('parses +Nw for weeks', () => {
    expect(parseDate('+2w', fixed)).toBe('2026-02-22');
    expect(parseDate('+1w', fixed)).toBe('2026-02-15');
  });

  it('parses +Nm for months', () => {
    expect(parseDate('+1m', fixed)).toBe('2026-03-08');
    expect(parseDate('+3m', fixed)).toBe('2026-05-08');
  });

  // --- Day of week ---

  it('parses day-of-week names (next occurrence)', () => {
    // Feb 8 2026 is a Sunday
    expect(parseDate('mon', fixed)).toBe('2026-02-09');    // next Monday
    expect(parseDate('friday', fixed)).toBe('2026-02-13');  // next Friday
    expect(parseDate('sat', fixed)).toBe('2026-02-14');     // next Saturday
  });

  it('returns next week when day-of-week matches today', () => {
    // Today is Sunday, so "sunday" should return next Sunday
    expect(parseDate('sun', fixed)).toBe('2026-02-15');
    expect(parseDate('sunday', fixed)).toBe('2026-02-15');
  });

  it('handles abbreviated and full day names', () => {
    expect(parseDate('tue', fixed)).toBe('2026-02-10');
    expect(parseDate('tuesday', fixed)).toBe('2026-02-10');
    expect(parseDate('wed', fixed)).toBe('2026-02-11');
    expect(parseDate('wednesday', fixed)).toBe('2026-02-11');
    expect(parseDate('thu', fixed)).toBe('2026-02-12');
    expect(parseDate('thursday', fixed)).toBe('2026-02-12');
  });

  // --- Month + day ---

  it('parses monthDD format', () => {
    expect(parseDate('mar15', fixed)).toBe('2026-03-15');
    expect(parseDate('dec25', fixed)).toBe('2026-12-25');
  });

  it('rolls to next year if month+day is past', () => {
    expect(parseDate('jan1', fixed)).toBe('2027-01-01'); // Jan already past in Feb
  });

  it('returns null for invalid month+day', () => {
    expect(parseDate('feb30', fixed)).toBeNull();
  });

  // --- ISO format ---

  it('parses yyyy-MM-dd', () => {
    expect(parseDate('2026-03-01', fixed)).toBe('2026-03-01');
    expect(parseDate('2025-12-25', fixed)).toBe('2025-12-25');
  });

  it('returns null for invalid ISO dates', () => {
    expect(parseDate('2026-13-01', fixed)).toBeNull();
    expect(parseDate('not-a-date', fixed)).toBeNull();
  });

  // --- Edge cases ---

  it('trims whitespace', () => {
    expect(parseDate('  today  ', fixed)).toBe('2026-02-08');
  });
});
