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
[JsonDerivedType(typeof(SetParentCommand), "set-parent")]
[JsonDerivedType(typeof(AddBlockerCommand), "add-blocker")]
[JsonDerivedType(typeof(RemoveBlockerCommand), "remove-blocker")]
[JsonDerivedType(typeof(AddRelatedCommand), "add-related")]
[JsonDerivedType(typeof(RemoveRelatedCommand), "remove-related")]
public interface IUndoableCommand
{
    string Description { get; }
    DateTime ExecutedAt { get; }
    void Execute();
    void Undo();
}
