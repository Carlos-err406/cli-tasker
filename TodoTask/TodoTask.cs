namespace cli_tasker;


public record TodoTask(string Id, string Description, bool IsChecked, DateTime CreatedAt, string ListName)
{
    public static TodoTask CreateTodoTask(string description, string listName) =>
        new(Guid.NewGuid().ToString()[..3], description, false, DateTime.Now, listName);
    public TodoTask Check()
    {
        return this with { IsChecked = true };
    }
    public TodoTask UnCheck()
    {
        return this with { IsChecked = false };
    }
    public TodoTask Rename(string newDescription)
    {
        return this with { Description = newDescription };
    }
    public TodoTask MoveToList(string listName)
    {
        return this with { ListName = listName };
    }
}