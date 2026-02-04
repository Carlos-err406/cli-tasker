namespace TaskerCore.Parsing;

using System.Text.RegularExpressions;
using TaskerCore.Models;

/// <summary>
/// Parses inline metadata from task descriptions.
/// Supports: !! (high), ! (medium), @date (due date), #tag (tags - future)
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

        var description = input;
        Priority? priority = null;
        DateOnly? dueDate = null;
        var tags = new List<string>();

        // Extract priority: !! (high) or ! (medium) - must be standalone token
        var priorityMatch = PriorityRegex().Match(description);
        if (priorityMatch.Success)
        {
            priority = priorityMatch.Value.Trim() == "!!" ? Models.Priority.High : Models.Priority.Medium;
            description = PriorityRegex().Replace(description, " ").Trim();
        }

        // Extract due date: @today, @tomorrow, @friday, @jan15, @+3d, etc.
        var dueDateMatch = DueDateRegex().Match(description);
        if (dueDateMatch.Success)
        {
            var dateStr = dueDateMatch.Groups[1].Value;
            dueDate = DateParser.Parse(dateStr);
            if (dueDate.HasValue)
            {
                description = DueDateRegex().Replace(description, " ").Trim();
            }
        }

        // Extract tags: #tag - store for future use
        var tagMatches = TagRegex().Matches(description);
        foreach (Match match in tagMatches)
        {
            tags.Add(match.Groups[1].Value);
        }
        if (tags.Count > 0)
        {
            description = TagRegex().Replace(description, " ").Trim();
        }

        // Clean up multiple spaces
        description = MultiSpaceRegex().Replace(description, " ").Trim();

        return new ParsedTask(description, priority, dueDate, tags.ToArray());
    }

    // Match !! or ! as standalone tokens (word boundary or whitespace around)
    [GeneratedRegex(@"(?:^|\s)(!{1,2})(?:\s|$)")]
    private static partial Regex PriorityRegex();

    // Match @word for due dates
    [GeneratedRegex(@"@(\S+)")]
    private static partial Regex DueDateRegex();

    // Match #word for tags
    [GeneratedRegex(@"#(\w+)")]
    private static partial Regex TagRegex();

    // Match multiple spaces
    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex MultiSpaceRegex();
}
