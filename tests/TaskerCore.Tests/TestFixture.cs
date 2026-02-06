namespace TaskerCore.Tests;

/// <summary>
/// Test fixture that provides isolated storage for tests.
/// Use with [Collection("Storage")] to share across test classes that need storage.
/// </summary>
public class StorageFixture : IDisposable
{
    public string TestDirectory { get; }
    public TaskerServices Services { get; }

    public StorageFixture()
    {
        TestDirectory = Path.Combine(Path.GetTempPath(), $"tasker-tests-{Guid.NewGuid()}");
        Directory.CreateDirectory(TestDirectory);
        Services = new TaskerServices(TestDirectory);
        TaskerServices.SetDefault(Services);
    }

    public void Dispose()
    {
        Services.Undo.ClearHistory();
                if (Directory.Exists(TestDirectory))
        {
            Directory.Delete(TestDirectory, recursive: true);
        }
        GC.SuppressFinalize(this);
    }
}

[CollectionDefinition("Storage")]
public class StorageCollection : ICollectionFixture<StorageFixture>
{
    // This class has no code, and is never created. Its purpose is to be
    // the place to apply [CollectionDefinition] and the ICollectionFixture<> interface.
}
