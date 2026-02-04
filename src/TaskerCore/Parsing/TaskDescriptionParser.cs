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
            // Last line is metadata-only, exclude it from display
            return string.Join("\n", lines.Take(lines.Length - 1));
        }

        return description;
    }

    // Match p1, p2, p3 for priority (must be standalone token)
    [GeneratedRegex(@"(?:^|\s)p([123])(?:\s|$)", RegexOptions.IgnoreCase)]
    private static partial Regex PriorityRegex();

    // Match @word for due dates
    [GeneratedRegex(@"@(\S+)")]
    private static partial Regex DueDateRegex();

    // Match #word for tags
    [GeneratedRegex(@"#(\w+)")]
    private static partial Regex TagRegex();
}
