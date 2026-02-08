import { describe, it, expect } from 'vitest';
import { parse, getDisplayDescription, syncMetadataToDescription } from '../../src/parsers/task-description-parser.js';
import { Priority } from '../../src/types/priority.js';

/** Fixed date for deterministic due-date parsing */
const NOW = new Date(2026, 1, 8); // Feb 8 2026

describe('parse', () => {
  // --- Priority ---

  it.each([
    ['task\np1', Priority.High],
    ['task\np2', Priority.Medium],
    ['task\np3', Priority.Low],
    ['task\nP1', Priority.High], // case insensitive
  ])('extracts priority from "%s"', (input, expected) => {
    const result = parse(input, NOW);
    expect(result.priority).toBe(expected);
    expect(result.lastLineIsMetadataOnly).toBe(true);
  });

  it('returns null priority when no metadata line', () => {
    const result = parse('just a task');
    expect(result.priority).toBeNull();
    expect(result.lastLineIsMetadataOnly).toBe(false);
  });

  it('does not parse priority from mixed content', () => {
    const result = parse('task with p1 in text');
    expect(result.priority).toBeNull();
    expect(result.lastLineIsMetadataOnly).toBe(false);
  });

  // --- Tags ---

  it.each([
    ['#simple', 'simple'],
    ['#with-hyphen', 'with-hyphen'],
    ['#multi_underscore', 'multi_underscore'],
    ['#CamelCase', 'CamelCase'],
  ])('parses tag %s', (tag, expected) => {
    const result = parse(`task\n${tag}`, NOW);
    expect(result.tags).toContain(expected);
    expect(result.lastLineIsMetadataOnly).toBe(true);
  });

  it('extracts multiple tags', () => {
    const result = parse('task\n#tag1 #tag2 #cli-only', NOW);
    expect(result.tags).toHaveLength(3);
    expect(result.tags).toContain('tag1');
    expect(result.tags).toContain('tag2');
    expect(result.tags).toContain('cli-only');
  });

  // --- Due date ---

  it('parses @today', () => {
    const result = parse('task\n@today', NOW);
    expect(result.dueDate).toBe('2026-02-08');
    expect(result.dueDateRaw).toBe('today');
  });

  it('parses @tomorrow', () => {
    const result = parse('task\n@tomorrow', NOW);
    expect(result.dueDate).toBe('2026-02-09');
  });

  // --- Combined metadata ---

  it('extracts all combined metadata', () => {
    const result = parse('task\np1 @today #urgent #work', NOW);
    expect(result.priority).toBe(Priority.High);
    expect(result.dueDate).toBe('2026-02-08');
    expect(result.tags).toHaveLength(2);
    expect(result.tags).toContain('urgent');
    expect(result.tags).toContain('work');
    expect(result.lastLineIsMetadataOnly).toBe(true);
  });

  it('does not parse mixed content and metadata', () => {
    const result = parse('task\nsome text p1');
    expect(result.priority).toBeNull();
    expect(result.lastLineIsMetadataOnly).toBe(false);
  });

  // --- Empty input ---

  it('handles empty input', () => {
    const result = parse('');
    expect(result.priority).toBeNull();
    expect(result.dueDate).toBeNull();
    expect(result.tags).toHaveLength(0);
    expect(result.lastLineIsMetadataOnly).toBe(false);
  });

  // --- Parent reference ^abc ---

  it('extracts parent ID', () => {
    const result = parse('task\n^abc', NOW);
    expect(result.parentId).toBe('abc');
    expect(result.lastLineIsMetadataOnly).toBe(true);
  });

  it('does not parse parent from non-metadata line', () => {
    const result = parse('task with ^abc in text');
    expect(result.parentId).toBeNull();
    expect(result.lastLineIsMetadataOnly).toBe(false);
  });

  // --- Blocks reference !abc ---

  it('extracts single blocks ID', () => {
    const result = parse('task\n!h67', NOW);
    expect(result.blocksIds).toEqual(['h67']);
  });

  it('extracts multiple blocks IDs', () => {
    const result = parse('task\n!h67 !j89', NOW);
    expect(result.blocksIds).toHaveLength(2);
    expect(result.blocksIds).toContain('h67');
    expect(result.blocksIds).toContain('j89');
  });

  it('does not parse blocks from non-metadata line', () => {
    const result = parse('fix bug! important');
    expect(result.blocksIds).toBeNull();
    expect(result.lastLineIsMetadataOnly).toBe(false);
  });

  // --- Inverse parent -^abc ---

  it('extracts inverse parent (has subtask) IDs', () => {
    const result = parse('task\n-^abc', NOW);
    expect(result.hasSubtaskIds).toEqual(['abc']);
    expect(result.lastLineIsMetadataOnly).toBe(true);
  });

  it('extracts multiple inverse parent IDs', () => {
    const result = parse('task\n-^abc -^def', NOW);
    expect(result.hasSubtaskIds).toHaveLength(2);
    expect(result.hasSubtaskIds).toContain('abc');
    expect(result.hasSubtaskIds).toContain('def');
  });

  it('does not confuse ^abc and -^abc', () => {
    const result = parse('task\n^abc -^def', NOW);
    expect(result.parentId).toBe('abc');
    expect(result.hasSubtaskIds).toEqual(['def']);
  });

  // --- Inverse blocker -!abc ---

  it('extracts inverse blocker (blocked by) IDs', () => {
    const result = parse('task\n-!abc', NOW);
    expect(result.blockedByIds).toEqual(['abc']);
    expect(result.lastLineIsMetadataOnly).toBe(true);
  });

  it('extracts multiple inverse blocker IDs', () => {
    const result = parse('task\n-!abc -!def', NOW);
    expect(result.blockedByIds).toHaveLength(2);
    expect(result.blockedByIds).toContain('abc');
    expect(result.blockedByIds).toContain('def');
  });

  it('does not confuse !abc and -!abc', () => {
    const result = parse('task\n!abc -!def', NOW);
    expect(result.blocksIds).toEqual(['abc']);
    expect(result.blockedByIds).toEqual(['def']);
  });

  // --- Related ~abc ---

  it('extracts related IDs', () => {
    const result = parse('task\n~abc', NOW);
    expect(result.relatedIds).toEqual(['abc']);
    expect(result.lastLineIsMetadataOnly).toBe(true);
  });

  it('extracts multiple related IDs', () => {
    const result = parse('task\n~abc ~def', NOW);
    expect(result.relatedIds).toHaveLength(2);
    expect(result.relatedIds).toContain('abc');
    expect(result.relatedIds).toContain('def');
  });

  // --- Combined dependencies + metadata ---

  it('extracts all marker types together', () => {
    const result = parse('build API\n^abc !ghi -^def -!jkl ~xyz p1 @today #tag', NOW);
    expect(result.parentId).toBe('abc');
    expect(result.blocksIds).toEqual(['ghi']);
    expect(result.hasSubtaskIds).toEqual(['def']);
    expect(result.blockedByIds).toEqual(['jkl']);
    expect(result.relatedIds).toEqual(['xyz']);
    expect(result.priority).toBe(Priority.High);
    expect(result.dueDate).toBe('2026-02-08');
    expect(result.tags).toContain('tag');
    expect(result.lastLineIsMetadataOnly).toBe(true);
  });

  it('extracts mixed related and other metadata', () => {
    const result = parse('build API\n~abc !h67 p1 #tag', NOW);
    expect(result.relatedIds).toEqual(['abc']);
    expect(result.blocksIds).toEqual(['h67']);
    expect(result.priority).toBe(Priority.High);
    expect(result.tags).toContain('tag');
  });
});

describe('getDisplayDescription', () => {
  it('hides metadata-only last line', () => {
    expect(getDisplayDescription('My task\np1 #urgent')).toBe('My task');
  });

  it('keeps non-metadata lines', () => {
    expect(getDisplayDescription('My task\nsome details')).toBe('My task\nsome details');
  });

  it('shows single metadata-only line (otherwise task would be empty)', () => {
    expect(getDisplayDescription('p1 #urgent')).toBe('p1 #urgent');
  });

  it('hides dependency tokens', () => {
    expect(getDisplayDescription('My task\n^abc !h67 p1')).toBe('My task');
  });

  it('hides inverse markers', () => {
    expect(getDisplayDescription('My task\n-^abc -!def p1')).toBe('My task');
  });

  it('hides related tokens', () => {
    expect(getDisplayDescription('My task\n~abc ~def')).toBe('My task');
  });

  it('hides all marker types', () => {
    expect(getDisplayDescription('My task\n^abc !ghi -^def -!jkl ~xyz p1 #tag')).toBe('My task');
  });
});

describe('syncMetadataToDescription', () => {
  it('adds metadata line', () => {
    expect(syncMetadataToDescription('task', Priority.High, null, null)).toBe('task\np1');
  });

  it('updates existing metadata line', () => {
    expect(syncMetadataToDescription('task\np3', Priority.High, null, null)).toBe('task\np1');
  });

  it('removes metadata line when empty', () => {
    expect(syncMetadataToDescription('task\np1', null, null, null)).toBe('task');
  });

  it('includes parent and blocks', () => {
    expect(
      syncMetadataToDescription('task', Priority.High, null, null, 'abc', ['h67']),
    ).toBe('task\n^abc !h67 p1');
  });

  it('updates existing dependency tokens', () => {
    expect(
      syncMetadataToDescription('task\n^abc p1', null, null, null, 'def'),
    ).toBe('task\n^def');
  });

  it('includes inverse markers', () => {
    expect(
      syncMetadataToDescription('task', null, null, null, null, null, ['abc'], ['def']),
    ).toBe('task\n-^abc -!def');
  });

  it('preserves full marker order', () => {
    const result = syncMetadataToDescription(
      'task', Priority.High, '2026-02-08', ['tag'],
      'abc', ['ghi'], ['def'], ['jkl'], ['xyz'],
    );
    expect(result).toBe('task\n^abc !ghi -^def -!jkl ~xyz p1 @2026-02-08 #tag');
  });

  it('preserves inverse markers on update', () => {
    const result = syncMetadataToDescription(
      'task\n-^abc -!def p1', Priority.Medium, null, null,
      null, null, ['abc'], ['def'],
    );
    expect(result).toBe('task\n-^abc -!def p2');
  });

  it('includes related IDs', () => {
    expect(
      syncMetadataToDescription('task', null, null, null, null, null, null, null, ['abc', 'def']),
    ).toBe('task\n~abc ~def');
  });

  it('produces correct order for related + other metadata', () => {
    expect(
      syncMetadataToDescription('task', Priority.High, null, ['tag'], 'abc', ['h67'], null, null, ['xyz']),
    ).toBe('task\n^abc !h67 ~xyz p1 #tag');
  });

  it('round-trips through parse and sync', () => {
    const original = 'build API\n^abc !ghi -^def -!jkl ~xyz p1 @2026-02-07 #tag';
    const parsed = parse(original, NOW);
    const synced = syncMetadataToDescription(
      'build API', parsed.priority, parsed.dueDate, parsed.tags,
      parsed.parentId, parsed.blocksIds,
      parsed.hasSubtaskIds, parsed.blockedByIds, parsed.relatedIds,
    );
    expect(synced).toBe(original);
  });
});
