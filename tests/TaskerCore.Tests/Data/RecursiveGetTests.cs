namespace TaskerCore.Tests.Data;

using System.Text.Json;
using cli_tasker;
using TaskerCore.Data;
using TaskerCore.Models;
using TaskStatus = TaskerCore.Models.TaskStatus;

public class RecursiveGetTests : IDisposable
{
    private readonly TaskerServices _services;
    private readonly TodoTaskList _taskList;

    public RecursiveGetTests()
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

    [Fact]
    public void BuildJsonTree_NoRelationships_ReturnsTaskOnly()
    {
        var task = AddTask("standalone task");

        var visited = new HashSet<string>();
        var result = GetCommand.BuildJsonTree(task, _taskList, visited);

        Assert.Equal(task.Id, result["id"]);
        Assert.Equal("standalone task", result["description"]);
        Assert.Equal("pending", result["status"]);
        Assert.Null(result["parent"]);
        Assert.Empty((object[])result["subtasks"]!);
        Assert.Empty((object[])result["blocks"]!);
        Assert.Empty((object[])result["blockedBy"]!);
        Assert.Empty((object[])result["related"]!);
    }

    [Fact]
    public void BuildJsonTree_WithSubtask_RecursesIntoChild()
    {
        var parent = AddTask("parent task");
        var child = AddTask("child task");
        _taskList.SetParent(child.Id, parent.Id, recordUndo: false);

        // Re-fetch parent to get updated description with -^ marker
        var updatedParent = _taskList.GetTodoTaskById(parent.Id)!;

        var visited = new HashSet<string>();
        var result = GetCommand.BuildJsonTree(updatedParent, _taskList, visited);

        var subtasks = (object[])result["subtasks"]!;
        Assert.Single(subtasks);

        var childNode = (Dictionary<string, object?>)subtasks[0];
        Assert.Equal(child.Id, childNode["id"]);
        Assert.Contains("child task", (string)childNode["description"]!);
    }

    [Fact]
    public void BuildJsonTree_CycleDetection_ShowsRefForVisitedTask()
    {
        var taskA = AddTask("task A");
        var taskB = AddTask("task B");

        // Create bidirectional related relationship
        _taskList.AddRelated(taskA.Id, taskB.Id, recordUndo: false);

        var updatedA = _taskList.GetTodoTaskById(taskA.Id)!;

        var visited = new HashSet<string>();
        var result = GetCommand.BuildJsonTree(updatedA, _taskList, visited);

        // A's related should contain B
        var related = (object[])result["related"]!;
        Assert.Single(related);

        var bNode = (Dictionary<string, object?>)related[0];
        Assert.Equal(taskB.Id, bNode["id"]);

        // B's related should contain A as a $ref (already visited)
        var bRelated = (object[])bNode["related"]!;
        Assert.Single(bRelated);

        var aRef = (Dictionary<string, object?>)bRelated[0];
        Assert.Equal(taskA.Id, aRef["id"]);
        Assert.Equal(true, aRef["$ref"]);
    }

    [Fact]
    public void BuildJsonTree_MissingTask_ShowsError()
    {
        // Create a task with a blocker reference to a non-existent ID
        var task = AddTask("task with broken ref\n-!zzz");

        var visited = new HashSet<string>();
        var result = GetCommand.BuildJsonTree(task, _taskList, visited);

        var blockedBy = (object[])result["blockedBy"]!;
        Assert.Single(blockedBy);

        var missing = (Dictionary<string, object?>)blockedBy[0];
        Assert.Equal("zzz", missing["id"]);
        Assert.Equal("task not found", missing["error"]);
    }

    [Fact]
    public void BuildJsonTree_ThreeLevelChain_TraversesAll()
    {
        var grandparent = AddTask("grandparent");
        var parent = AddTask("parent");
        var child = AddTask("child");
        _taskList.SetParent(parent.Id, grandparent.Id, recordUndo: false);
        _taskList.SetParent(child.Id, parent.Id, recordUndo: false);

        var updatedGrandparent = _taskList.GetTodoTaskById(grandparent.Id)!;

        var visited = new HashSet<string>();
        var result = GetCommand.BuildJsonTree(updatedGrandparent, _taskList, visited);

        // Grandparent → subtasks → parent → subtasks → child
        var subtasks = (object[])result["subtasks"]!;
        Assert.Single(subtasks);
        var parentNode = (Dictionary<string, object?>)subtasks[0];
        Assert.Equal(parent.Id, parentNode["id"]);

        var parentSubtasks = (object[])parentNode["subtasks"]!;
        Assert.Single(parentSubtasks);
        var childNode = (Dictionary<string, object?>)parentSubtasks[0];
        Assert.Equal(child.Id, childNode["id"]);
    }

    [Fact]
    public void BuildJsonTree_MixedRelationships_IncludesAll()
    {
        var task = AddTask("main task");
        var blocker = AddTask("blocker");
        var related = AddTask("related");
        var subtask = AddTask("subtask");

        _taskList.AddBlocker(blocker.Id, task.Id, recordUndo: false); // blocker blocks task
        _taskList.AddRelated(task.Id, related.Id, recordUndo: false);
        _taskList.SetParent(subtask.Id, task.Id, recordUndo: false);

        var updated = _taskList.GetTodoTaskById(task.Id)!;

        var visited = new HashSet<string>();
        var result = GetCommand.BuildJsonTree(updated, _taskList, visited);

        Assert.Single((object[])result["subtasks"]!);
        Assert.Single((object[])result["blockedBy"]!);
        Assert.Single((object[])result["related"]!);
    }

    [Fact]
    public void BuildJsonTree_SerializesToValidJson()
    {
        var parent = AddTask("parent p2 #work");
        var child = AddTask("child");
        _taskList.SetParent(child.Id, parent.Id, recordUndo: false);

        var updatedParent = _taskList.GetTodoTaskById(parent.Id)!;

        var visited = new HashSet<string>();
        var result = GetCommand.BuildJsonTree(updatedParent, _taskList, visited);
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });

        // Should be valid JSON that can be deserialized
        var parsed = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.Equal(updatedParent.Id, parsed.GetProperty("id").GetString());
        Assert.True(parsed.GetProperty("subtasks").GetArrayLength() > 0);
    }

    [Fact]
    public void FormatStatus_ReturnsCorrectStrings()
    {
        Assert.Equal("done", GetCommand.FormatStatus(TaskStatus.Done));
        Assert.Equal("in-progress", GetCommand.FormatStatus(TaskStatus.InProgress));
        Assert.Equal("pending", GetCommand.FormatStatus(TaskStatus.Pending));
        Assert.Equal("pending", GetCommand.FormatStatus(null));
    }

    [Fact]
    public void BuildJsonTree_IncludesAllTaskFields()
    {
        var task = AddTask("important task\np1 @2026-03-01 #feature #urgent");
        // Re-fetch from DB to get stored fields
        var fetched = _taskList.GetTodoTaskById(task.Id)!;

        var visited = new HashSet<string>();
        var result = GetCommand.BuildJsonTree(fetched, _taskList, visited);

        Assert.Equal(fetched.Id, result["id"]);
        Assert.NotNull(result["status"]);
        Assert.NotNull(result["listName"]);
        Assert.NotNull(result["createdAt"]);
        // Priority, tags, dueDate parsed from metadata line
        Assert.Equal("high", result["priority"]);
        Assert.NotNull(result["tags"]);
        Assert.Equal("2026-03-01", result["dueDate"]);
    }
}
