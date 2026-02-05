namespace TaskerCore.Tests.Undo;

using System.Text.Json;
using TaskerCore.Models;
using TaskerCore.Undo;
using TaskerCore.Undo.Commands;

public class UndoSerializationTest
{
    private static JsonSerializerOptions GetJsonOptions() => new()
    {
        WriteIndented = true
    };

    [Fact]
    public void AddTaskCommand_SerializesWithTypeDiscriminator()
    {
        var task = TodoTask.CreateTodoTask("test", "tasks");
        var cmd = new AddTaskCommand { Task = task };

        var history = new UndoHistory
        {
            UndoStack = [cmd],
            RedoStack = [],
            TasksChecksum = "test",
            TasksFileSize = 100
        };

        var json = JsonSerializer.Serialize(history, GetJsonOptions());

        Assert.Contains("\"$type\": \"add\"", json);
        Assert.Contains("\"Task\":", json);
    }

    [Fact]
    public void AddTaskCommand_DeserializesCorrectly()
    {
        var task = TodoTask.CreateTodoTask("test", "tasks");
        var cmd = new AddTaskCommand { Task = task };

        var history = new UndoHistory
        {
            UndoStack = [cmd],
            RedoStack = [],
            TasksChecksum = "test",
            TasksFileSize = 100
        };

        var json = JsonSerializer.Serialize(history, GetJsonOptions());
        var restored = JsonSerializer.Deserialize<UndoHistory>(json, GetJsonOptions());

        Assert.NotNull(restored);
        Assert.Single(restored.UndoStack);
        Assert.IsType<AddTaskCommand>(restored.UndoStack[0]);

        var restoredCmd = (AddTaskCommand)restored.UndoStack[0];
        Assert.Equal(task.Id, restoredCmd.Task.Id);
    }

    [Fact]
    public void RenameListCommand_SerializesWithTypeDiscriminator()
    {
        var cmd = new RenameListCommand
        {
            OldName = "old",
            NewName = "new",
            WasDefaultList = true
        };

        var history = new UndoHistory
        {
            UndoStack = [cmd],
            RedoStack = [],
            TasksChecksum = "test",
            TasksFileSize = 100
        };

        var json = JsonSerializer.Serialize(history, GetJsonOptions());

        Assert.Contains("\"$type\": \"renameList\"", json);
        Assert.Contains("\"OldName\": \"old\"", json);
    }

    [Fact]
    public void RenameListCommand_DeserializesCorrectly()
    {
        var cmd = new RenameListCommand
        {
            OldName = "old",
            NewName = "new",
            WasDefaultList = true
        };

        var history = new UndoHistory
        {
            UndoStack = [cmd],
            RedoStack = [],
            TasksChecksum = "test",
            TasksFileSize = 100
        };

        var json = JsonSerializer.Serialize(history, GetJsonOptions());
        var restored = JsonSerializer.Deserialize<UndoHistory>(json, GetJsonOptions());

        Assert.NotNull(restored);
        Assert.Single(restored.UndoStack);
        Assert.IsType<RenameListCommand>(restored.UndoStack[0]);

        var restoredCmd = (RenameListCommand)restored.UndoStack[0];
        Assert.Equal("old", restoredCmd.OldName);
        Assert.Equal("new", restoredCmd.NewName);
        Assert.True(restoredCmd.WasDefaultList);
    }

    [Fact]
    public void MultipleCommands_SerializeAndDeserialize()
    {
        var task = TodoTask.CreateTodoTask("test", "tasks");
        var addCmd = new AddTaskCommand { Task = task };
        var deleteCmd = new DeleteTaskCommand { DeletedTask = task };
        var renameListCmd = new RenameListCommand
        {
            OldName = "old",
            NewName = "new",
            WasDefaultList = false
        };

        var history = new UndoHistory
        {
            UndoStack = [addCmd, deleteCmd, renameListCmd],
            RedoStack = [],
            TasksChecksum = "test",
            TasksFileSize = 100
        };

        var json = JsonSerializer.Serialize(history, GetJsonOptions());
        var restored = JsonSerializer.Deserialize<UndoHistory>(json, GetJsonOptions());

        Assert.NotNull(restored);
        Assert.Equal(3, restored.UndoStack.Count);
        Assert.IsType<AddTaskCommand>(restored.UndoStack[0]);
        Assert.IsType<DeleteTaskCommand>(restored.UndoStack[1]);
        Assert.IsType<RenameListCommand>(restored.UndoStack[2]);
    }
}
