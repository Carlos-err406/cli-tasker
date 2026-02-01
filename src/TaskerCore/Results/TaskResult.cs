namespace TaskerCore.Results;

/// <summary>
/// Result type for task operations. Allows data layer to return outcomes
/// without directly calling UI output methods.
/// </summary>
public abstract record TaskResult
{
    /// <summary>Operation completed successfully.</summary>
    public sealed record Success(string Message) : TaskResult;

    /// <summary>Task with the given ID was not found.</summary>
    public sealed record NotFound(string TaskId) : TaskResult;

    /// <summary>Operation had no effect (e.g., task already checked).</summary>
    public sealed record NoChange(string Message) : TaskResult;

    /// <summary>Operation failed with an error.</summary>
    public sealed record Error(string Message) : TaskResult;

    /// <summary>Check if the result indicates success.</summary>
    public bool IsSuccess => this is Success;

    /// <summary>Check if the result indicates failure.</summary>
    public bool IsError => this is Error or NotFound;
}

/// <summary>
/// Result type for batch operations that may have mixed success/failure.
/// </summary>
public record BatchTaskResult
{
    public required IReadOnlyList<TaskResult> Results { get; init; }

    public int SuccessCount => Results.Count(r => r is TaskResult.Success);
    public int FailureCount => Results.Count(r => r.IsError);
    public bool AllSucceeded => Results.All(r => r.IsSuccess);
    public bool AnyFailed => Results.Any(r => r.IsError);
}
