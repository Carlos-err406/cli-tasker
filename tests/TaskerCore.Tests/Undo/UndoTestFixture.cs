namespace TaskerCore.Tests.Undo;

/// <summary>
/// Collection definition for undo tests to run sequentially.
/// Required because UndoManager is a singleton and tests share state.
/// </summary>
[CollectionDefinition("UndoTests")]
public class UndoTestCollection : ICollectionFixture<UndoTestFixture>
{
}

public class UndoTestFixture
{
    // No shared state needed - just used to group tests
}
