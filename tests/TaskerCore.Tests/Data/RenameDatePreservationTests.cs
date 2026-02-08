namespace TaskerCore.Tests.Data;

using TaskerCore.Data;
using TaskerCore.Models;

public class RenameDatePreservationTests : IDisposable
{
    private readonly TaskerServices _services;
    private readonly TodoTaskList _taskList;

    public RenameDatePreservationTests()
    {
        _services = TaskerServices.CreateInMemory();
        _taskList = new TodoTaskList(_services);
    }

    public void Dispose()
    {
        _services.Dispose();
        GC.SuppressFinalize(this);
    }

    private TodoTask AddTask(string desc)
    {
        var task = TodoTask.CreateTodoTask(desc, "tasks");
        _taskList.AddTodoTask(task, recordUndo: false);
        return task;
    }

    [Fact]
    public void Rename_SameDateMarker_PreservesDueDate()
    {
        // Simulate: create with @today (resolves to today's date)
        var task = AddTask("buy milk\n@today");
        var original = _taskList.GetTodoTaskById(task.Id)!;
        Assert.Equal(DateOnly.FromDateTime(DateTime.Today), original.DueDate);

        // Simulate time passing by directly updating the DB due date to yesterday
        // while leaving the description text with @today unchanged
        var yesterday = new DateOnly(2026, 1, 15);
        _services.Db.Execute("UPDATE tasks SET due_date = @due WHERE id = @id",
            ("@due", yesterday.ToString("yyyy-MM-dd")), ("@id", task.Id));

        // Rename text without changing @today marker
        _taskList.RenameTask(task.Id, "buy almond milk\n@today", recordUndo: false);
        var renamed = _taskList.GetTodoTaskById(task.Id)!;

        // Due date should be preserved (2026-01-15), NOT re-evaluated to today
        Assert.Equal(yesterday, renamed.DueDate);
    }

    [Fact]
    public void Rename_DifferentDateMarker_ReEvaluates()
    {
        var task = AddTask("buy milk\n@today");

        // Change date marker from @today to @tomorrow
        _taskList.RenameTask(task.Id, "buy milk\n@tomorrow", recordUndo: false);
        var renamed = _taskList.GetTodoTaskById(task.Id)!;

        // Should re-evaluate — @tomorrow resolves to tomorrow
        var tomorrow = DateOnly.FromDateTime(DateTime.Today.AddDays(1));
        Assert.Equal(tomorrow, renamed.DueDate);
    }

    [Fact]
    public void Rename_AddDateMarker_SetsDueDate()
    {
        var task = AddTask("buy milk");
        var original = _taskList.GetTodoTaskById(task.Id)!;
        Assert.Null(original.DueDate);

        // Add a date marker where there wasn't one
        _taskList.RenameTask(task.Id, "buy milk\n@today", recordUndo: false);
        var renamed = _taskList.GetTodoTaskById(task.Id)!;

        Assert.Equal(DateOnly.FromDateTime(DateTime.Today), renamed.DueDate);
    }

    [Fact]
    public void Rename_RemoveDateMarker_ClearsDueDate()
    {
        var task = AddTask("buy milk\n@today");
        var original = _taskList.GetTodoTaskById(task.Id)!;
        Assert.NotNull(original.DueDate);

        // Remove the metadata line entirely (just plain text, no metadata)
        _taskList.RenameTask(task.Id, "buy milk", recordUndo: false);
        var renamed = _taskList.GetTodoTaskById(task.Id)!;

        // Due date should be preserved since no metadata line means "don't change metadata"
        // (same behavior as ParentId: preserve when no metadata line)
        // Actually — Rename() without oldParsed having metadata: newParsed.DueDateRaw == null == oldParsed.DueDateRaw?
        // No: oldParsed.DueDateRaw is "today", newParsed.DueDateRaw is null → they differ → re-evaluate (null)
        Assert.Null(renamed.DueDate);
    }

    [Fact]
    public void Rename_NoMetadataLine_PreservesDueDate()
    {
        // Task with no metadata line, due date set directly in DB
        var task = AddTask("buy milk");
        // Set due date directly in DB without syncing to description
        _services.Db.Execute("UPDATE tasks SET due_date = @due WHERE id = @id",
            ("@due", "2026-03-15"), ("@id", task.Id));

        // Rename text — no metadata line in either old or new
        _taskList.RenameTask(task.Id, "buy almond milk", recordUndo: false);
        var renamed = _taskList.GetTodoTaskById(task.Id)!;

        // Due date preserved since both old and new have null DueDateRaw
        Assert.Equal(new DateOnly(2026, 3, 15), renamed.DueDate);
    }

    [Fact]
    public void Rename_SameAbsoluteDateMarker_Preserved()
    {
        var task = AddTask("buy milk\n@2026-02-07");
        var original = _taskList.GetTodoTaskById(task.Id)!;
        Assert.Equal(new DateOnly(2026, 2, 7), original.DueDate);

        // Rename with same absolute date marker
        _taskList.RenameTask(task.Id, "buy almond milk\n@2026-02-07", recordUndo: false);
        var renamed = _taskList.GetTodoTaskById(task.Id)!;

        Assert.Equal(new DateOnly(2026, 2, 7), renamed.DueDate);
    }
}
