namespace TaskerCore.Models;

using TaskerCore.Parsing;

public record TodoTask(
    string Id,
    string Description,
    bool IsChecked,
    DateTime CreatedAt,
    string ListName,
    DateOnly? DueDate = null,
    Priority? Priority = null,
    string[]? Tags = null)
{
    public static TodoTask CreateTodoTask(string description, string listName) =>
        new(Guid.NewGuid().ToString()[..3], description.Trim(), false, DateTime.Now, listName);

    // Computed properties for display logic
    public bool IsOverdue => DueDate.HasValue && DueDate.Value < DateOnly.FromDateTime(DateTime.Today);
    public bool IsDueToday => DueDate.HasValue && DueDate.Value == DateOnly.FromDateTime(DateTime.Today);
    public bool IsDueSoon => DueDate.HasValue && DueDate.Value <= DateOnly.FromDateTime(DateTime.Today.AddDays(3));
    public bool HasTags => Tags is { Length: > 0 };

    public TodoTask Check() => this with { IsChecked = true };

    public TodoTask UnCheck() => this with { IsChecked = false };

    public TodoTask Rename(string newDescription)
    {
        var trimmed = newDescription.Trim();
        var parsed = TaskDescriptionParser.Parse(trimmed);

        return this with
        {
            Description = trimmed,
            Priority = parsed.Priority,
            DueDate = parsed.DueDate,
            Tags = parsed.Tags.Length > 0 ? parsed.Tags : null
        };
    }

    public TodoTask MoveToList(string listName) => this with { ListName = listName };

    public TodoTask SetDueDate(DateOnly date) => this with { DueDate = date };

    public TodoTask ClearDueDate() => this with { DueDate = null };

    public TodoTask SetPriority(Priority priority) => this with { Priority = priority };

    public TodoTask ClearPriority() => this with { Priority = null };

    public TodoTask SetTags(string[] tags) => this with { Tags = tags.Length > 0 ? tags : null };

    public TodoTask ClearTags() => this with { Tags = null };
}
