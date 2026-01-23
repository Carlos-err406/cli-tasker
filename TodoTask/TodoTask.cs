namespace cli_tasker;


record TodoTask(string Id, string Description, bool IsComplete, DateTime CreatedAt)
{
    public static TodoTask CreateTodoTask(string description) => new(Guid.NewGuid().ToString()[..3], description, false, DateTime.Now);
}