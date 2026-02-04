namespace TaskerCore.Models;

public record TodoTask(
    string Id,
    string Description,
    bool IsChecked,
    DateTime CreatedAt,
    string ListName,
    DateOnly? DueDate = null,
    Priority? Priority = null)
{
    public static TodoTask CreateTodoTask(string description, string listName) =>
        new(Guid.NewGuid().ToString()[..3], description, false, DateTime.Now, listName);

    // Computed properties for display logic
    public bool IsOverdue => DueDate.HasValue && DueDate.Value < DateOnly.FromDateTime(DateTime.Today);
    public bool IsDueToday => DueDate.HasValue && DueDate.Value == DateOnly.FromDateTime(DateTime.Today);
    public bool IsDueSoon => DueDate.HasValue && DueDate.Value <= DateOnly.FromDateTime(DateTime.Today.AddDays(3));

    public TodoTask Check() => this with { IsChecked = true };

    public TodoTask UnCheck() => this with { IsChecked = false };

    public TodoTask Rename(string newDescription) => this with { Description = newDescription };

    public TodoTask MoveToList(string listName) => this with { ListName = listName };

    public TodoTask SetDueDate(DateOnly date) => this with { DueDate = date };

    public TodoTask ClearDueDate() => this with { DueDate = null };

    public TodoTask SetPriority(Priority priority) => this with { Priority = priority };

    public TodoTask ClearPriority() => this with { Priority = null };
}
