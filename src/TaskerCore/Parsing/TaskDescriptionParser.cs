namespace TaskerCore.Parsing;

using System.Text.RegularExpressions;
using TaskerCore.Models;

/// <summary>
/// Parses inline metadata from task descriptions.
/// Only parses the LAST LINE for metadata markers.
/// Keeps original text intact (does not strip markers).
/// Supports: p1/p2/p3 (priority), @date (due date), #tag (tags - future)
/// </summary>
public static partial class TaskDescriptionParser
{
    public record ParsedTask(
        string Description,
        Priority? Priority,
        DateOnly? DueDate,
        string[] Tags);

    public static ParsedTask Parse(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return new ParsedTask(input, null, null, []);

        // Only parse metadata from the last line
        var lines = input.Split('\n');
        var lastLine = lines[^1];

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

        // Extract tags: #tag - store for future use
        var tagMatches = TagRegex().Matches(lastLine);
        foreach (Match match in tagMatches)
        {
            tags.Add(match.Groups[1].Value);
        }

        // Keep original description intact
        return new ParsedTask(input, priority, dueDate, tags.ToArray());
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
