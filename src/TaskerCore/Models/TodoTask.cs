namespace TaskerCore.Models;

public record TodoTask(string Id, string Description, bool IsChecked, DateTime CreatedAt, string ListName)
{
    public static TodoTask CreateTodoTask(string description, string listName) =>
        new(Guid.NewGuid().ToString()[..3], description, false, DateTime.Now, listName);

    public TodoTask Check() => this with { IsChecked = true };

    public TodoTask UnCheck() => this with { IsChecked = false };

    public TodoTask Rename(string newDescription) => this with { Description = newDescription };

    public TodoTask MoveToList(string listName) => this with { ListName = listName };
}
