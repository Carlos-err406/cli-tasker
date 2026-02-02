namespace TaskerCore.Undo;

using System.Text.Json.Serialization;
using TaskerCore.Undo.Commands;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(AddTaskCommand), "add")]
[JsonDerivedType(typeof(DeleteTaskCommand), "delete")]
[JsonDerivedType(typeof(CheckTaskCommand), "check")]
[JsonDerivedType(typeof(UncheckTaskCommand), "uncheck")]
[JsonDerivedType(typeof(RenameTaskCommand), "rename")]
[JsonDerivedType(typeof(MoveTaskCommand), "move")]
[JsonDerivedType(typeof(ClearTasksCommand), "clear")]
[JsonDerivedType(typeof(CompositeCommand), "batch")]
public interface IUndoableCommand
{
    string Description { get; }
    DateTime ExecutedAt { get; }
    void Execute();
    void Undo();
}
