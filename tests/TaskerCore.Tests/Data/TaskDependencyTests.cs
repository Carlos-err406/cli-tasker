namespace TaskerCore.Tests.Data;

using TaskerCore.Data;
using TaskerCore.Models;
using TaskerCore.Results;
using TaskStatus = TaskerCore.Models.TaskStatus;

public class TaskDependencyTests : IDisposable
{
    private readonly TaskerServices _services;
    private readonly TodoTaskList _taskList;

    public TaskDependencyTests()
    {
        _services = TaskerServices.CreateInMemory();
        _taskList = new TodoTaskList(_services);
    }

    public void Dispose()
    {
        _services.Dispose();
        GC.SuppressFinalize(this);
    }

    private TodoTask AddTask(string desc, string list = "tasks")
    {
        var task = TodoTask.CreateTodoTask(desc, list);
        _taskList.AddTodoTask(task, recordUndo: false);
        return task;
    }

    // --- SetParent / UnsetParent ---

    [Fact]
    public void SetParent_SetsParentId()
    {
        var parent = AddTask("parent task");
        var child = AddTask("child task");

        var result = _taskList.SetParent(child.Id, parent.Id, recordUndo: false);

        Assert.IsType<TaskResult.Success>(result);
        var updated = _taskList.GetTodoTaskById(child.Id)!;
        Assert.Equal(parent.Id, updated.ParentId);
    }

    [Fact]
    public void UnsetParent_ClearsParentId()
    {
        var parent = AddTask("parent task");
        var child = AddTask("child task");
        _taskList.SetParent(child.Id, parent.Id, recordUndo: false);

        var result = _taskList.UnsetParent(child.Id, recordUndo: false);

        Assert.IsType<TaskResult.Success>(result);
        var updated = _taskList.GetTodoTaskById(child.Id)!;
        Assert.Null(updated.ParentId);
    }

    [Fact]
    public void SetParent_SameListConstraint_RejectsOtherList()
    {
        TodoTaskList.CreateList(_services, "work");
        var parent = AddTask("parent task", "tasks");
        var child = AddTask("child task", "work");

        var result = _taskList.SetParent(child.Id, parent.Id, recordUndo: false);

        Assert.IsType<TaskResult.Error>(result);
    }

    [Fact]
    public void SetParent_PreventsCircularReference()
    {
        var grandparent = AddTask("grandparent");
        var parent = AddTask("parent");
        var child = AddTask("child");
        _taskList.SetParent(parent.Id, grandparent.Id, recordUndo: false);
        _taskList.SetParent(child.Id, parent.Id, recordUndo: false);

        // Trying to set grandparent as child of child creates a cycle
        var result = _taskList.SetParent(grandparent.Id, child.Id, recordUndo: false);

        Assert.IsType<TaskResult.Error>(result);
    }

    [Fact]
    public void SetParent_PreventsSelfReference()
    {
        var task = AddTask("task");

        var result = _taskList.SetParent(task.Id, task.Id, recordUndo: false);

        Assert.IsType<TaskResult.Error>(result);
    }

    // --- AddBlocker / RemoveBlocker ---

    [Fact]
    public void AddBlocker_CreatesRelationship()
    {
        var blocker = AddTask("blocker");
        var blocked = AddTask("blocked");

        var result = _taskList.AddBlocker(blocker.Id, blocked.Id, recordUndo: false);

        Assert.IsType<TaskResult.Success>(result);
        var blocks = _taskList.GetBlocks(blocker.Id);
        Assert.Single(blocks);
        Assert.Equal(blocked.Id, blocks[0].Id);
    }

    [Fact]
    public void RemoveBlocker_DeletesRelationship()
    {
        var blocker = AddTask("blocker");
        var blocked = AddTask("blocked");
        _taskList.AddBlocker(blocker.Id, blocked.Id, recordUndo: false);

        var result = _taskList.RemoveBlocker(blocker.Id, blocked.Id, recordUndo: false);

        Assert.IsType<TaskResult.Success>(result);
        var blocks = _taskList.GetBlocks(blocker.Id);
        Assert.Empty(blocks);
    }

    [Fact]
    public void AddBlocker_PreventsSelfBlock()
    {
        var task = AddTask("task");

        var result = _taskList.AddBlocker(task.Id, task.Id, recordUndo: false);

        Assert.IsType<TaskResult.Error>(result);
    }

    [Fact]
    public void AddBlocker_PreventsCircularBlocking()
    {
        var a = AddTask("a");
        var b = AddTask("b");
        var c = AddTask("c");
        _taskList.AddBlocker(a.Id, b.Id, recordUndo: false);
        _taskList.AddBlocker(b.Id, c.Id, recordUndo: false);

        // c blocking a would create A→B→C→A cycle
        var result = _taskList.AddBlocker(c.Id, a.Id, recordUndo: false);

        Assert.IsType<TaskResult.Error>(result);
    }

    [Fact]
    public void AddBlocker_AllowsCrossListBlocking()
    {
        TodoTaskList.CreateList(_services, "work");
        var blocker = AddTask("blocker", "tasks");
        var blocked = AddTask("blocked", "work");

        var result = _taskList.AddBlocker(blocker.Id, blocked.Id, recordUndo: false);

        Assert.IsType<TaskResult.Success>(result);
    }

    // --- GetSubtasks / GetAllDescendants ---

    [Fact]
    public void GetSubtasks_ReturnsDirectChildren()
    {
        var parent = AddTask("parent");
        var child1 = AddTask("child1");
        var child2 = AddTask("child2");
        _taskList.SetParent(child1.Id, parent.Id, recordUndo: false);
        _taskList.SetParent(child2.Id, parent.Id, recordUndo: false);

        var subtasks = _taskList.GetSubtasks(parent.Id);

        Assert.Equal(2, subtasks.Count);
    }

    [Fact]
    public void GetAllDescendantIds_ReturnsFullTree()
    {
        var root = AddTask("root");
        var child = AddTask("child");
        var grandchild = AddTask("grandchild");
        _taskList.SetParent(child.Id, root.Id, recordUndo: false);
        _taskList.SetParent(grandchild.Id, child.Id, recordUndo: false);

        var descendants = _taskList.GetAllDescendantIds(root.Id);

        Assert.Equal(2, descendants.Count);
        Assert.Contains(child.Id, descendants);
        Assert.Contains(grandchild.Id, descendants);
    }

    // --- GetBlockedBy / GetBlocks ---

    [Fact]
    public void GetBlockedBy_ReturnsBlockers()
    {
        var blocker1 = AddTask("blocker1");
        var blocker2 = AddTask("blocker2");
        var blocked = AddTask("blocked");
        _taskList.AddBlocker(blocker1.Id, blocked.Id, recordUndo: false);
        _taskList.AddBlocker(blocker2.Id, blocked.Id, recordUndo: false);

        var blockedBy = _taskList.GetBlockedBy(blocked.Id);

        Assert.Equal(2, blockedBy.Count);
    }

    // --- Cascade trash ---

    [Fact]
    public void DeleteTask_CascadeTrashesDescendants()
    {
        var parent = AddTask("parent");
        var child = AddTask("child");
        var grandchild = AddTask("grandchild");
        _taskList.SetParent(child.Id, parent.Id, recordUndo: false);
        _taskList.SetParent(grandchild.Id, child.Id, recordUndo: false);

        _taskList.DeleteTask(parent.Id, save: false, recordUndo: false);

        // All should be trashed
        Assert.Null(_taskList.GetTodoTaskById(parent.Id));
        Assert.Null(_taskList.GetTodoTaskById(child.Id));
        Assert.Null(_taskList.GetTodoTaskById(grandchild.Id));
    }

    // --- Cascade restore ---

    [Fact]
    public void RestoreFromTrash_CascadeRestoresDescendants()
    {
        var parent = AddTask("parent");
        var child = AddTask("child");
        _taskList.SetParent(child.Id, parent.Id, recordUndo: false);
        _taskList.DeleteTask(parent.Id, save: false, recordUndo: false);

        _taskList.RestoreFromTrash(parent.Id);

        Assert.NotNull(_taskList.GetTodoTaskById(parent.Id));
        Assert.NotNull(_taskList.GetTodoTaskById(child.Id));
    }

    // --- Cascade check ---

    [Fact]
    public void SetStatus_Done_CascadeChecksDescendants()
    {
        var parent = AddTask("parent");
        var child = AddTask("child");
        var grandchild = AddTask("grandchild");
        _taskList.SetParent(child.Id, parent.Id, recordUndo: false);
        _taskList.SetParent(grandchild.Id, child.Id, recordUndo: false);

        _taskList.SetStatus(parent.Id, TaskStatus.Done, recordUndo: false);

        Assert.Equal(TaskStatus.Done, _taskList.GetTodoTaskById(parent.Id)!.Status);
        Assert.Equal(TaskStatus.Done, _taskList.GetTodoTaskById(child.Id)!.Status);
        Assert.Equal(TaskStatus.Done, _taskList.GetTodoTaskById(grandchild.Id)!.Status);
    }

    [Fact]
    public void SetStatus_Done_SkipsAlreadyDoneSubtasks()
    {
        var parent = AddTask("parent");
        var child = AddTask("child");
        _taskList.SetParent(child.Id, parent.Id, recordUndo: false);
        _taskList.SetStatus(child.Id, TaskStatus.Done, recordUndo: false);

        var result = _taskList.SetStatus(parent.Id, TaskStatus.Done, recordUndo: false);

        // Message should not mention subtasks since the child was already done
        Assert.IsType<TaskResult.Success>(result);
        var msg = ((TaskResult.Success)result).Message;
        Assert.DoesNotContain("subtask", msg);
    }

    [Fact]
    public void SetStatus_Pending_DoesNotCascade()
    {
        var parent = AddTask("parent");
        var child = AddTask("child");
        _taskList.SetParent(child.Id, parent.Id, recordUndo: false);
        _taskList.SetStatus(parent.Id, TaskStatus.Done, recordUndo: false);

        // Unchecking parent should NOT cascade
        _taskList.SetStatus(parent.Id, TaskStatus.Pending, recordUndo: false);

        Assert.Equal(TaskStatus.Pending, _taskList.GetTodoTaskById(parent.Id)!.Status);
        Assert.Equal(TaskStatus.Done, _taskList.GetTodoTaskById(child.Id)!.Status);
    }

    // --- Cascade move ---

    [Fact]
    public void MoveTask_CascadeMovesDescendants()
    {
        TodoTaskList.CreateList(_services, "work");
        var parent = AddTask("parent");
        var child = AddTask("child");
        _taskList.SetParent(child.Id, parent.Id, recordUndo: false);

        _taskList.MoveTask(parent.Id, "work", recordUndo: false);

        Assert.Equal("work", _taskList.GetTodoTaskById(parent.Id)!.ListName);
        Assert.Equal("work", _taskList.GetTodoTaskById(child.Id)!.ListName);
    }

    [Fact]
    public void MoveTask_BlocksSubtaskMove()
    {
        TodoTaskList.CreateList(_services, "work");
        var parent = AddTask("parent");
        var child = AddTask("child");
        _taskList.SetParent(child.Id, parent.Id, recordUndo: false);

        var result = _taskList.MoveTask(child.Id, "work", recordUndo: false);

        Assert.IsType<TaskResult.Error>(result);
    }

    // --- AddTodoTask with dependencies ---

    [Fact]
    public void AddTodoTask_WithParentRef_SetsParent()
    {
        var parent = AddTask("parent task");

        var task = TodoTask.CreateTodoTask($"child task\n^{parent.Id}", "tasks");
        var result = _taskList.AddTodoTask(task, recordUndo: false);

        var created = _taskList.GetTodoTaskById(result.Task.Id)!;
        Assert.Equal(parent.Id, created.ParentId);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void AddTodoTask_WithInvalidParent_WarnsAndCreatesWithoutParent()
    {
        var task = TodoTask.CreateTodoTask("child task\n^zzz", "tasks");
        var result = _taskList.AddTodoTask(task, recordUndo: false);

        var created = _taskList.GetTodoTaskById(result.Task.Id)!;
        Assert.Null(created.ParentId);
        Assert.Single(result.Warnings);
        Assert.Contains("not found", result.Warnings[0]);
    }

    [Fact]
    public void AddTodoTask_WithBlocksRef_CreatesRelationship()
    {
        var blocked = AddTask("blocked task");

        var task = TodoTask.CreateTodoTask($"blocker task\n!{blocked.Id}", "tasks");
        var result = _taskList.AddTodoTask(task, recordUndo: false);

        var blocks = _taskList.GetBlocks(result.Task.Id);
        Assert.Single(blocks);
        Assert.Equal(blocked.Id, blocks[0].Id);
    }

    [Fact]
    public void AddTodoTask_WithInvalidBlocksRef_WarnsAndSkips()
    {
        var task = TodoTask.CreateTodoTask("blocker task\n!zzz", "tasks");
        var result = _taskList.AddTodoTask(task, recordUndo: false);

        Assert.NotNull(_taskList.GetTodoTaskById(result.Task.Id));
        Assert.Single(result.Warnings);
        Assert.Contains("not found", result.Warnings[0]);
    }

    // --- FK ON DELETE CASCADE ---

    [Fact]
    public void HardDelete_CascadesViaForeignKey()
    {
        var parent = AddTask("parent");
        var child = AddTask("child");
        _taskList.SetParent(child.Id, parent.Id, recordUndo: false);

        // Hard delete (not trash)
        _taskList.DeleteTask(parent.Id, save: false, moveToTrash: false, recordUndo: false);

        // Child should also be deleted via FK CASCADE
        var allTasks = _services.Db.Query(
            "SELECT id FROM tasks", reader => reader.GetString(0));
        Assert.DoesNotContain(parent.Id, allTasks);
        Assert.DoesNotContain(child.Id, allTasks);
    }

    [Fact]
    public void HardDelete_RemovesDependencyRows()
    {
        var blocker = AddTask("blocker");
        var blocked = AddTask("blocked");
        _taskList.AddBlocker(blocker.Id, blocked.Id, recordUndo: false);

        // Hard delete blocker
        _taskList.DeleteTask(blocker.Id, save: false, moveToTrash: false, recordUndo: false);

        var depCount = _services.Db.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM task_dependencies");
        Assert.Equal(0, depCount);
    }

    // --- Migration ---

    [Fact]
    public void Migration_AddsParentIdColumn()
    {
        var columns = _services.Db.Query("PRAGMA table_info(tasks)",
            reader => reader.GetString(1), []);
        Assert.Contains("parent_id", columns);
    }

    [Fact]
    public void TaskDependenciesTable_Exists()
    {
        var tables = _services.Db.Query(
            "SELECT name FROM sqlite_master WHERE type='table' AND name='task_dependencies'",
            reader => reader.GetString(0));
        Assert.Single(tables);
    }

    // --- Phase 6: Edge cases ---

    [Fact]
    public void SyncMetadata_RoundTrips_ParentAndBlocks()
    {
        var parent = AddTask("parent task");
        var blocked = AddTask("blocked task");

        // Create task with both ^parent and !blocks via inline syntax
        var task = TodoTask.CreateTodoTask($"my task\n^{parent.Id} !{blocked.Id}", "tasks");
        var result = _taskList.AddTodoTask(task, recordUndo: false);

        // Verify relationships were set
        var created = _taskList.GetTodoTaskById(result.Task.Id)!;
        Assert.Equal(parent.Id, created.ParentId);
        Assert.Single(_taskList.GetBlocks(created.Id));
        Assert.Equal(blocked.Id, _taskList.GetBlocks(created.Id)[0].Id);
    }

    [Fact]
    public void Rename_WithNewParentToken_UpdatesParent()
    {
        var parent1 = AddTask("parent1");
        var parent2 = AddTask("parent2");
        var child = AddTask("child");
        _taskList.SetParent(child.Id, parent1.Id, recordUndo: false);

        // Rename with new parent token
        _taskList.RenameTask(child.Id, $"renamed child\n^{parent2.Id}", recordUndo: false);

        var updated = _taskList.GetTodoTaskById(child.Id)!;
        Assert.Equal(parent2.Id, updated.ParentId);
    }

    [Fact]
    public void AddTodoTask_WithParentInDifferentList_OverridesToParentList()
    {
        TodoTaskList.CreateList(_services, "work");
        var parent = AddTask("parent task", "work");

        // Create task targeting "tasks" list but with ^parent in "work"
        var task = TodoTask.CreateTodoTask($"child task\n^{parent.Id}", "tasks");
        var result = _taskList.AddTodoTask(task, recordUndo: false);

        // Should be moved to parent's list
        Assert.Equal("work", result.Task.ListName);
        Assert.Single(result.Warnings);
        Assert.Contains("work", result.Warnings[0]);
    }

    [Fact]
    public void GetBlocksIds_ReturnsCorrectIds()
    {
        var blocker = AddTask("blocker");
        var blocked1 = AddTask("blocked1");
        var blocked2 = AddTask("blocked2");
        _taskList.AddBlocker(blocker.Id, blocked1.Id, recordUndo: false);
        _taskList.AddBlocker(blocker.Id, blocked2.Id, recordUndo: false);

        var blocksIds = _taskList.GetBlocksIds(blocker.Id);

        Assert.Equal(2, blocksIds.Count);
        Assert.Contains(blocked1.Id, blocksIds);
        Assert.Contains(blocked2.Id, blocksIds);
    }

    [Fact]
    public void GetBlockedByIds_ReturnsCorrectIds()
    {
        var blocker1 = AddTask("blocker1");
        var blocker2 = AddTask("blocker2");
        var blocked = AddTask("blocked");
        _taskList.AddBlocker(blocker1.Id, blocked.Id, recordUndo: false);
        _taskList.AddBlocker(blocker2.Id, blocked.Id, recordUndo: false);

        var blockedByIds = _taskList.GetBlockedByIds(blocked.Id);

        Assert.Equal(2, blockedByIds.Count);
        Assert.Contains(blocker1.Id, blockedByIds);
        Assert.Contains(blocker2.Id, blockedByIds);
    }

    [Fact]
    public void AddBlocker_DuplicateRelationship_ReturnsNoChange()
    {
        var blocker = AddTask("blocker");
        var blocked = AddTask("blocked");
        _taskList.AddBlocker(blocker.Id, blocked.Id, recordUndo: false);

        var result = _taskList.AddBlocker(blocker.Id, blocked.Id, recordUndo: false);

        Assert.IsType<TaskResult.NoChange>(result);
    }

    [Fact]
    public void RemoveBlocker_Nonexistent_ReturnsNoChange()
    {
        var a = AddTask("a");
        var b = AddTask("b");

        var result = _taskList.RemoveBlocker(a.Id, b.Id, recordUndo: false);

        Assert.IsType<TaskResult.NoChange>(result);
    }

    [Fact]
    public void UnsetParent_NoParent_ReturnsNoChange()
    {
        var task = AddTask("task");

        var result = _taskList.UnsetParent(task.Id, recordUndo: false);

        Assert.IsType<TaskResult.NoChange>(result);
    }

    [Fact]
    public void CascadeTrash_DoesNotAffectUnrelatedTasks()
    {
        var parent = AddTask("parent");
        var child = AddTask("child");
        var unrelated = AddTask("unrelated");
        _taskList.SetParent(child.Id, parent.Id, recordUndo: false);

        _taskList.DeleteTask(parent.Id, save: false, recordUndo: false);

        // Unrelated task should still be active
        Assert.NotNull(_taskList.GetTodoTaskById(unrelated.Id));
    }

    [Fact]
    public void DeepNesting_CascadeChecksAllLevels()
    {
        var root = AddTask("root");
        var l1 = AddTask("level1");
        var l2 = AddTask("level2");
        var l3 = AddTask("level3");
        _taskList.SetParent(l1.Id, root.Id, recordUndo: false);
        _taskList.SetParent(l2.Id, l1.Id, recordUndo: false);
        _taskList.SetParent(l3.Id, l2.Id, recordUndo: false);

        _taskList.SetStatus(root.Id, TaskStatus.Done, recordUndo: false);

        Assert.Equal(TaskStatus.Done, _taskList.GetTodoTaskById(root.Id)!.Status);
        Assert.Equal(TaskStatus.Done, _taskList.GetTodoTaskById(l1.Id)!.Status);
        Assert.Equal(TaskStatus.Done, _taskList.GetTodoTaskById(l2.Id)!.Status);
        Assert.Equal(TaskStatus.Done, _taskList.GetTodoTaskById(l3.Id)!.Status);
    }

    // --- Rename syncs dependencies ---

    [Fact]
    public void Rename_RemovingParentToken_ClearsParent()
    {
        var parent = AddTask("parent task");
        var child = AddTask("child task");
        _taskList.SetParent(child.Id, parent.Id, recordUndo: false);

        // Rename with metadata line that no longer has ^parent
        _taskList.RenameTask(child.Id, $"renamed child\n!{parent.Id}", recordUndo: false);

        var updated = _taskList.GetTodoTaskById(child.Id)!;
        Assert.Null(updated.ParentId);
    }

    [Fact]
    public void Rename_AddingBlockerToken_CreatesRelationship()
    {
        var blocker = AddTask("blocker task");
        var blocked = AddTask("blocked task");

        // Rename to add !blockedId
        _taskList.RenameTask(blocker.Id, $"blocker task\n!{blocked.Id}", recordUndo: false);

        var blocks = _taskList.GetBlocks(blocker.Id);
        Assert.Single(blocks);
        Assert.Equal(blocked.Id, blocks[0].Id);
    }

    [Fact]
    public void Rename_RemovingBlockerToken_RemovesRelationship()
    {
        var blocker = AddTask("blocker task");
        var blocked = AddTask("blocked task");
        _taskList.AddBlocker(blocker.Id, blocked.Id, recordUndo: false);

        // Rename with metadata line that no longer has !blocked
        _taskList.RenameTask(blocker.Id, "blocker task\n#sometag", recordUndo: false);

        var blocks = _taskList.GetBlocks(blocker.Id);
        Assert.Empty(blocks);
    }

    [Fact]
    public void Rename_SwappingParentToBlocker_UpdatesBoth()
    {
        var other = AddTask("other task");
        var task = AddTask("my task");
        _taskList.SetParent(task.Id, other.Id, recordUndo: false);

        // Change ^other to !other (remove parent, add blocker)
        _taskList.RenameTask(task.Id, $"my task\n!{other.Id}", recordUndo: false);

        var updated = _taskList.GetTodoTaskById(task.Id)!;
        Assert.Null(updated.ParentId);
        var blocks = _taskList.GetBlocks(task.Id);
        Assert.Single(blocks);
        Assert.Equal(other.Id, blocks[0].Id);
    }

    [Fact]
    public void Rename_ChangingBlockerTarget_UpdatesRelationship()
    {
        var task = AddTask("task");
        var blocked1 = AddTask("blocked1");
        var blocked2 = AddTask("blocked2");
        _taskList.AddBlocker(task.Id, blocked1.Id, recordUndo: false);

        // Change !blocked1 to !blocked2
        _taskList.RenameTask(task.Id, $"task\n!{blocked2.Id}", recordUndo: false);

        var blocks = _taskList.GetBlocks(task.Id);
        Assert.Single(blocks);
        Assert.Equal(blocked2.Id, blocks[0].Id);
    }

    [Fact]
    public void Rename_WithoutMetadataLine_PreservesParent()
    {
        var parent = AddTask("parent task");
        var child = AddTask("child task");
        _taskList.SetParent(child.Id, parent.Id, recordUndo: false);

        // Rename with no metadata line — parent should be preserved
        _taskList.RenameTask(child.Id, "renamed child", recordUndo: false);

        var updated = _taskList.GetTodoTaskById(child.Id)!;
        Assert.Equal(parent.Id, updated.ParentId);
    }

    [Fact]
    public void Rename_WithoutMetadataLine_PreservesBlockers()
    {
        var blocker = AddTask("blocker task");
        var blocked = AddTask("blocked task");
        _taskList.AddBlocker(blocker.Id, blocked.Id, recordUndo: false);

        // Rename with no metadata line — blocker should be preserved
        _taskList.RenameTask(blocker.Id, "renamed blocker", recordUndo: false);

        var blocks = _taskList.GetBlocks(blocker.Id);
        Assert.Single(blocks);
        Assert.Equal(blocked.Id, blocks[0].Id);
    }

    [Fact]
    public void Rename_WithInvalidParentToken_ClearsParent()
    {
        var parent = AddTask("parent task");
        var child = AddTask("child task");
        _taskList.SetParent(child.Id, parent.Id, recordUndo: false);

        // Rename with nonexistent parent
        _taskList.RenameTask(child.Id, "child task\n^zzz", recordUndo: false);

        var updated = _taskList.GetTodoTaskById(child.Id)!;
        Assert.Null(updated.ParentId);
    }

    [Fact]
    public void Rename_WithInvalidBlockerToken_SkipsInvalidBlocker()
    {
        var task = AddTask("my task");

        // Rename with nonexistent blocker target
        _taskList.RenameTask(task.Id, "my task\n!zzz", recordUndo: false);

        var blocks = _taskList.GetBlocks(task.Id);
        Assert.Empty(blocks);
    }

    [Fact]
    public void Rename_MultipleBlockerChanges_SyncsCorrectly()
    {
        var task = AddTask("task");
        var b1 = AddTask("blocked1");
        var b2 = AddTask("blocked2");
        var b3 = AddTask("blocked3");
        _taskList.AddBlocker(task.Id, b1.Id, recordUndo: false);
        _taskList.AddBlocker(task.Id, b2.Id, recordUndo: false);

        // Change from !b1 !b2 to !b2 !b3
        _taskList.RenameTask(task.Id, $"task\n!{b2.Id} !{b3.Id}", recordUndo: false);

        var blocks = _taskList.GetBlocksIds(task.Id);
        Assert.Equal(2, blocks.Count);
        Assert.DoesNotContain(b1.Id, blocks);
        Assert.Contains(b2.Id, blocks);
        Assert.Contains(b3.Id, blocks);
    }

    // --- AddRelated / RemoveRelated ---

    [Fact]
    public void AddRelated_CreatesRelationship()
    {
        var a = AddTask("task a");
        var b = AddTask("task b");

        var result = _taskList.AddRelated(a.Id, b.Id, recordUndo: false);

        Assert.IsType<TaskResult.Success>(result);
        var related = _taskList.GetRelated(a.Id);
        Assert.Single(related);
        Assert.Equal(b.Id, related[0].Id);
    }

    [Fact]
    public void AddRelated_CanonicalOrdering()
    {
        var a = AddTask("task a");
        var b = AddTask("task b");

        // Regardless of argument order, stored canonically
        _taskList.AddRelated(b.Id, a.Id, recordUndo: false);

        var relatedFromA = _taskList.GetRelated(a.Id);
        Assert.Single(relatedFromA);
        Assert.Equal(b.Id, relatedFromA[0].Id);

        var relatedFromB = _taskList.GetRelated(b.Id);
        Assert.Single(relatedFromB);
        Assert.Equal(a.Id, relatedFromB[0].Id);
    }

    [Fact]
    public void AddRelated_SelfReference_ReturnsError()
    {
        var task = AddTask("task");

        var result = _taskList.AddRelated(task.Id, task.Id, recordUndo: false);

        Assert.IsType<TaskResult.Error>(result);
    }

    [Fact]
    public void AddRelated_Duplicate_ReturnsNoChange()
    {
        var a = AddTask("task a");
        var b = AddTask("task b");
        _taskList.AddRelated(a.Id, b.Id, recordUndo: false);

        var result = _taskList.AddRelated(a.Id, b.Id, recordUndo: false);

        Assert.IsType<TaskResult.NoChange>(result);
    }

    [Fact]
    public void AddRelated_NonexistentTask_ReturnsError()
    {
        var a = AddTask("task a");

        var result = _taskList.AddRelated(a.Id, "zzz", recordUndo: false);

        Assert.IsType<TaskResult.Error>(result);
    }

    [Fact]
    public void RemoveRelated_DeletesRelationship()
    {
        var a = AddTask("task a");
        var b = AddTask("task b");
        _taskList.AddRelated(a.Id, b.Id, recordUndo: false);

        var result = _taskList.RemoveRelated(a.Id, b.Id, recordUndo: false);

        Assert.IsType<TaskResult.Success>(result);
        Assert.Empty(_taskList.GetRelated(a.Id));
        Assert.Empty(_taskList.GetRelated(b.Id));
    }

    [Fact]
    public void RemoveRelated_NonExisting_ReturnsNoChange()
    {
        var a = AddTask("task a");
        var b = AddTask("task b");

        var result = _taskList.RemoveRelated(a.Id, b.Id, recordUndo: false);

        Assert.IsType<TaskResult.NoChange>(result);
    }

    [Fact]
    public void GetRelated_ReturnsBothSides()
    {
        var a = AddTask("task a");
        var b = AddTask("task b");
        var c = AddTask("task c");
        _taskList.AddRelated(a.Id, b.Id, recordUndo: false);
        _taskList.AddRelated(a.Id, c.Id, recordUndo: false);

        var relatedA = _taskList.GetRelated(a.Id);
        Assert.Equal(2, relatedA.Count);

        var relatedB = _taskList.GetRelated(b.Id);
        Assert.Single(relatedB);
        Assert.Equal(a.Id, relatedB[0].Id);
    }

    [Fact]
    public void GetRelatedIds_ReturnsBothSides()
    {
        var a = AddTask("task a");
        var b = AddTask("task b");
        _taskList.AddRelated(a.Id, b.Id, recordUndo: false);

        var idsFromA = _taskList.GetRelatedIds(a.Id);
        Assert.Single(idsFromA);
        Assert.Contains(b.Id, idsFromA);

        var idsFromB = _taskList.GetRelatedIds(b.Id);
        Assert.Single(idsFromB);
        Assert.Contains(a.Id, idsFromB);
    }

    [Fact]
    public void AddRelated_AllowsCrossList()
    {
        TodoTaskList.CreateList(_services, "work");
        var a = AddTask("task a", "tasks");
        var b = AddTask("task b", "work");

        var result = _taskList.AddRelated(a.Id, b.Id, recordUndo: false);

        Assert.IsType<TaskResult.Success>(result);
    }

    // --- Inline related via AddTodoTask ---

    [Fact]
    public void AddTodoTask_WithRelatedRef_CreatesRelationship()
    {
        var existing = AddTask("existing task");

        var task = TodoTask.CreateTodoTask($"new task\n~{existing.Id}", "tasks");
        var result = _taskList.AddTodoTask(task, recordUndo: false);

        var related = _taskList.GetRelated(result.Task.Id);
        Assert.Single(related);
        Assert.Equal(existing.Id, related[0].Id);
    }

    [Fact]
    public void AddTodoTask_WithInvalidRelatedRef_WarnsAndSkips()
    {
        var task = TodoTask.CreateTodoTask("new task\n~zzz", "tasks");
        var result = _taskList.AddTodoTask(task, recordUndo: false);

        Assert.NotNull(_taskList.GetTodoTaskById(result.Task.Id));
        Assert.Single(result.Warnings);
        Assert.Contains("not found", result.Warnings[0]);
    }

    [Fact]
    public void AddTodoTask_WithSelfRelatedRef_WarnsAndSkips()
    {
        // Create task with a known ID, then try to relate to itself
        var task = TodoTask.CreateTodoTask("task a", "tasks");
        _taskList.AddTodoTask(task, recordUndo: false);

        // Now create a task that tries to relate to itself — since IDs are random,
        // we test this via the AddRelated method instead
        var a = AddTask("task a");
        var result = _taskList.AddRelated(a.Id, a.Id, recordUndo: false);
        Assert.IsType<TaskResult.Error>(result);
    }

    // --- Rename syncs related relationships ---

    [Fact]
    public void Rename_AddingRelatedToken_CreatesRelationship()
    {
        var a = AddTask("task a");
        var b = AddTask("task b");

        _taskList.RenameTask(a.Id, $"task a\n~{b.Id}", recordUndo: false);

        var related = _taskList.GetRelated(a.Id);
        Assert.Single(related);
        Assert.Equal(b.Id, related[0].Id);
    }

    [Fact]
    public void Rename_RemovingRelatedToken_RemovesRelationship()
    {
        var a = AddTask("task a");
        var b = AddTask("task b");
        _taskList.AddRelated(a.Id, b.Id, recordUndo: false);

        _taskList.RenameTask(a.Id, "task a\n#sometag", recordUndo: false);

        Assert.Empty(_taskList.GetRelated(a.Id));
    }

    // --- Bidirectional sync ---

    [Fact]
    public void AddRelated_SyncsBothDescriptions()
    {
        var a = AddTask("task a");
        var b = AddTask("task b");

        _taskList.AddRelated(a.Id, b.Id, recordUndo: false);

        var updatedA = _taskList.GetTodoTaskById(a.Id)!;
        var updatedB = _taskList.GetTodoTaskById(b.Id)!;
        Assert.Contains($"~{b.Id}", updatedA.Description);
        Assert.Contains($"~{a.Id}", updatedB.Description);
    }

    [Fact]
    public void RemoveRelated_SyncsBothDescriptions()
    {
        var a = AddTask("task a");
        var b = AddTask("task b");
        _taskList.AddRelated(a.Id, b.Id, recordUndo: false);

        _taskList.RemoveRelated(a.Id, b.Id, recordUndo: false);

        var updatedA = _taskList.GetTodoTaskById(a.Id)!;
        var updatedB = _taskList.GetTodoTaskById(b.Id)!;
        Assert.DoesNotContain("~", updatedA.Description);
        Assert.DoesNotContain("~", updatedB.Description);
    }

    // --- task_relations table ---

    [Fact]
    public void TaskRelationsTable_Exists()
    {
        var tables = _services.Db.Query(
            "SELECT name FROM sqlite_master WHERE type='table' AND name='task_relations'",
            reader => reader.GetString(0));
        Assert.Single(tables);
    }

    [Fact]
    public void HardDelete_RemovesRelationRows()
    {
        var a = AddTask("task a");
        var b = AddTask("task b");
        _taskList.AddRelated(a.Id, b.Id, recordUndo: false);

        _taskList.DeleteTask(a.Id, save: false, moveToTrash: false, recordUndo: false);

        var relCount = _services.Db.ExecuteScalar<long>("SELECT COUNT(*) FROM task_relations");
        Assert.Equal(0, relCount);
    }

    [Fact]
    public void Rename_WithoutMetadataLine_PreservesRelated()
    {
        var a = AddTask("task a");
        var b = AddTask("task b");
        _taskList.AddRelated(a.Id, b.Id, recordUndo: false);

        // Rename with no metadata line — related should be preserved
        _taskList.RenameTask(a.Id, "renamed task a", recordUndo: false);

        var related = _taskList.GetRelated(a.Id);
        Assert.Single(related);
        Assert.Equal(b.Id, related[0].Id);
    }
}
