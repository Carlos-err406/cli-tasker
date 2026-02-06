namespace TaskerCore.Tests;

/// <summary>
/// Base class for tests that need isolated TaskerServices.
/// Each test gets its own temp directory and services instance.
/// </summary>
public abstract class TestBase : IDisposable
{
    protected readonly string TestDir;
    protected readonly TaskerServices Services;

    protected TestBase()
    {
        TestDir = Path.Combine(Path.GetTempPath(), $"tasker-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(TestDir);
        Services = new TaskerServices(TestDir);
        TaskerServices.SetDefault(Services);
    }

    public void Dispose()
    {
        Services.Undo.ClearHistory();
                if (Directory.Exists(TestDir))
        {
            Directory.Delete(TestDir, recursive: true);
        }
        GC.SuppressFinalize(this);
    }
}
