namespace TaskerCore.Undo;

using System.Text.Json.Serialization;
using TaskerCore.Undo.Commands;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(AddTaskCommand), "add")]
[JsonDerivedType(typeof(DeleteTaskCommand), "delete")]
[JsonDerivedType(typeof(SetStatusCommand), "set-status")]
[JsonDerivedType(typeof(RenameTaskCommand), "rename")]
[JsonDerivedType(typeof(MoveTaskCommand), "move")]
[JsonDerivedType(typeof(ClearTasksCommand), "clear")]
[JsonDerivedType(typeof(CompositeCommand), "batch")]
[JsonDerivedType(typeof(TaskMetadataChangedCommand), "metadata")]
[JsonDerivedType(typeof(RenameListCommand), "renameList")]
[JsonDerivedType(typeof(ReorderTaskCommand), "reorderTask")]
[JsonDerivedType(typeof(ReorderListCommand), "reorderList")]
[JsonDerivedType(typeof(DeleteListCommand), "deleteList")]
public interface IUndoableCommand
{
    string Description { get; }
    DateTime ExecutedAt { get; }
    void Execute();
    void Undo();
}
