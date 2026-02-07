using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskerCore;
using TaskerCore.Data;
using TaskerCore.Models;
using TaskerCore.Parsing;
using TaskerCore.Results;
using TaskStatus = TaskerCore.Models.TaskStatus;

namespace TaskerTray.ViewModels;

public partial class TodoTaskViewModel : ObservableObject
{
    private readonly Action<TodoTaskViewModel>? _onChanged;
    private TodoTask _task;

    public string Id => _task.Id;
    public string Description => _task.Description;
    public string ListName => _task.ListName;
    public DateTime CreatedAt => _task.CreatedAt;
    public Priority? Priority => _task.Priority;
    public DateOnly? DueDate => _task.DueDate;
    public string[]? Tags => _task.Tags;
    public bool IsOverdue => _task.IsOverdue;
    public bool IsDueToday => _task.IsDueToday;
    public bool HasDueDate => _task.DueDate.HasValue;
    public bool HasPriority => _task.Priority.HasValue;
    public bool HasTags => _task.HasTags;
    public DateTime? CompletedAt => _task.CompletedAt;
    public string? ParentId => _task.ParentId;
    public bool HasParent => _task.ParentId != null;

    // Relationship display (populated via LoadRelationships)
    public string? ParentDisplay { get; private set; }
    public string[]? SubtasksDisplay { get; private set; }
    public bool HasSubtasks => SubtasksDisplay is { Length: > 0 };
    public string[]? BlocksDisplay { get; private set; }
    public bool HasBlocks => BlocksDisplay is { Length: > 0 };
    public string[]? BlockedByDisplay { get; private set; }
    public bool HasBlockedBy => BlockedByDisplay is { Length: > 0 };
    public string[]? RelatedDisplay { get; private set; }
    public bool HasRelated => RelatedDisplay is { Length: > 0 };
    public bool HasRelationships => HasParent || HasSubtasks || HasBlocks || HasBlockedBy || HasRelated;

    /// <summary>
    /// Relative time display for completed tasks (e.g., "2h ago").
    /// </summary>
    public string? CompletedAtDisplay => CompletedAt.HasValue ? FormatRelativeTime(CompletedAt.Value) : null;

    private static string FormatRelativeTime(DateTime timestamp)
    {
        var span = DateTime.UtcNow - timestamp;
        return span.TotalMinutes < 1 ? "just now"
             : span.TotalHours < 1 ? $"{(int)span.TotalMinutes}m ago"
             : span.TotalDays < 1 ? $"{(int)span.TotalHours}h ago"
             : span.TotalDays < 7 ? $"{(int)span.TotalDays}d ago"
             : timestamp.ToLocalTime().ToString("MMM d");
    }

    [ObservableProperty]
    private bool _isChecked;

    [ObservableProperty]
    private TaskStatus _status;

    /// <summary>
    /// Display text for menu item - first line, truncated.
    /// </summary>
    public string DisplayText => GetDisplayText();

    /// <summary>
    /// Full description for tooltip.
    /// </summary>
    public string FullDescription => _task.Description;

    /// <summary>
    /// Description preview (lines after the first).
    /// </summary>
    public string DescriptionPreview => GetDescriptionPreview();

    /// <summary>
    /// Whether task has additional description beyond title (using display description).
    /// </summary>
    public bool HasDescription => TaskDescriptionParser.GetDisplayDescription(_task.Description).Contains('\n');

    /// <summary>
    /// Menu item text with checkbox indicator.
    /// </summary>
    public string MenuText => Status switch
    {
        TaskStatus.Done => $"[x] {DisplayText}",
        TaskStatus.InProgress => $"[-] {DisplayText}",
        _ => $"[ ] {DisplayText}"
    };

    /// <summary>
    /// Priority display text.
    /// </summary>
    public string PriorityDisplay => Priority switch
    {
        TaskerCore.Models.Priority.High => ">>>",
        TaskerCore.Models.Priority.Medium => ">>",
        TaskerCore.Models.Priority.Low => ">",
        _ => ""
    };

    /// <summary>
    /// Priority color for display.
    /// </summary>
    public IBrush PriorityColor => Priority switch
    {
        TaskerCore.Models.Priority.High => Brushes.Red,
        TaskerCore.Models.Priority.Medium => Brushes.Orange,
        TaskerCore.Models.Priority.Low => Brushes.DodgerBlue,
        _ => Brushes.Transparent
    };

    /// <summary>
    /// Due date display text.
    /// </summary>
    public string DueDateDisplay
    {
        get
        {
            if (!DueDate.HasValue) return "";

            // For completed tasks, freeze the label based on completion time
            if (_task.Status == TaskStatus.Done && CompletedAt.HasValue)
            {
                var completedDate = DateOnly.FromDateTime(CompletedAt.Value.ToLocalTime());
                var lateDays = completedDate.DayNumber - DueDate.Value.DayNumber;
                return lateDays > 0
                    ? $"Completed {lateDays}d late"
                    : $"Due: {DueDate.Value:MMM d}";
            }

            var today = DateOnly.FromDateTime(DateTime.Today);
            var diff = DueDate.Value.DayNumber - today.DayNumber;

            return diff switch
            {
                < 0 => $"OVERDUE ({-diff}d)",
                0 => "Due: Today",
                1 => "Due: Tomorrow",
                < 7 => $"Due: {DueDate.Value:dddd}",
                _ => $"Due: {DueDate.Value:MMM d}"
            };
        }
    }

    /// <summary>
    /// Due date color for display. Gray for completed tasks (even if late).
    /// </summary>
    public IBrush DueDateColor =>
        _task.Status == TaskStatus.Done ? Brushes.Gray
        : IsOverdue ? Brushes.Red
        : IsDueToday ? Brushes.Orange
        : Brushes.Gray;

    /// <summary>
    /// Tags display text.
    /// </summary>
    public string TagsDisplay => HasTags ? string.Join(" ", Tags!.Select(t => $"#{t}")) : "";

    public TodoTaskViewModel(TodoTask task, Action<TodoTaskViewModel>? onChanged = null)
    {
        _task = task;
        _onChanged = onChanged;
        _isChecked = task.Status == TaskStatus.Done;
        _status = task.Status;
    }

    [RelayCommand]
    private void Toggle()
    {
        var taskList = new TodoTaskList();
        // Click = toggle done/pending
        var newStatus = Status == TaskStatus.Done ? TaskStatus.Pending : TaskStatus.Done;
        var result = taskList.SetStatus(Id, newStatus);

        if (result is TaskResult.Success)
        {
            Status = newStatus;
            IsChecked = newStatus == TaskStatus.Done;
            OnPropertyChanged(nameof(MenuText));
            _onChanged?.Invoke(this);
        }
    }

    [RelayCommand]
    private void SetInProgress()
    {
        var taskList = new TodoTaskList();
        var result = taskList.SetStatus(Id, TaskStatus.InProgress);

        if (result is TaskResult.Success)
        {
            Status = TaskStatus.InProgress;
            IsChecked = false;
            OnPropertyChanged(nameof(MenuText));
            _onChanged?.Invoke(this);
        }
    }

    [RelayCommand]
    private void Delete()
    {
        var taskList = new TodoTaskList();
        var result = taskList.DeleteTask(Id);

        if (result is TaskResult.Success)
        {
            _onChanged?.Invoke(this);
        }
    }

    public void MoveToList(string targetList)
    {
        var taskList = new TodoTaskList();
        var result = taskList.MoveTask(Id, targetList);

        if (result is TaskResult.Success)
        {
            _onChanged?.Invoke(this);
        }
    }

    public void Rename(string newDescription)
    {
        var taskList = new TodoTaskList();
        var result = taskList.RenameTask(Id, newDescription);

        if (result is TaskResult.Success)
        {
            _onChanged?.Invoke(this);
        }
    }

    /// <summary>
    /// Loads relationship data for display. Call after construction.
    /// </summary>
    public void LoadRelationships(TodoTaskList taskList)
    {
        var parsed = TaskDescriptionParser.Parse(_task.Description);

        // Parent display with title
        if (parsed.ParentId != null)
        {
            var parent = taskList.GetTodoTaskById(parsed.ParentId);
            var parentTitle = parent != null
                ? TaskDescriptionParser.GetDisplayDescription(parent.Description).Split('\n')[0]
                : "?";
            ParentDisplay = $"Subtask of ({parsed.ParentId}) {parentTitle}";
        }

        // Subtasks with id + title (from -^abc markers)
        if (parsed.HasSubtaskIds is { Length: > 0 })
        {
            SubtasksDisplay = parsed.HasSubtaskIds.Select(subId =>
            {
                var sub = taskList.GetTodoTaskById(subId);
                var title = sub != null
                    ? TaskDescriptionParser.GetDisplayDescription(sub.Description).Split('\n')[0]
                    : "?";
                return $"Subtask ({subId}) {title}";
            }).ToArray();
        }

        // Blocks with id + title (from !abc markers)
        if (parsed.BlocksIds is { Length: > 0 })
        {
            BlocksDisplay = parsed.BlocksIds.Select(bId =>
            {
                var b = taskList.GetTodoTaskById(bId);
                var title = b != null
                    ? TaskDescriptionParser.GetDisplayDescription(b.Description).Split('\n')[0]
                    : "?";
                return $"Blocks ({bId}) {title}";
            }).ToArray();
        }

        // Blocked by with id + title (from -!abc markers)
        if (parsed.BlockedByIds is { Length: > 0 })
        {
            BlockedByDisplay = parsed.BlockedByIds.Select(bbId =>
            {
                var bb = taskList.GetTodoTaskById(bbId);
                var title = bb != null
                    ? TaskDescriptionParser.GetDisplayDescription(bb.Description).Split('\n')[0]
                    : "?";
                return $"Blocked by ({bbId}) {title}";
            }).ToArray();
        }

        // Related with id + title (from ~abc markers)
        if (parsed.RelatedIds is { Length: > 0 })
        {
            RelatedDisplay = parsed.RelatedIds.Select(rId =>
            {
                var r = taskList.GetTodoTaskById(rId);
                var title = r != null
                    ? TaskDescriptionParser.GetDisplayDescription(r.Description).Split('\n')[0]
                    : "?";
                return $"Related to ({rId}) {title}";
            }).ToArray();
        }
    }

    private string GetDisplayText()
    {
        // Get first line only (from display description, which hides metadata-only last line)
        var displayDesc = TaskDescriptionParser.GetDisplayDescription(Description);
        return displayDesc.Split('\n')[0];
    }

    private string GetDescriptionPreview()
    {
        var displayDesc = TaskDescriptionParser.GetDisplayDescription(Description);
        var lines = displayDesc.Split('\n');
        if (lines.Length <= 1) return string.Empty;

        // Join remaining lines with newlines to preserve line breaks
        return string.Join("\n", lines.Skip(1).Select(l => l.Trim()).Where(l => !string.IsNullOrEmpty(l)));
    }
}
