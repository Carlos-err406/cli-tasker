namespace cli_tasker;

using System.Text.Json;


record TodoTask(string Id, string Description, bool IsComplete, DateTime CreatedAt)
{
    public string ToJson() => JsonSerializer.Serialize(this);
    public static TodoTask CreateTodoTask(string description) => new(Guid.NewGuid().ToString()[..3], description, false, DateTime.Now);
}