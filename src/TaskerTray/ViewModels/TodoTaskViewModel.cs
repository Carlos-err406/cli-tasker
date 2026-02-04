using System;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskerCore;
using TaskerCore.Data;
using TaskerCore.Models;
using TaskerCore.Parsing;
using TaskerCore.Results;

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

    [ObservableProperty]
    private bool _isChecked;

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
    public string MenuText => IsChecked ? $"[x] {DisplayText}" : $"[ ] {DisplayText}";

    /// <summary>
    /// Priority display text.
    /// </summary>
    public string PriorityDisplay => Priority switch
    {
        TaskerCore.Models.Priority.High => "!",
        TaskerCore.Models.Priority.Medium => "·",
        TaskerCore.Models.Priority.Low => "·",
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
    /// Due date color for display.
    /// </summary>
    public IBrush DueDateColor => IsOverdue ? Brushes.Red : (IsDueToday ? Brushes.Orange : Brushes.Gray);

    /// <summary>
    /// Tags display text.
    /// </summary>
    public string TagsDisplay => HasTags ? string.Join(" ", Tags!.Select(t => $"#{t}")) : "";

    public TodoTaskViewModel(TodoTask task, Action<TodoTaskViewModel>? onChanged = null)
    {
        _task = task;
        _onChanged = onChanged;
        _isChecked = task.IsChecked;
    }

    [RelayCommand]
    private void Toggle()
    {
        var taskList = new TodoTaskList();
        TaskResult result;

        if (IsChecked)
        {
            result = taskList.UncheckTask(Id);
        }
        else
        {
            result = taskList.CheckTask(Id);
        }

        if (result is TaskResult.Success)
        {
            IsChecked = !IsChecked;
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

        // Join remaining lines - no truncation, let UI wrap
        return string.Join(" ", lines.Skip(1).Select(l => l.Trim()).Where(l => !string.IsNullOrEmpty(l)));
    }
}
