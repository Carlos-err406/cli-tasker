namespace TaskerCore.Tests.Undo;

using TaskerCore.Data;
using TaskerCore.Models;
using TaskerCore.Results;
using TaskStatus = TaskerCore.Models.TaskStatus;

[Collection("IsolatedTests")]
public class UndoDependencyTests : IDisposable
{
    private readonly string _testDir;
    private readonly TaskerServices _services;

    public UndoDependencyTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"tasker-dep-undo-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDir);
        _services = new TaskerServices(_testDir);
        TaskerServices.SetDefault(_services);
        _services.Undo.ClearHistory();
    }

    public void Dispose()
    {
        _services.Undo.ClearHistory();
        _services.Dispose();
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, recursive: true);
        }
        GC.SuppressFinalize(this);
    }

    private TodoTask AddTask(string desc, string list = "tasks")
    {
        var task = TodoTask.CreateTodoTask(desc, list);
        var taskList = new TodoTaskList(_services, list);
        taskList.AddTodoTask(task, recordUndo: false);
        return task;
    }

    // --- Undo SetParent ---

    [Fact]
    public void Undo_SetParent_RestoresNullParent()
    {
        var parent = AddTask("parent");
        var child = AddTask("child");
        var taskList = new TodoTaskList(_services);

        taskList.SetParent(child.Id, parent.Id);

        Assert.Equal(parent.Id, taskList.GetTodoTaskById(child.Id)!.ParentId);

        _services.Undo.Undo();

        Assert.Null(taskList.GetTodoTaskById(child.Id)!.ParentId);
    }

    [Fact]
    public void Undo_SetParent_RestoresPreviousParent()
    {
        var parent1 = AddTask("parent1");
        var parent2 = AddTask("parent2");
        var child = AddTask("child");
        var taskList = new TodoTaskList(_services);

        taskList.SetParent(child.Id, parent1.Id);
        _services.Undo.ClearHistory();

        taskList.SetParent(child.Id, parent2.Id);

        Assert.Equal(parent2.Id, taskList.GetTodoTaskById(child.Id)!.ParentId);

        _services.Undo.Undo();

        Assert.Equal(parent1.Id, taskList.GetTodoTaskById(child.Id)!.ParentId);
    }

    [Fact]
    public void Undo_UnsetParent_RestoresParent()
    {
        var parent = AddTask("parent");
        var child = AddTask("child");
        var taskList = new TodoTaskList(_services);

        taskList.SetParent(child.Id, parent.Id);
        _services.Undo.ClearHistory();

        taskList.UnsetParent(child.Id);

        Assert.Null(taskList.GetTodoTaskById(child.Id)!.ParentId);

        _services.Undo.Undo();

        Assert.Equal(parent.Id, taskList.GetTodoTaskById(child.Id)!.ParentId);
    }

    // --- Undo AddBlocker ---

    [Fact]
    public void Undo_AddBlocker_RemovesRelationship()
    {
        var blocker = AddTask("blocker");
        var blocked = AddTask("blocked");
        var taskList = new TodoTaskList(_services);

        taskList.AddBlocker(blocker.Id, blocked.Id);

        Assert.Single(taskList.GetBlocks(blocker.Id));

        _services.Undo.Undo();

        Assert.Empty(taskList.GetBlocks(blocker.Id));
    }

    [Fact]
    public void Undo_RemoveBlocker_RestoresRelationship()
    {
        var blocker = AddTask("blocker");
        var blocked = AddTask("blocked");
        var taskList = new TodoTaskList(_services);

        taskList.AddBlocker(blocker.Id, blocked.Id, recordUndo: false);
        taskList.RemoveBlocker(blocker.Id, blocked.Id);

        Assert.Empty(taskList.GetBlocks(blocker.Id));

        _services.Undo.Undo();

        Assert.Single(taskList.GetBlocks(blocker.Id));
    }

    // --- Undo cascade delete ---

    [Fact]
    public void Undo_CascadeDelete_RestoresParentAndDescendants()
    {
        var parent = AddTask("parent");
        var child = AddTask("child");
        var grandchild = AddTask("grandchild");
        var taskList = new TodoTaskList(_services);
        taskList.SetParent(child.Id, parent.Id, recordUndo: false);
        taskList.SetParent(grandchild.Id, child.Id, recordUndo: false);

        taskList.DeleteTask(parent.Id);

        Assert.Null(taskList.GetTodoTaskById(parent.Id));
        Assert.Null(taskList.GetTodoTaskById(child.Id));
        Assert.Null(taskList.GetTodoTaskById(grandchild.Id));

        _services.Undo.Undo();

        Assert.NotNull(taskList.GetTodoTaskById(parent.Id));
        Assert.NotNull(taskList.GetTodoTaskById(child.Id));
        Assert.NotNull(taskList.GetTodoTaskById(grandchild.Id));
    }

    // --- Undo cascade check ---

    [Fact]
    public void Undo_CascadeCheck_RestoresOriginalStatuses()
    {
        var parent = AddTask("parent");
        var child = AddTask("child");
        var taskList = new TodoTaskList(_services);
        taskList.SetParent(child.Id, parent.Id, recordUndo: false);

        // Set child to in-progress first
        taskList.SetStatus(child.Id, TaskStatus.InProgress, recordUndo: false);

        // Cascade-check parent (marks both as Done)
        taskList.SetStatus(parent.Id, TaskStatus.Done);

        Assert.Equal(TaskStatus.Done, taskList.GetTodoTaskById(parent.Id)!.Status);
        Assert.Equal(TaskStatus.Done, taskList.GetTodoTaskById(child.Id)!.Status);

        _services.Undo.Undo();

        Assert.Equal(TaskStatus.Pending, taskList.GetTodoTaskById(parent.Id)!.Status);
        Assert.Equal(TaskStatus.InProgress, taskList.GetTodoTaskById(child.Id)!.Status);
    }

    // --- Undo cascade move ---

    [Fact]
    public void Undo_CascadeMove_RestoresOriginalList()
    {
        TodoTaskList.CreateList(_services, "work");
        var parent = AddTask("parent");
        var child = AddTask("child");
        var taskList = new TodoTaskList(_services);
        taskList.SetParent(child.Id, parent.Id, recordUndo: false);

        taskList.MoveTask(parent.Id, "work");

        Assert.Equal("work", taskList.GetTodoTaskById(parent.Id)!.ListName);
        Assert.Equal("work", taskList.GetTodoTaskById(child.Id)!.ListName);

        _services.Undo.Undo();

        Assert.Equal("tasks", taskList.GetTodoTaskById(parent.Id)!.ListName);
        Assert.Equal("tasks", taskList.GetTodoTaskById(child.Id)!.ListName);
    }

    // --- Redo ---

    [Fact]
    public void Redo_SetParent_ReappliesParent()
    {
        var parent = AddTask("parent");
        var child = AddTask("child");
        var taskList = new TodoTaskList(_services);

        taskList.SetParent(child.Id, parent.Id);
        _services.Undo.Undo();

        Assert.Null(taskList.GetTodoTaskById(child.Id)!.ParentId);

        _services.Undo.Redo();

        Assert.Equal(parent.Id, taskList.GetTodoTaskById(child.Id)!.ParentId);
    }

    [Fact]
    public void Redo_AddBlocker_ReappliesRelationship()
    {
        var blocker = AddTask("blocker");
        var blocked = AddTask("blocked");
        var taskList = new TodoTaskList(_services);

        taskList.AddBlocker(blocker.Id, blocked.Id);
        _services.Undo.Undo();

        Assert.Empty(taskList.GetBlocks(blocker.Id));

        _services.Undo.Redo();

        Assert.Single(taskList.GetBlocks(blocker.Id));
    }

    // --- Undo AddRelated ---

    [Fact]
    public void Undo_AddRelated_RemovesRelationship()
    {
        var a = AddTask("task a");
        var b = AddTask("task b");
        var taskList = new TodoTaskList(_services);

        taskList.AddRelated(a.Id, b.Id);

        Assert.Single(taskList.GetRelated(a.Id));

        _services.Undo.Undo();

        Assert.Empty(taskList.GetRelated(a.Id));
        Assert.Empty(taskList.GetRelated(b.Id));
    }

    [Fact]
    public void Undo_RemoveRelated_RestoresRelationship()
    {
        var a = AddTask("task a");
        var b = AddTask("task b");
        var taskList = new TodoTaskList(_services);

        taskList.AddRelated(a.Id, b.Id, recordUndo: false);
        taskList.RemoveRelated(a.Id, b.Id);

        Assert.Empty(taskList.GetRelated(a.Id));

        _services.Undo.Undo();

        Assert.Single(taskList.GetRelated(a.Id));
        Assert.Single(taskList.GetRelated(b.Id));
    }

    [Fact]
    public void Redo_AddRelated_ReappliesRelationship()
    {
        var a = AddTask("task a");
        var b = AddTask("task b");
        var taskList = new TodoTaskList(_services);

        taskList.AddRelated(a.Id, b.Id);
        _services.Undo.Undo();

        Assert.Empty(taskList.GetRelated(a.Id));

        _services.Undo.Redo();

        Assert.Single(taskList.GetRelated(a.Id));
    }

    [Fact]
    public void Undo_AddRelated_SerializationRoundTrip()
    {
        var a = AddTask("task a");
        var b = AddTask("task b");
        var taskList = new TodoTaskList(_services);

        taskList.AddRelated(a.Id, b.Id);

        // Force serialization round-trip by saving and reloading
        _services.Undo.SaveHistory();

        var freshServices = new TaskerServices(_testDir);
        TaskerServices.SetDefault(freshServices);

        // Undo from freshly loaded history
        freshServices.Undo.Undo();

        var freshTaskList = new TodoTaskList(freshServices);
        Assert.Empty(freshTaskList.GetRelated(a.Id));

        freshServices.Dispose();
    }

    [Fact]
    public void Undo_RemoveRelated_SerializationRoundTrip()
    {
        var a = AddTask("task a");
        var b = AddTask("task b");
        var taskList = new TodoTaskList(_services);

        taskList.AddRelated(a.Id, b.Id, recordUndo: false);
        taskList.RemoveRelated(a.Id, b.Id);

        // Force serialization round-trip
        _services.Undo.SaveHistory();

        var freshServices = new TaskerServices(_testDir);
        TaskerServices.SetDefault(freshServices);

        freshServices.Undo.Undo();

        var freshTaskList = new TodoTaskList(freshServices);
        Assert.Single(freshTaskList.GetRelated(a.Id));

        freshServices.Dispose();
    }
}
