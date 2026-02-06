namespace TaskerCore.Tests.Undo;

using TaskerCore.Data;
using TaskerCore.Models;
using TaskerCore.Undo.Commands;

[Collection("IsolatedTests")]
public class DeleteListCommandTests : IDisposable
{
    private readonly string _testDir;
    private readonly TaskerServices _services;

    public DeleteListCommandTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"tasker-undo-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDir);
        _services = new TaskerServices(_testDir);
        TaskerServices.SetDefault(_services);
        _services.Undo.ClearHistory();
    }

    public void Dispose()
    {
        _services.Undo.ClearHistory();
                if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, recursive: true);
        }
        GC.SuppressFinalize(this);
    }

    private void CreateListWithTasks(string listName, params string[] taskDescriptions)
    {
        ListManager.CreateList(_services, listName);
        var taskList = new TodoTaskList(_services, listName);
        foreach (var desc in taskDescriptions)
        {
            var task = TodoTask.CreateTodoTask(desc, listName);
            taskList.AddTodoTask(task, recordUndo: false);
        }
    }

    [Fact]
    public void DeleteList_RecordsUndoCommand()
    {
        // Arrange
        CreateListWithTasks("testlist", "Task 1", "Task 2");
        _services.Undo.ClearHistory();

        // Act
        ListManager.DeleteList(_services, "testlist");

        // Assert
        Assert.True(_services.Undo.CanUndo);
    }

    [Fact]
    public void DeleteList_Undo_RestoresList()
    {
        // Arrange
        CreateListWithTasks("mylist", "Task A", "Task B");
        _services.Undo.ClearHistory();

        // Act
        ListManager.DeleteList(_services, "mylist");
        Assert.False(ListManager.ListExists(_services, "mylist"));

        _services.Undo.Undo();

        // Assert
        Assert.True(ListManager.ListExists(_services, "mylist"));
    }

    [Fact]
    public void DeleteList_Undo_RestoresAllTasks()
    {
        // Arrange
        CreateListWithTasks("mylist", "Task A", "Task B", "Task C");
        _services.Undo.ClearHistory();

        // Act
        ListManager.DeleteList(_services, "mylist");
        _services.Undo.Undo();

        // Assert
        var taskList = new TodoTaskList(_services, "mylist");
        var tasks = taskList.GetAllTasks();
        Assert.Equal(3, tasks.Count);
        Assert.Contains(tasks, t => t.Description == "Task A");
        Assert.Contains(tasks, t => t.Description == "Task B");
        Assert.Contains(tasks, t => t.Description == "Task C");
    }

    [Fact]
    public void DeleteList_Redo_DeletesListAgain()
    {
        // Arrange
        CreateListWithTasks("mylist", "Task 1");
        _services.Undo.ClearHistory();

        // Act
        ListManager.DeleteList(_services, "mylist");
        _services.Undo.Undo();
        Assert.True(ListManager.ListExists(_services, "mylist"));

        _services.Undo.Redo();

        // Assert
        Assert.False(ListManager.ListExists(_services, "mylist"));
    }

    [Fact]
    public void DeleteList_EmptyList_CanBeUndone()
    {
        // Arrange
        ListManager.CreateList(_services, "emptylist");
        _services.Undo.ClearHistory();

        // Act
        ListManager.DeleteList(_services, "emptylist");
        Assert.False(ListManager.ListExists(_services, "emptylist"));

        _services.Undo.Undo();

        // Assert
        Assert.True(ListManager.ListExists(_services, "emptylist"));
    }

    [Fact]
    public void DeleteList_WithDefaultList_UndoRestoresDefault()
    {
        // Arrange
        CreateListWithTasks("mydefault", "Task 1");
        _services.Config.SetDefaultList("mydefault");
        _services.Undo.ClearHistory();

        // Act
        ListManager.DeleteList(_services, "mydefault");

        // Verify default was reset
        Assert.Equal(ListManager.DefaultListName, _services.Config.GetDefaultList());

        // Undo
        _services.Undo.Undo();

        // Assert - default should be restored
        Assert.Equal("mydefault", _services.Config.GetDefaultList());
        Assert.True(ListManager.ListExists(_services, "mydefault"));
    }

    [Fact]
    public void DeleteList_WithRecordUndoFalse_DoesNotRecordCommand()
    {
        // Arrange
        CreateListWithTasks("noundo", "Task 1");
        _services.Undo.ClearHistory();

        // Act
        ListManager.DeleteList(_services, "noundo", recordUndo: false);

        // Assert
        Assert.False(_services.Undo.CanUndo);
        Assert.False(ListManager.ListExists(_services, "noundo"));
    }

    [Fact]
    public void DeleteListCommand_Description_FormattedCorrectly()
    {
        // Arrange
        var cmd = new DeleteListCommand
        {
            ListName = "work",
            DeletedList = TaskList.Create("work"),
            WasDefaultList = false,
            OriginalIndex = 1
        };

        // Assert
        Assert.Equal("Delete list: work", cmd.Description);
    }

    [Fact]
    public void DeleteList_RestoresAtOriginalIndex()
    {
        // Arrange - create multiple lists
        ListManager.CreateList(_services, "listA");
        ListManager.CreateList(_services, "listB");
        ListManager.CreateList(_services, "listC");

        var initialOrder = ListManager.GetAllListNames(_services);
        var listBIndex = Array.IndexOf(initialOrder, "listB");
        _services.Undo.ClearHistory();

        // Act - delete listB
        ListManager.DeleteList(_services, "listB");
        _services.Undo.Undo();

        // Assert - listB should be at same position
        var restoredOrder = ListManager.GetAllListNames(_services);
        var newIndex = Array.IndexOf(restoredOrder, "listB");
        Assert.Equal(listBIndex, newIndex);
    }

    [Fact]
    public void DeleteList_WithTrashedTasks_RestoresTrash()
    {
        // Arrange - create list with task, then delete task to trash
        CreateListWithTasks("mylist", "Task 1", "Task 2");
        var taskList = new TodoTaskList(_services, "mylist");
        var tasks = taskList.GetAllTasks();
        taskList.DeleteTask(tasks[0].Id, recordUndo: false);  // Move to trash
        _services.Undo.ClearHistory();

        // Act
        ListManager.DeleteList(_services, "mylist");
        _services.Undo.Undo();

        // Assert - verify trashed task is restored
        var restoredTaskList = new TodoTaskList(_services, "mylist");
        var trash = restoredTaskList.GetTrash();
        Assert.Single(trash);  // One task was in trash
    }
}
