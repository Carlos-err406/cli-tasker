using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskerCore.Data;

namespace TaskerTray.ViewModels;

public partial class AppViewModel : ObservableObject
{
    public event Action? QuitRequested;

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private bool _hasExternalChanges;

    public AppViewModel()
    {
        LoadTasks();
    }

    [RelayCommand]
    private void Quit()
    {
        QuitRequested?.Invoke();
    }

    [RelayCommand]
    private void Refresh()
    {
        LoadTasks();
        HasExternalChanges = false;
        StatusMessage = "Refreshed";
    }

    [RelayCommand]
    private void AddTask()
    {
        // TODO: Show dialog to add task
        // For now, just a placeholder
        StatusMessage = "Add task dialog coming soon";
    }

    private void LoadTasks()
    {
        try
        {
            var taskList = new TodoTaskList();
            var tasks = taskList.GetAllTasks();
            StatusMessage = $"Loaded {tasks.Count} tasks";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
    }
}
