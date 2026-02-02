using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using CommunityToolkit.Mvvm.ComponentModel;
using TaskerTray.ViewModels;

namespace TaskerTray;

/// <summary>
/// AOT-compatible ViewLocator using explicit type mapping instead of reflection.
/// </summary>
public class ViewLocator : IDataTemplate
{
    // Explicit mapping of ViewModels to Views - no reflection needed
    private static readonly Dictionary<Type, Func<Control>> ViewFactories = new()
    {
        // Add mappings as we create views
        // [typeof(AppViewModel)] = () => new AppView(),
        // [typeof(TaskListViewModel)] = () => new TaskListView(),
        // [typeof(TodoTaskViewModel)] = () => new TodoTaskView(),
    };

    public Control Build(object? data)
    {
        if (data is null)
            return new TextBlock { Text = "Data is null" };

        var viewModelType = data.GetType();

        if (ViewFactories.TryGetValue(viewModelType, out var factory))
            return factory();

        // Fallback for unmapped types - helpful during development
        return new TextBlock { Text = $"No view for {viewModelType.Name}" };
    }

    public bool Match(object? data) => data is ObservableObject;
}
