using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskerCore;
using TaskerCore.Data;
using TaskerCore.Models;
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
    /// Menu item text with checkbox indicator.
    /// </summary>
    public string MenuText => IsChecked ? $"[x] {DisplayText}" : $"[ ] {DisplayText}";

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

    private string GetDisplayText()
    {
        // Get first line only
        var firstLine = Description.Split('\n')[0];

        // Truncate long descriptions
        const int maxLength = 40;
        if (firstLine.Length > maxLength)
        {
            return StringHelpers.Truncate(firstLine, maxLength);
        }

        return firstLine;
    }
}
