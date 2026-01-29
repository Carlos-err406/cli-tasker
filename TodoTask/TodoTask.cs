namespace cli_tasker;


record TodoTask(string Id, string Description, bool IsChecked, DateTime CreatedAt)
{
    public static TodoTask CreateTodoTask(string description) => new(Guid.NewGuid().ToString()[..3], description, false, DateTime.Now);
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
}