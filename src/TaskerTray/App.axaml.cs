using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using TaskerCore.Data;
using TaskerTray.ViewModels;

namespace TaskerTray;

public class App : Application
{
    private TrayIcon? _trayIcon;
    private AppViewModel? _appViewModel;
    private TaskListViewModel? _taskListViewModel;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // No main window - tray only
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            _appViewModel = new AppViewModel();
            _appViewModel.QuitRequested += () => desktop.Shutdown();

            _taskListViewModel = new TaskListViewModel();

            SetupTrayIcon();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void SetupTrayIcon()
    {
        _trayIcon = new TrayIcon
        {
            ToolTipText = GetTooltipText(),
            IsVisible = true,
            Menu = CreateTrayMenu()
        };

        // Subscribe to task list changes to update menu
        if (_taskListViewModel != null)
        {
            _taskListViewModel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(TaskListViewModel.Tasks))
                {
                    UpdateTrayMenu();
                }
            };
        }
    }

    private string GetTooltipText()
    {
        if (_taskListViewModel == null) return "Tasker";

        var pendingCount = _taskListViewModel.UncheckedCount;
        var total = _taskListViewModel.TotalCount;

        if (total == 0) return "Tasker - No tasks";
        return $"Tasker - {pendingCount} pending / {total} total";
    }

    private NativeMenu CreateTrayMenu()
    {
        var menu = new NativeMenu();

        // Add task section
        var addItem = new NativeMenuItem("Add Task...");
        addItem.Click += (_, _) => _appViewModel?.AddTaskCommand.Execute(null);
        menu.Items.Add(addItem);

        menu.Items.Add(new NativeMenuItemSeparator());

        // Task items section
        AddTasksToMenu(menu);

        menu.Items.Add(new NativeMenuItemSeparator());

        // List filter submenu
        AddListFilterSubmenu(menu);

        menu.Items.Add(new NativeMenuItemSeparator());

        // Refresh item
        var refreshItem = new NativeMenuItem("Refresh");
        refreshItem.Click += (_, _) =>
        {
            _taskListViewModel?.Refresh();
            UpdateTrayMenu();
        };
        menu.Items.Add(refreshItem);

        // Quit item
        var quitItem = new NativeMenuItem("Quit Tasker");
        quitItem.Click += (_, _) => _appViewModel?.QuitCommand.Execute(null);
        menu.Items.Add(quitItem);

        return menu;
    }

    private void AddTasksToMenu(NativeMenu menu)
    {
        if (_taskListViewModel == null) return;

        var tasks = _taskListViewModel.Tasks;

        if (tasks.Count == 0)
        {
            var emptyItem = new NativeMenuItem("No tasks") { IsEnabled = false };
            menu.Items.Add(emptyItem);
            return;
        }

        // Check if viewing all lists (need grouping)
        var isViewingAll = _taskListViewModel.CurrentListFilter == null;

        if (isViewingAll)
        {
            // Group by list
            foreach (var group in _taskListViewModel.GetTasksByList())
            {
                // List header
                var headerItem = new NativeMenuItem($"-- {group.Key} --") { IsEnabled = false };
                menu.Items.Add(headerItem);

                // Tasks in this list (limit to 10 per list)
                foreach (var task in group.Take(10))
                {
                    AddTaskMenuItem(menu, task);
                }
            }
        }
        else
        {
            // Single list - no grouping needed, limit to 20
            foreach (var task in tasks.Take(20))
            {
                AddTaskMenuItem(menu, task);
            }
        }

        // Show truncation message if needed
        if (tasks.Count > 20)
        {
            var moreItem = new NativeMenuItem($"... and {tasks.Count - 20} more") { IsEnabled = false };
            menu.Items.Add(moreItem);
        }
    }

    private void AddTaskMenuItem(NativeMenu menu, TodoTaskViewModel task)
    {
        var item = new NativeMenuItem(task.MenuText);
        item.ToolTip = task.FullDescription;
        item.Click += (_, _) =>
        {
            task.ToggleCommand.Execute(null);
            UpdateTrayMenu();
        };
        menu.Items.Add(item);
    }

    private void AddListFilterSubmenu(NativeMenu menu)
    {
        if (_taskListViewModel == null) return;

        var submenu = new NativeMenu();
        var filterItem = new NativeMenuItem("Filter by List")
        {
            Menu = submenu
        };

        // "All Lists" option
        var allItem = new NativeMenuItem("All Lists");
        var isAllSelected = _taskListViewModel.CurrentListFilter == null;
        allItem.Click += (_, _) =>
        {
            _taskListViewModel.SetListFilter(null);
            UpdateTrayMenu();
        };
        if (isAllSelected)
        {
            allItem.Header = "* All Lists";
        }
        submenu.Items.Add(allItem);

        submenu.Items.Add(new NativeMenuItemSeparator());

        // Individual lists
        foreach (var list in _taskListViewModel.AvailableLists.Skip(1)) // Skip "All Lists" entry
        {
            var listItem = new NativeMenuItem(list);
            var isSelected = _taskListViewModel.CurrentListFilter == list;
            if (isSelected)
            {
                listItem.Header = $"* {list}";
            }
            listItem.Click += (_, _) =>
            {
                _taskListViewModel.SetListFilter(list);
                UpdateTrayMenu();
            };
            submenu.Items.Add(listItem);
        }

        menu.Items.Add(filterItem);
    }

    private void UpdateTrayMenu()
    {
        if (_trayIcon != null)
        {
            _trayIcon.Menu = CreateTrayMenu();
            _trayIcon.ToolTipText = GetTooltipText();
        }
    }
}
