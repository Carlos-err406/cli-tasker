namespace TaskerCore.Tests.Undo;

/// <summary>
/// Collection definition for tests that need isolated storage via SetDefault.
/// Runs tests sequentially to prevent interference through the static TaskerServices.Default.
/// </summary>
[CollectionDefinition("IsolatedTests")]
public class IsolatedTestsCollection : ICollectionFixture<IsolatedTestsFixture>
{
}

public class IsolatedTestsFixture
{
    // No shared state needed - just used to group tests for sequential execution
}
