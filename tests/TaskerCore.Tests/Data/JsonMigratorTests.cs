namespace TaskerCore.Tests.Data;

using System.Text.Json;
using TaskerCore.Data;
using TaskerCore.Models;
using TaskerCore.Parsing;
using TaskStatus = TaskerCore.Models.TaskStatus;

[Collection("IsolatedTests")]
public class JsonMigratorTests : IDisposable
{
    private readonly string _testDir;

    public JsonMigratorTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"migration-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
                Directory.Delete(_testDir, recursive: true);
        }
        catch { }
        GC.SuppressFinalize(this);
    }

    private TaskerServices CreateServicesWithJsonFiles()
    {
        // Create services which triggers migration in constructor
        var services = new TaskerServices(_testDir);
        TaskerServices.SetDefault(services);
        return services;
    }

    /// <summary>
    /// Writes old-format JSON with IsChecked bool (pre-TaskStatus migration).
    /// </summary>
    private static string OldFormatJson(params (string listName, (string id, string desc, bool isChecked, string listName)[] tasks)[] lists)
    {
        var arr = lists.Select(l => new
        {
            ListName = l.listName,
            Tasks = l.tasks.Select(t => new
            {
                Id = t.id,
                Description = t.desc,
                IsChecked = t.isChecked,
                CreatedAt = DateTime.Now,
                ListName = t.listName
            }).ToArray()
        }).ToArray();
        return JsonSerializer.Serialize(arr);
    }

    [Fact]
    public void MigratesTasks_FromListFirstFormat()
    {
        // Arrange - write old-format JSON file with IsChecked booleans
        var json = OldFormatJson(
            ("tasks", [("abc", "Task one", false, "tasks"), ("def", "Task two", true, "tasks")]),
            ("work", [("ghi", "Work task", false, "work")]));
        File.WriteAllText(Path.Combine(_testDir, "all-tasks.json"), json);

        // Act
        var services = CreateServicesWithJsonFiles();

        // Assert
        var taskList = new TodoTaskList(services, "tasks");
        var allTasks = taskList.GetAllTasks();
        Assert.Equal(2, allTasks.Count);

        // Verify IsChecked=true maps to Status=Done
        var doneTask = allTasks.First(t => t.Id == "def");
        Assert.Equal(TaskStatus.Done, doneTask.Status);

        // Verify IsChecked=false maps to Status=Pending
        var pendingTask = allTasks.First(t => t.Id == "abc");
        Assert.Equal(TaskStatus.Pending, pendingTask.Status);

        var workList = new TodoTaskList(services, "work");
        Assert.Single(workList.GetAllTasks());

        // JSON file should be renamed to .bak
        Assert.False(File.Exists(Path.Combine(_testDir, "all-tasks.json")));
        Assert.True(File.Exists(Path.Combine(_testDir, "all-tasks.json.bak")));
    }

    [Fact]
    public void MigratesTrash_FromJson()
    {
        // Arrange - write trash JSON file
        var json = OldFormatJson(("tasks", [("xyz", "Deleted task", false, "tasks")]));
        File.WriteAllText(Path.Combine(_testDir, "all-tasks.trash.json"), json);

        // Act
        var services = CreateServicesWithJsonFiles();

        // Assert
        var taskList = new TodoTaskList(services);
        var trashItems = taskList.GetTrash();
        Assert.Single(trashItems);
        Assert.Equal("Deleted task", trashItems[0].Description);
    }

    [Fact]
    public void MigratesConfig_DefaultList()
    {
        // Arrange
        var config = new { DefaultList = "work" };
        File.WriteAllText(
            Path.Combine(_testDir, "config.json"),
            JsonSerializer.Serialize(config));

        // Act
        var services = CreateServicesWithJsonFiles();

        // Assert
        Assert.Equal("work", services.Config.GetDefaultList());
    }

    [Fact]
    public void MigratesEmptyLists()
    {
        // Arrange - list with no tasks
        var lists = new[]
        {
            new TaskList("tasks", []),
            new TaskList("projects", [])
        };

        File.WriteAllText(
            Path.Combine(_testDir, "all-tasks.json"),
            JsonSerializer.Serialize(lists));

        // Act
        var services = CreateServicesWithJsonFiles();

        // Assert
        var listNames = TodoTaskList.GetAllListNames(services);
        Assert.Contains("tasks", listNames);
        Assert.Contains("projects", listNames);
    }

    [Fact]
    public void NoJsonFiles_NoMigration()
    {
        // Act - create services without any JSON files
        var services = CreateServicesWithJsonFiles();

        // Assert - should have default list only
        var listNames = TodoTaskList.GetAllListNames(services);
        Assert.Single(listNames);
        Assert.Equal("tasks", listNames[0]);
    }

    [Fact]
    public void PreservesSortOrder_NewestFirst()
    {
        // Arrange - tasks in array order (newest first in old format)
        var json = OldFormatJson(("tasks", [
            ("new", "Newest", false, "tasks"),
            ("mid", "Middle", false, "tasks"),
            ("old", "Oldest", false, "tasks")]));
        File.WriteAllText(Path.Combine(_testDir, "all-tasks.json"), json);

        // Act
        var services = CreateServicesWithJsonFiles();

        // Assert - GetAllTasks returns sort_order DESC, so array order should match original
        var taskList = new TodoTaskList(services, "tasks");
        var result = taskList.GetAllTasks();
        Assert.Equal("new", result[0].Id);
        Assert.Equal("mid", result[1].Id);
        Assert.Equal("old", result[2].Id);
    }

    [Fact]
    public void MigratesTasksWithMetadata()
    {
        // Arrange - tasks with due dates, priority, and tags (as raw JSON to include metadata)
        var json = JsonSerializer.Serialize(new[]
        {
            new
            {
                ListName = "tasks",
                Tasks = new[]
                {
                    new
                    {
                        Id = "abc",
                        Description = "Important task",
                        IsChecked = false,
                        CreatedAt = DateTime.Now,
                        ListName = "tasks",
                        DueDate = "2026-03-15",
                        Priority = (int)Priority.High,
                        Tags = new[] { "urgent", "review" }
                    }
                }
            }
        });

        File.WriteAllText(Path.Combine(_testDir, "all-tasks.json"), json);

        // Act
        var services = CreateServicesWithJsonFiles();

        // Assert
        var taskList = new TodoTaskList(services, "tasks");
        var result = taskList.GetAllTasks();
        Assert.Single(result);
        Assert.Equal(new DateOnly(2026, 3, 15), result[0].DueDate);
        Assert.Equal(Priority.High, result[0].Priority);
        Assert.Equal(["urgent", "review"], result[0].Tags!);
    }
}
