namespace TaskerCore.Tests.Data;

using TaskerCore.Data;

[Collection("IsolatedTests")]
public class ListManagerResolveTests : IDisposable
{
    private readonly string _testDir;
    private readonly TaskerServices _services;

    public ListManagerResolveTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"tasker-resolve-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDir);
        _services = new TaskerServices(_testDir);
        TaskerServices.SetDefault(_services);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, recursive: true);
        }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void ResolveListFilter_ExplicitList_WinsOverAutoDetect()
    {
        // Create a list matching the working directory name
        ListManager.CreateList(_services, "my-project");

        var result = ListManager.ResolveListFilter(
            _services, explicitList: "work", showAll: false, workingDirectory: "/path/to/my-project");

        Assert.Equal("work", result);
    }

    [Fact]
    public void ResolveListFilter_ShowAll_ReturnsNull()
    {
        ListManager.CreateList(_services, "my-project");

        var result = ListManager.ResolveListFilter(
            _services, explicitList: null, showAll: true, workingDirectory: "/path/to/my-project");

        Assert.Null(result);
    }

    [Fact]
    public void ResolveListFilter_AutoDetects_WhenDirectoryMatchesList()
    {
        ListManager.CreateList(_services, "my-project");

        var result = ListManager.ResolveListFilter(
            _services, explicitList: null, showAll: false, workingDirectory: "/path/to/my-project");

        Assert.Equal("my-project", result);
    }

    [Fact]
    public void ResolveListFilter_ReturnsNull_WhenNoMatchingList()
    {
        var result = ListManager.ResolveListFilter(
            _services, explicitList: null, showAll: false, workingDirectory: "/path/to/nonexistent-dir");

        Assert.Null(result);
    }

    [Fact]
    public void ResolveListFilter_ExplicitList_IgnoresShowAll()
    {
        // Explicit list wins even when --all is set
        var result = ListManager.ResolveListFilter(
            _services, explicitList: "work", showAll: true, workingDirectory: "/whatever");

        Assert.Equal("work", result);
    }

    [Fact]
    public void ResolveListFilter_DetectsDefaultList()
    {
        // "tasks" always exists — should auto-detect it
        var result = ListManager.ResolveListFilter(
            _services, explicitList: null, showAll: false, workingDirectory: "/path/to/tasks");

        Assert.Equal("tasks", result);
    }

    // Init + auto-detect integration tests

    [Fact]
    public void Init_CreatesListThenAutoDetects()
    {
        // Simulate what `tasker init` does: create a list from directory name
        var dirName = "my-app";
        Assert.False(ListManager.ListExists(_services, dirName));

        ListManager.CreateList(_services, dirName);

        // Now auto-detect should find it
        var result = ListManager.ResolveListFilter(
            _services, explicitList: null, showAll: false, workingDirectory: $"/projects/{dirName}");

        Assert.Equal(dirName, result);
    }

    [Fact]
    public void Init_AlreadyExistingList_StillAutoDetects()
    {
        ListManager.CreateList(_services, "existing");

        // "init" on an existing list is a no-op, but auto-detect still works
        Assert.True(ListManager.ListExists(_services, "existing"));

        var result = ListManager.ResolveListFilter(
            _services, explicitList: null, showAll: false, workingDirectory: "/code/existing");

        Assert.Equal("existing", result);
    }

    [Fact]
    public void Init_InvalidDirectoryName_WouldNotCreateList()
    {
        // Directory names with spaces or special chars are invalid list names
        Assert.False(ListManager.IsValidListName("my project"));
        Assert.False(ListManager.IsValidListName("foo.bar"));
        Assert.True(ListManager.IsValidListName("my-project"));
        Assert.True(ListManager.IsValidListName("my_project"));
    }
}
