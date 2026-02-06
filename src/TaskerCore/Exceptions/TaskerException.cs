namespace TaskerCore.Exceptions;

public class TaskerException : Exception
{
    public TaskerException(string message) : base(message) { }
}

public class ListNotFoundException : TaskerException
{
    public ListNotFoundException(string listName)
        : base($"List '{listName}' does not exist. Use 'tasker lists create {listName}' to create it.") { }
}

public class ListAlreadyExistsException : TaskerException
{
    public ListAlreadyExistsException(string listName)
        : base($"List '{listName}' already exists.") { }
}

public class InvalidListNameException : TaskerException
{
    public InvalidListNameException(string listName)
        : base($"Invalid list name '{listName}'. Use only letters, numbers, hyphens, and underscores.") { }
}

public class CannotModifyDefaultListException : TaskerException
{
    public CannotModifyDefaultListException(string operation)
        : base($"Cannot {operation} the default list.") { }
}

public class TaskNotFoundException : TaskerException
{
    public TaskNotFoundException(string taskId)
        : base($"Could not find task with id '{taskId}'.") { }
}

public class BackupNotFoundException : TaskerException
{
    public BackupNotFoundException(string message)
        : base(message) { }
}
