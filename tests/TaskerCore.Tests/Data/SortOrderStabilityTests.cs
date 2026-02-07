namespace TaskerCore.Tests.Data;

using TaskerCore.Data;
using TaskerCore.Models;
using TaskStatus = TaskerCore.Models.TaskStatus;

public class SortOrderStabilityTests : IDisposable
{
    private readonly TaskerServices _services;

    public SortOrderStabilityTests()
    {
        _services = TaskerServices.CreateInMemory();
    }

    public void Dispose()
    {
        _services.Dispose();
        GC.SuppressFinalize(this);
    }

    private long GetSortOrder(string taskId) =>
        _services.Db.ExecuteScalar<long>(
            "SELECT sort_order FROM tasks WHERE id = @id",
            ("@id", taskId))!;

    [Fact]
    public void SetStatus_DoesNotChangeSortOrder()
    {
        var task = TodoTask.CreateTodoTask("test task", "tasks");
        var taskList = new TodoTaskList(_services, "tasks");
        taskList.AddTodoTask(task, recordUndo: false);

        var sortOrderBefore = GetSortOrder(task.Id);

        taskList.SetStatus(task.Id, TaskStatus.InProgress);
        Assert.Equal(sortOrderBefore, GetSortOrder(task.Id));

        taskList.SetStatus(task.Id, TaskStatus.Done);
        Assert.Equal(sortOrderBefore, GetSortOrder(task.Id));

        taskList.SetStatus(task.Id, TaskStatus.Pending);
        Assert.Equal(sortOrderBefore, GetSortOrder(task.Id));
    }

    [Fact]
    public void SetStatuses_DoesNotChangeSortOrder()
    {
        var task1 = TodoTask.CreateTodoTask("task one", "tasks");
        var task2 = TodoTask.CreateTodoTask("task two", "tasks");
        var taskList = new TodoTaskList(_services, "tasks");
        taskList.AddTodoTask(task1, recordUndo: false);
        taskList.AddTodoTask(task2, recordUndo: false);

        var sortOrder1 = GetSortOrder(task1.Id);
        var sortOrder2 = GetSortOrder(task2.Id);

        // CheckTasks calls SetStatuses internally
        taskList.CheckTasks([task1.Id, task2.Id]);

        Assert.Equal(sortOrder1, GetSortOrder(task1.Id));
        Assert.Equal(sortOrder2, GetSortOrder(task2.Id));
    }

    [Fact]
    public void GetSortedTasks_StillSortsByStatusOnFreshCall()
    {
        var taskList = new TodoTaskList(_services, "tasks");

        var pending = TodoTask.CreateTodoTask("pending task", "tasks");
        var done = TodoTask.CreateTodoTask("done task", "tasks");
        var inProgress = TodoTask.CreateTodoTask("in-progress task", "tasks");

        taskList.AddTodoTask(pending, recordUndo: false);
        taskList.AddTodoTask(done, recordUndo: false);
        taskList.AddTodoTask(inProgress, recordUndo: false);

        // Set statuses without affecting sort_order
        taskList.SetStatus(done.Id, TaskStatus.Done);
        taskList.SetStatus(inProgress.Id, TaskStatus.InProgress);

        var sorted = taskList.GetSortedTasks();

        // InProgress should come first, then Pending, then Done
        Assert.Equal(TaskStatus.InProgress, sorted[0].Status);
        Assert.Equal(TaskStatus.Pending, sorted[1].Status);
        Assert.Equal(TaskStatus.Done, sorted[2].Status);
    }

    [Fact]
    public void GetSortedTasks_RespectsUserSortOrderWithinStatusGroup()
    {
        var taskList = new TodoTaskList(_services, "tasks");

        var taskA = TodoTask.CreateTodoTask("task A", "tasks");
        var taskB = TodoTask.CreateTodoTask("task B", "tasks");
        var taskC = TodoTask.CreateTodoTask("task C", "tasks");

        taskList.AddTodoTask(taskA, recordUndo: false);  // sort_order 0
        taskList.AddTodoTask(taskB, recordUndo: false);  // sort_order 1
        taskList.AddTodoTask(taskC, recordUndo: false);  // sort_order 2

        // Default display order (sort_order DESC): C, B, A
        var before = taskList.GetSortedTasks();
        Assert.Equal(taskC.Id, before[0].Id);
        Assert.Equal(taskB.Id, before[1].Id);
        Assert.Equal(taskA.Id, before[2].Id);

        // Reorder: move C to the bottom (index 2 in display = lowest sort_order)
        TodoTaskList.ReorderTask(_services, taskC.Id, 2);

        var after = taskList.GetSortedTasks();

        // B should be first (highest sort_order), then A, then C (reordered to bottom)
        Assert.Equal(taskB.Id, after[0].Id);
        Assert.Equal(taskA.Id, after[1].Id);
        Assert.Equal(taskC.Id, after[2].Id);
    }

    [Fact]
    public void RenameTask_DoesNotChangeSortOrder()
    {
        var taskList = new TodoTaskList(_services, "tasks");

        var taskA = TodoTask.CreateTodoTask("task A", "tasks");
        var taskB = TodoTask.CreateTodoTask("task B", "tasks");

        taskList.AddTodoTask(taskA, recordUndo: false);
        taskList.AddTodoTask(taskB, recordUndo: false);

        var sortOrderA = GetSortOrder(taskA.Id);

        // Renaming should NOT bump sort order
        taskList.RenameTask(taskA.Id, "renamed A", recordUndo: false);

        Assert.Equal(sortOrderA, GetSortOrder(taskA.Id));
    }
}
