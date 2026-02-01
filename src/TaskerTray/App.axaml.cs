using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using TaskerCore.Data;
using TaskerTray.Services;
using TaskerTray.ViewModels;
using TaskerTray.Views;

namespace TaskerTray;

public class App : Application
{
    private TrayIcon? _trayIcon;
    private AppViewModel? _appViewModel;
    private TaskListViewModel? _taskListViewModel;
    private FileWatcherService? _fileWatcher;

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
            _appViewModel.QuitRequested += () =>
            {
                _fileWatcher?.Dispose();
                desktop.Shutdown();
            };
            _appViewModel.TasksChanged += OnTasksChanged;

            _taskListViewModel = new TaskListViewModel();

            // Start watching for external file changes
            _fileWatcher = new FileWatcherService();
            _fileWatcher.ExternalChangeDetected += OnExternalChange;

            SetupTrayIcon();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnTasksChanged()
    {
        _taskListViewModel?.Refresh();
        UpdateTrayMenu();
    }

    private void OnExternalChange()
    {
        // Notify the ViewModel so it can update HasExternalChanges
        _appViewModel?.NotifyExternalChange();

        // Refresh task list to reflect external changes
        _taskListViewModel?.Refresh();
        UpdateTrayMenu();
    }

    private void SetupTrayIcon()
    {
        _trayIcon = new TrayIcon
        {
            ToolTipText = GetTooltipText(),
            IsVisible = true,
            Menu = CreateTrayMenu()
        };
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
        var addItem = new NativeMenuItem("Add Task...")
        {
            Gesture = new KeyGesture(Key.N, KeyModifiers.Meta)
        };
        addItem.Click += async (_, _) => await ShowQuickAddDialog();
        menu.Items.Add(addItem);

        menu.Items.Add(new NativeMenuItemSeparator());

        // Task items section
        AddTasksToMenu(menu);

        menu.Items.Add(new NativeMenuItemSeparator());

        // List filter submenu
        AddListFilterSubmenu(menu);

        menu.Items.Add(new NativeMenuItemSeparator());

        // Undo/Redo
        var undoItem = new NativeMenuItem("Undo")
        {
            Gesture = new KeyGesture(Key.Z, KeyModifiers.Meta),
            IsEnabled = _appViewModel?.CanUndo ?? false
        };
        undoItem.Click += (_, _) =>
        {
            _appViewModel?.UndoCommand.Execute(null);
        };
        menu.Items.Add(undoItem);

        var redoItem = new NativeMenuItem("Redo")
        {
            Gesture = new KeyGesture(Key.Z, KeyModifiers.Meta | KeyModifiers.Shift),
            IsEnabled = _appViewModel?.CanRedo ?? false
        };
        redoItem.Click += (_, _) =>
        {
            _appViewModel?.RedoCommand.Execute(null);
        };
        menu.Items.Add(redoItem);

        menu.Items.Add(new NativeMenuItemSeparator());

        // Refresh item
        var refreshItem = new NativeMenuItem("Refresh")
        {
            Gesture = new KeyGesture(Key.R, KeyModifiers.Meta)
        };
        refreshItem.Click += (_, _) =>
        {
            _appViewModel?.RefreshCommand.Execute(null);
        };
        menu.Items.Add(refreshItem);

        // Quit item
        var quitItem = new NativeMenuItem("Quit Tasker")
        {
            Gesture = new KeyGesture(Key.Q, KeyModifiers.Meta)
        };
        quitItem.Click += (_, _) => _appViewModel?.QuitCommand.Execute(null);
        menu.Items.Add(quitItem);

        return menu;
    }

    private async System.Threading.Tasks.Task ShowQuickAddDialog()
    {
        var window = new QuickAddWindow();
        var result = await window.ShowDialog<bool?>(null);

        if (result == true && !string.IsNullOrEmpty(window.TaskDescription))
        {
            _appViewModel?.AddTaskWithDescription(window.TaskDescription);
        }
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
        // Create a submenu for each task with toggle and delete options
        var taskSubmenu = new NativeMenu();

        var toggleItem = new NativeMenuItem(task.IsChecked ? "Uncheck" : "Check");
        toggleItem.Click += (_, _) =>
        {
            task.ToggleCommand.Execute(null);
            _taskListViewModel?.Refresh();
            UpdateTrayMenu();
        };
        taskSubmenu.Items.Add(toggleItem);

        var deleteItem = new NativeMenuItem("Delete");
        deleteItem.Click += (_, _) =>
        {
            task.DeleteCommand.Execute(null);
            _taskListViewModel?.Refresh();
            UpdateTrayMenu();
        };
        taskSubmenu.Items.Add(deleteItem);

        var item = new NativeMenuItem(task.MenuText)
        {
            Menu = taskSubmenu,
            ToolTip = task.FullDescription
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
