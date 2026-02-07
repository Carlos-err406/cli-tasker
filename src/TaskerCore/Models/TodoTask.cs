namespace TaskerCore.Models;

using TaskerCore.Parsing;

public record TodoTask(
    string Id,
    string Description,
    TaskStatus Status,
    DateTime CreatedAt,
    string ListName,
    DateOnly? DueDate = null,
    Priority? Priority = null,
    string[]? Tags = null,
    DateTime? CompletedAt = null,
    string? ParentId = null)
{
    public static TodoTask CreateTodoTask(string description, string listName)
    {
        var trimmed = description.Trim();
        var parsed = TaskDescriptionParser.Parse(trimmed);

        return new TodoTask(
            Guid.NewGuid().ToString()[..3],
            trimmed,
            TaskStatus.Pending,
            DateTime.Now,
            listName,
            parsed.DueDate,
            parsed.Priority,
            parsed.Tags.Length > 0 ? parsed.Tags : null,
            ParentId: parsed.ParentId
        );
    }

    // Parent mutation methods
    public bool HasParent => ParentId != null;

    public TodoTask SetParent(string parentId) => this with { ParentId = parentId };

    public TodoTask ClearParent() => this with { ParentId = null };

    // Computed properties for display logic
    public bool IsOverdue => DueDate.HasValue && DueDate.Value < DateOnly.FromDateTime(DateTime.Today);
    public bool IsDueToday => DueDate.HasValue && DueDate.Value == DateOnly.FromDateTime(DateTime.Today);
    public bool IsDueSoon => DueDate.HasValue && DueDate.Value <= DateOnly.FromDateTime(DateTime.Today.AddDays(3));
    public bool HasTags => Tags is { Length: > 0 };

    public TodoTask WithStatus(TaskStatus status) => this with
    {
        Status = status,
        CompletedAt = status == TaskStatus.Done ? DateTime.UtcNow : null
    };

    public TodoTask Rename(string newDescription)
    {
        var trimmed = newDescription.Trim();
        var parsed = TaskDescriptionParser.Parse(trimmed);

        return this with
        {
            Description = trimmed,
            Priority = parsed.Priority,
            DueDate = parsed.DueDate,
            Tags = parsed.Tags.Length > 0 ? parsed.Tags : null,
            // If metadata line exists, use parsed parent (null = cleared).
            // If no metadata line, preserve existing parent.
            ParentId = parsed.LastLineIsMetadataOnly ? parsed.ParentId : ParentId
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
