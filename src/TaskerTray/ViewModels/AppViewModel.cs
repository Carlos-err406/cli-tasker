using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskerCore.Config;
using TaskerCore.Data;
using TaskerCore.Models;
using TaskerCore.Undo;

namespace TaskerTray.ViewModels;

public partial class AppViewModel : ObservableObject
{
    public event Action? QuitRequested;
    public event Action? TasksChanged;

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private bool _hasExternalChanges;

    [ObservableProperty]
    private bool _canUndo;

    [ObservableProperty]
    private bool _canRedo;

    public AppViewModel()
    {
        UpdateUndoState();
    }

    [RelayCommand]
    private void Quit()
    {
        QuitRequested?.Invoke();
    }

    [RelayCommand]
    private void Refresh()
    {
        HasExternalChanges = false;
        StatusMessage = "Refreshed";
        TasksChanged?.Invoke();
    }

    /// <summary>
    /// Called from App.axaml.cs after showing QuickAddWindow.
    /// </summary>
    public void AddTaskWithDescription(string description)
    {
        try
        {
            var defaultList = AppConfig.GetDefaultList();
            var task = TodoTask.CreateTodoTask(description, defaultList);
            var taskList = new TodoTaskList(defaultList);
            taskList.AddTodoTask(task);

            StatusMessage = $"Added: {description}";
            UpdateUndoState();
            TasksChanged?.Invoke();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Undo()
    {
        var undoManager = UndoManager.Instance;
        if (undoManager.CanUndo)
        {
            undoManager.Undo();
            StatusMessage = "Undone";
            UpdateUndoState();
            TasksChanged?.Invoke();
        }
    }

    [RelayCommand]
    private void Redo()
    {
        var undoManager = UndoManager.Instance;
        if (undoManager.CanRedo)
        {
            undoManager.Redo();
            StatusMessage = "Redone";
            UpdateUndoState();
            TasksChanged?.Invoke();
        }
    }

    private void UpdateUndoState()
    {
        var undoManager = UndoManager.Instance;
        CanUndo = undoManager.CanUndo;
        CanRedo = undoManager.CanRedo;
    }

    public void NotifyExternalChange()
    {
        HasExternalChanges = true;
        UpdateUndoState();
    }
}
