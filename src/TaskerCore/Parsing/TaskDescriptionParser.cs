namespace TaskerCore.Parsing;

using System.Text.RegularExpressions;
using TaskerCore.Models;

/// <summary>
/// Parses inline metadata from task descriptions.
/// Only parses the LAST LINE if it contains ONLY metadata markers.
/// Keeps original text intact (does not strip markers).
/// Supports: p1/p2/p3 (priority), @date (due date), #tag (tags)
/// </summary>
public static partial class TaskDescriptionParser
{
    public record ParsedTask(
        string Description,
        Priority? Priority,
        DateOnly? DueDate,
        string[] Tags,
        bool LastLineIsMetadataOnly);

    public static ParsedTask Parse(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return new ParsedTask(input, null, null, [], false);

        var lines = input.Split('\n');
        var lastLine = lines[^1];

        // Check if last line is metadata-only (contains only p1/p2/p3, @date, #tags, and whitespace)
        var strippedLine = lastLine;
        strippedLine = PriorityRegex().Replace(strippedLine, " ");
        strippedLine = DueDateRegex().Replace(strippedLine, " ");
        strippedLine = TagRegex().Replace(strippedLine, " ");
        var isMetadataOnly = string.IsNullOrWhiteSpace(strippedLine);

        // Only parse if the last line is metadata-only
        if (!isMetadataOnly)
            return new ParsedTask(input, null, null, [], false);

        Priority? priority = null;
        DateOnly? dueDate = null;
        var tags = new List<string>();

        // Extract priority: p1 (high), p2 (medium), p3 (low)
        var priorityMatch = PriorityRegex().Match(lastLine);
        if (priorityMatch.Success)
        {
            priority = priorityMatch.Groups[1].Value switch
            {
                "1" => Models.Priority.High,
                "2" => Models.Priority.Medium,
                "3" => Models.Priority.Low,
                _ => null
            };
        }

        // Extract due date: @today, @tomorrow, @friday, @jan15, @+3d, etc.
        var dueDateMatch = DueDateRegex().Match(lastLine);
        if (dueDateMatch.Success)
        {
            var dateStr = dueDateMatch.Groups[1].Value;
            dueDate = DateParser.Parse(dateStr);
        }

        // Extract tags: #tag
        var tagMatches = TagRegex().Matches(lastLine);
        foreach (Match match in tagMatches)
        {
            tags.Add(match.Groups[1].Value);
        }

        // Keep original description intact
        return new ParsedTask(input, priority, dueDate, tags.ToArray(), true);
    }

    /// <summary>
    /// Gets the description for display purposes (hides metadata-only last line).
    /// </summary>
    public static string GetDisplayDescription(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return description;

        var lines = description.Split('\n');
        if (lines.Length == 1)
        {
            // Single line - check if it's metadata-only
            var strippedLine = lines[0];
            strippedLine = PriorityRegex().Replace(strippedLine, " ");
            strippedLine = DueDateRegex().Replace(strippedLine, " ");
            strippedLine = TagRegex().Replace(strippedLine, " ");
            // If single line is metadata-only, still show it (otherwise task would be empty)
            return description;
        }

        // Multi-line - check if last line is metadata-only
        var lastLine = lines[^1];
        var stripped = lastLine;
        stripped = PriorityRegex().Replace(stripped, " ");
        stripped = DueDateRegex().Replace(stripped, " ");
        stripped = TagRegex().Replace(stripped, " ");

        if (string.IsNullOrWhiteSpace(stripped))
        {
            // Last line is metadata-only, exclude it from display and trim trailing whitespace
            return string.Join("\n", lines.Take(lines.Length - 1)).TrimEnd();
        }

        return description.TrimEnd();
    }

    /// <summary>
    /// Updates the description to sync metadata changes. Updates existing metadata line or appends new one.
    /// </summary>
    public static string SyncMetadataToDescription(string description, Priority? priority, DateOnly? dueDate, string[]? tags)
    {
        var lines = description.Split('\n').ToList();
        var lastLine = lines[^1];

        // Check if last line is metadata-only
        var stripped = lastLine;
        stripped = PriorityRegex().Replace(stripped, " ");
        stripped = DueDateRegex().Replace(stripped, " ");
        stripped = TagRegex().Replace(stripped, " ");
        var hasMetadataLine = string.IsNullOrWhiteSpace(stripped);

        // Build the new metadata line
        var metaParts = new List<string>();
        if (priority.HasValue)
        {
            var p = priority.Value switch
            {
                Models.Priority.High => "p1",
                Models.Priority.Medium => "p2",
                Models.Priority.Low => "p3",
                _ => ""
            };
            if (!string.IsNullOrEmpty(p)) metaParts.Add(p);
        }
        if (dueDate.HasValue)
        {
            metaParts.Add($"@{dueDate.Value:yyyy-MM-dd}");
        }
        if (tags is { Length: > 0 })
        {
            metaParts.AddRange(tags.Select(t => $"#{t}"));
        }

        var newMetaLine = string.Join(" ", metaParts);

        if (hasMetadataLine)
        {
            // Replace the existing metadata line
            if (string.IsNullOrEmpty(newMetaLine))
            {
                // Remove the metadata line entirely
                lines.RemoveAt(lines.Count - 1);
            }
            else
            {
                lines[^1] = newMetaLine;
            }
        }
        else if (!string.IsNullOrEmpty(newMetaLine))
        {
            // Append new metadata line
            lines.Add(newMetaLine);
        }

        return string.Join("\n", lines);
    }

    // Match p1, p2, p3 for priority (must be standalone token)
    [GeneratedRegex(@"(?:^|\s)p([123])(?:\s|$)", RegexOptions.IgnoreCase)]
    private static partial Regex PriorityRegex();

    // Match @word for due dates
    [GeneratedRegex(@"@(\S+)")]
    private static partial Regex DueDateRegex();

    // Match #word for tags (supports hyphens like #cli-only)
    [GeneratedRegex(@"#([\w-]+)")]
    private static partial Regex TagRegex();
}
