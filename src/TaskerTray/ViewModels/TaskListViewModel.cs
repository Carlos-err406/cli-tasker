using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskerCore.Data;
using TaskerCore.Models;

namespace TaskerTray.ViewModels;

public partial class TaskListViewModel : ObservableObject
{
    private readonly TodoTaskList _taskList;

    [ObservableProperty]
    private string? _currentListFilter;

    [ObservableProperty]
    private ObservableCollection<TodoTaskViewModel> _tasks = new();

    [ObservableProperty]
    private ObservableCollection<string> _availableLists = new();

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private int _uncheckedCount;

    public TaskListViewModel(string? listFilter = null)
    {
        _currentListFilter = listFilter;
        _taskList = new TodoTaskList(listFilter);
        LoadTasks();
        LoadAvailableLists();
    }

    public void Refresh()
    {
        LoadTasks();
        LoadAvailableLists();
    }

    public void SetListFilter(string? listName)
    {
        CurrentListFilter = listName;
        LoadTasks();
    }

    private void LoadTasks()
    {
        var taskList = new TodoTaskList(CurrentListFilter);
        var sortedTasks = taskList.GetSortedTasks();

        // Update counts
        TotalCount = sortedTasks.Count;
        UncheckedCount = sortedTasks.Count(t => !t.IsChecked);

        // Convert to ViewModels
        Tasks.Clear();
        foreach (var task in sortedTasks)
        {
            Tasks.Add(new TodoTaskViewModel(task, OnTaskChanged));
        }
    }

    private void LoadAvailableLists()
    {
        var lists = TodoTaskList.GetAllListNames();
        AvailableLists.Clear();
        AvailableLists.Add("All Lists"); // Special option for no filter
        foreach (var list in lists)
        {
            AvailableLists.Add(list);
        }
    }

    private void OnTaskChanged(TodoTaskViewModel taskVm)
    {
        // Refresh to reflect changes and re-sort
        LoadTasks();
    }

    /// <summary>
    /// Get tasks grouped by list name for display in menu.
    /// </summary>
    public IEnumerable<IGrouping<string, TodoTaskViewModel>> GetTasksByList()
    {
        // If viewing all lists, group by list name
        if (CurrentListFilter == null)
        {
            return Tasks
                .GroupBy(t => t.ListName)
                .OrderBy(g => g.Key != ListManager.DefaultListName) // default list first
                .ThenBy(g => g.Key);
        }

        // Single list - return as single group
        return Tasks.GroupBy(t => t.ListName);
    }
}
