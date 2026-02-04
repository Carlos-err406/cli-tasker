using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using TaskerCore.Config;
using TaskerCore.Data;
using TaskerCore.Models;
using TaskerTray.ViewModels;

namespace TaskerTray.Views;

public partial class TaskListPopup : Window
{
    public event Action? QuitRequested;

    private List<TodoTaskViewModel> _tasks = new();
    private string? _currentListFilter;
    private TextBox? _activeInlineEditor;
    private string? _editingTaskId;
    private string? _addingToList;
    private bool _creatingNewList;
    private int _showCount; // Incremented each time window is shown, used to ignore stale LostFocus events

    public TaskListPopup()
    {
        InitializeComponent();

        // Close when clicking outside or pressing Escape
        Deactivated += (_, _) =>
        {
            // Save any pending inline add before closing (don't wait for LostFocus which may fire late)
            SavePendingInlineAdd();
            CancelInlineEdit();
            Hide();
            // Hide from Dock when popup closes
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
            {
                HideFromDock();
            }
        };
        KeyDown += OnKeyDown;

        LoadLists();
        RefreshTasks();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (_activeInlineEditor != null)
            {
                CancelInlineEdit();
                e.Handled = true;
            }
            else
            {
                Hide();
            }
        }
        else if (e.Key == Key.W && e.KeyModifiers.HasFlag(KeyModifiers.Meta))
        {
            // Cmd+W closes the popup
            Hide();
            e.Handled = true;
        }
        else if (e.Key == Key.R && e.KeyModifiers.HasFlag(KeyModifiers.Meta))
        {
            RefreshTasks();
            e.Handled = true;
        }
        else if (e.Key == Key.Q && e.KeyModifiers.HasFlag(KeyModifiers.Meta))
        {
            QuitRequested?.Invoke();
            e.Handled = true;
        }
    }

    private void LoadLists()
    {
        // Lists are loaded dynamically when dropdown is clicked
    }

    public void RefreshTasks()
    {
        var taskList = new TodoTaskList(_currentListFilter);
        var tasks = taskList.GetAllTasks();

        _tasks.Clear();
        foreach (var task in tasks)
        {
            _tasks.Add(new TodoTaskViewModel(task));
        }

        BuildTaskList();
        UpdateStatus();
    }

    private void BuildTaskList()
    {
        TaskListPanel.Children.Clear();

        // Show create list input at top if active
        if (_creatingNewList)
        {
            TaskListPanel.Children.Add(CreateInlineListNameField());
        }

        if (_tasks.Count == 0 && _addingToList == null && !_creatingNewList)
        {
            var emptyText = new TextBlock
            {
                Text = "No tasks",
                Foreground = new SolidColorBrush(Color.Parse("#666")),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                Margin = new Thickness(0, 20, 0, 0)
            };
            TaskListPanel.Children.Add(emptyText);
            return;
        }

        // Group by list if viewing all lists
        if (_currentListFilter == null)
        {
            // Get all list names (including empty lists) and group tasks
            var allListNames = TodoTaskList.GetAllListNames();
            var tasksByList = _tasks.GroupBy(t => t.ListName).ToDictionary(g => g.Key, g => g.ToList());

            foreach (var listName in allListNames)
            {
                var isCollapsed = TodoTaskList.IsListCollapsed(listName);
                AddListHeader(listName, isCollapsed);

                // Only show tasks if list is not collapsed
                if (!isCollapsed)
                {
                    // Show inline add if adding to this list
                    if (_addingToList == listName)
                    {
                        TaskListPanel.Children.Add(CreateInlineAddField(listName));
                    }

                    // Get tasks for this list (empty list if none)
                    var tasksInList = tasksByList.GetValueOrDefault(listName, new List<TodoTaskViewModel>());

                    if (tasksInList.Count == 0 && _addingToList != listName)
                    {
                        // Show "empty" indicator for empty lists
                        var emptyIndicator = new TextBlock
                        {
                            Text = "No tasks in this list",
                            Foreground = new SolidColorBrush(Color.Parse("#555")),
                            FontSize = 12,
                            FontStyle = Avalonia.Media.FontStyle.Italic,
                            Margin = new Thickness(4, 4, 4, 8)
                        };
                        TaskListPanel.Children.Add(emptyIndicator);
                    }
                    else
                    {
                        foreach (var task in tasksInList)
                        {
                            if (_editingTaskId == task.Id)
                            {
                                TaskListPanel.Children.Add(CreateInlineEditField(task));
                            }
                            else
                            {
                                TaskListPanel.Children.Add(CreateTaskItem(task));
                            }
                        }
                    }
                }
            }

            // If adding to a list that doesn't exist yet (new list being created)
            if (_addingToList != null && !allListNames.Contains(_addingToList))
            {
                AddListHeader(_addingToList, false);
                TaskListPanel.Children.Add(CreateInlineAddField(_addingToList));
            }
        }
        else
        {
            // Single list view - ignore collapse state, always show tasks
            AddListHeader(_currentListFilter, false);

            if (_addingToList == _currentListFilter)
            {
                TaskListPanel.Children.Add(CreateInlineAddField(_currentListFilter));
            }

            foreach (var task in _tasks)
            {
                if (_editingTaskId == task.Id)
                {
                    TaskListPanel.Children.Add(CreateInlineEditField(task));
                }
                else
                {
                    TaskListPanel.Children.Add(CreateTaskItem(task));
                }
            }
        }

    }

    private void AddListHeader(string listName, bool isCollapsed = false)
    {
        var isDefaultList = listName == ListManager.DefaultListName;

        // Get task counts for summary display when collapsed
        var tasksInList = _tasks.Where(t => t.ListName == listName).ToList();
        var totalCount = tasksInList.Count;
        var pendingCount = tasksInList.Count(t => !t.IsChecked);

        var headerPanel = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse(isDefaultList ? "Auto,*,Auto" : "Auto,*,Auto,Auto"),
            Margin = new Thickness(4, 8, 4, 4)
        };

        // Collapse chevron button (column 0)
        var chevronBtn = new Button
        {
            Content = isCollapsed ? "▶" : "▼",
            Width = 18,
            Height = 18,
            FontSize = 10,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.Parse("#666")),
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        ToolTip.SetTip(chevronBtn, isCollapsed ? "Expand list" : "Collapse list");
        chevronBtn.Click += (_, _) => OnToggleListCollapsed(listName, !isCollapsed);
        Grid.SetColumn(chevronBtn, 0);
        headerPanel.Children.Add(chevronBtn);

        // List name + summary (column 1)
        var headerStack = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };

        var header = new TextBlock
        {
            Text = listName,
            FontWeight = FontWeight.SemiBold,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.Parse("#888")),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        headerStack.Children.Add(header);

        // Show summary when collapsed
        if (isCollapsed)
        {
            var summary = new TextBlock
            {
                Text = $"{totalCount} tasks, {pendingCount} pending",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.Parse("#555")),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
            headerStack.Children.Add(summary);
        }

        Grid.SetColumn(headerStack, 1);
        headerPanel.Children.Add(headerStack);

        // Add button (column 2)
        var addBtn = new Button
        {
            Content = "+",
            Width = 22,
            Height = 22,
            FontSize = 14,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.Parse("#888")),
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        ToolTip.SetTip(addBtn, $"Add task to {listName}");
        addBtn.Click += (_, _) => StartInlineAdd(listName);
        Grid.SetColumn(addBtn, 2);
        headerPanel.Children.Add(addBtn);

        // Add menu button for non-default lists (allows delete)
        if (!isDefaultList)
        {
            var menuBtn = new Button
            {
                Content = "•••",
                Width = 22,
                Height = 22,
                FontSize = 9,
                Padding = new Thickness(0),
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(Color.Parse("#666")),
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center
            };

            var contextMenu = new ContextMenu();
            var deleteItem = new MenuItem
            {
                Header = "Delete list",
                Foreground = new SolidColorBrush(Color.Parse("#FF6B6B"))
            };
            deleteItem.Click += (_, _) => OnDeleteListClicked(listName);
            contextMenu.Items.Add(deleteItem);

            menuBtn.ContextMenu = contextMenu;
            menuBtn.Click += (_, _) => contextMenu.Open(menuBtn);
            Grid.SetColumn(menuBtn, 3);
            headerPanel.Children.Add(menuBtn);
        }

        TaskListPanel.Children.Add(headerPanel);
    }

    private void OnToggleListCollapsed(string listName, bool collapsed)
    {
        TodoTaskList.SetListCollapsed(listName, collapsed);
        BuildTaskList();
    }

    private void OnDeleteListClicked(string listName)
    {
        try
        {
            ListManager.DeleteList(listName);
            RefreshTasks();
            StatusText.Text = $"Deleted list '{listName}'";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
    }

    private Border CreateInlineAddField(string listName)
    {
        var border = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#3A3A3A")),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8),
            Margin = new Thickness(0, 2)
        };

        var textBox = new TextBox
        {
            Watermark = "New task...",
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            FontSize = 13,
            Foreground = Brushes.White,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap
        };

        textBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                e.Handled = true;
                SubmitInlineAdd(textBox.Text, listName);
            }
            else if (e.Key == Key.Escape)
            {
                CancelInlineEdit();
            }
        };

        // Capture current show count to detect stale events
        var capturedShowCount = _showCount;
        textBox.LostFocus += (_, _) =>
        {
            // Ignore if this event is from a previous show (popup was closed and reopened)
            if (capturedShowCount != _showCount)
                return;

            if (!string.IsNullOrWhiteSpace(textBox.Text))
            {
                SubmitInlineAdd(textBox.Text, listName);
            }
            else
            {
                CancelInlineEdit();
            }
        };

        _activeInlineEditor = textBox;
        border.Child = textBox;

        // Focus after render
        Avalonia.Threading.Dispatcher.UIThread.Post(() => textBox.Focus(), Avalonia.Threading.DispatcherPriority.Background);

        return border;
    }

    private Border CreateInlineEditField(TodoTaskViewModel task)
    {
        var border = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#3A3A3A")),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8),
            Margin = new Thickness(0, 2)
        };

        var textBox = new TextBox
        {
            Text = task.FullDescription,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            FontSize = 13,
            Foreground = Brushes.White,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap
        };

        textBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                e.Handled = true;
                SubmitInlineEdit(task.Id, textBox.Text);
            }
            else if (e.Key == Key.Escape)
            {
                CancelInlineEdit();
            }
        };

        textBox.LostFocus += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(textBox.Text))
            {
                SubmitInlineEdit(task.Id, textBox.Text);
            }
            else
            {
                CancelInlineEdit();
            }
        };

        _activeInlineEditor = textBox;
        border.Child = textBox;

        // Focus and select all after render
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            textBox.Focus();
            textBox.SelectAll();
        }, Avalonia.Threading.DispatcherPriority.Background);

        return border;
    }

    private void StartInlineAdd(string listName)
    {
        CancelInlineEdit();
        _addingToList = listName;

        // Auto-expand the list if it's collapsed (so user can see the add field)
        if (TodoTaskList.IsListCollapsed(listName))
        {
            TodoTaskList.SetListCollapsed(listName, false);
        }

        BuildTaskList();
    }

    private void StartInlineEdit(TodoTaskViewModel task)
    {
        CancelInlineEdit();
        _editingTaskId = task.Id;
        BuildTaskList();
    }

    private void OnCreateListClick(object? sender, RoutedEventArgs e)
    {
        // No-op if already in create mode (handles double-click)
        if (_creatingNewList) return;

        CancelInlineEdit();  // Discards any pending task add/edit
        _creatingNewList = true;
        BuildTaskList();
    }

    private Border CreateInlineListNameField()
    {
        var border = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#3A3A3A")),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8),
            Margin = new Thickness(4, 4, 4, 8)
        };

        var textBox = new TextBox
        {
            Watermark = "New list name...",
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            FontSize = 13,
            Foreground = Brushes.White,
            AcceptsReturn = false,
            MaxLength = 50
        };

        var submitted = false;  // Local flag to prevent double submission

        textBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                if (!submitted)
                {
                    submitted = true;
                    SubmitNewListName(textBox.Text);
                }
            }
            else if (e.Key == Key.Escape)
            {
                submitted = true;  // Mark as handled to prevent LostFocus submission
                CancelInlineEdit();
                BuildTaskList();
            }
        };

        // CRITICAL: Apply show count pattern to prevent race condition
        var capturedShowCount = _showCount;
        textBox.LostFocus += (_, _) =>
        {
            if (capturedShowCount != _showCount)
                return;

            // Guard against double submission (Enter/Escape already processed)
            if (submitted)
                return;

            submitted = true;
            if (!string.IsNullOrWhiteSpace(textBox.Text))
                SubmitNewListName(textBox.Text);
            else
                CancelInlineEdit();
        };

        _activeInlineEditor = textBox;
        border.Child = textBox;

        Avalonia.Threading.Dispatcher.UIThread.Post(() => textBox.Focus(),
            Avalonia.Threading.DispatcherPriority.Background);

        return border;
    }

    private void SubmitNewListName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            CancelInlineEdit();
            return;
        }

        try
        {
            ListManager.CreateList(name.Trim());
            CancelInlineEdit();
            RefreshTasks();
            StatusText.Text = $"Created list '{name.Trim()}'";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
            CancelInlineEdit();
            BuildTaskList();
        }
    }

    private void SubmitInlineAdd(string? text, string listName)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            CancelInlineEdit();
            return;
        }

        try
        {
            var task = TodoTask.CreateTodoTask(text.Trim(), listName);
            var taskList = new TodoTaskList(listName);
            taskList.AddTodoTask(task);
            CancelInlineEdit();
            RefreshTasks();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
            CancelInlineEdit();
        }
    }

    private void SubmitInlineEdit(string taskId, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            CancelInlineEdit();
            return;
        }

        try
        {
            var taskList = new TodoTaskList();
            taskList.RenameTask(taskId, text.Trim());
            CancelInlineEdit();
            RefreshTasks();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
            CancelInlineEdit();
        }
    }

    private void CancelInlineEdit()
    {
        _activeInlineEditor = null;
        _editingTaskId = null;
        _addingToList = null;
        _creatingNewList = false;
    }

    /// <summary>
    /// Saves any pending inline add without refreshing UI (called before window hides).
    /// </summary>
    private void SavePendingInlineAdd()
    {
        if (_activeInlineEditor == null)
            return;

        var text = _activeInlineEditor.Text;
        if (string.IsNullOrWhiteSpace(text))
            return;

        // Handle pending list creation
        if (_creatingNewList)
        {
            try
            {
                ListManager.CreateList(text.Trim());
            }
            catch
            {
                // Silently fail - list won't be created but UI won't crash
            }
            return;
        }

        // Handle pending task add
        if (_addingToList == null)
            return;

        try
        {
            var task = TodoTask.CreateTodoTask(text.Trim(), _addingToList);
            var taskList = new TodoTaskList(_addingToList);
            taskList.AddTodoTask(task);
        }
        catch
        {
            // Silently fail - task will be lost but UI won't crash
        }
    }

    private Border CreateTaskItem(TodoTaskViewModel task)
    {
        var border = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#2A2A2A")),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8),
            Margin = new Thickness(0, 2)
        };

        var grid = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("Auto,*,Auto")
        };

        // Checkbox
        var checkbox = new CheckBox
        {
            IsChecked = task.IsChecked,
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top
        };
        Grid.SetColumn(checkbox, 0);
        checkbox.Click += (_, _) => OnCheckboxClicked(task, checkbox);
        grid.Children.Add(checkbox);

        // Task content (title + description)
        var contentPanel = new StackPanel
        {
            Spacing = 2
        };
        Grid.SetColumn(contentPanel, 1);

        var titleColor = task.IsChecked ? "#666" : "#FFF";
        var title = new TextBlock
        {
            Text = task.DisplayText,
            FontWeight = FontWeight.SemiBold,
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.Parse(titleColor)),
            TextWrapping = TextWrapping.Wrap
        };
        contentPanel.Children.Add(title);

        if (task.HasDescription)
        {
            var desc = new TextBlock
            {
                Text = task.DescriptionPreview,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.Parse("#888")),
                TextWrapping = TextWrapping.Wrap
            };
            contentPanel.Children.Add(desc);
        }

        grid.Children.Add(contentPanel);

        // Menu button (always visible, opens dropdown)
        var menuBtn = new Button
        {
            Content = "•••",
            Width = 28,
            Height = 28,
            FontSize = 11,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.Parse("#888")),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center
        };

        var contextMenu = new ContextMenu();

        var editItem = new MenuItem { Header = "Edit" };
        editItem.Click += (_, _) => StartInlineEdit(task);
        contextMenu.Items.Add(editItem);

        // Move submenu
        var moveItem = new MenuItem { Header = "Move to..." };
        var allLists = TodoTaskList.GetAllListNames().Where(l => l != task.ListName).ToList();
        foreach (var listName in allLists)
        {
            var moveToItem = new MenuItem { Header = listName };
            moveToItem.Click += (_, _) => OnMoveTask(task, listName);
            moveItem.Items.Add(moveToItem);
        }
        if (moveItem.Items.Count > 0)
        {
            contextMenu.Items.Add(moveItem);
        }

        contextMenu.Items.Add(new Separator());

        var deleteItem = new MenuItem { Header = "Delete", Foreground = new SolidColorBrush(Color.Parse("#FF6B6B")) };
        deleteItem.Click += (_, _) => OnDeleteTaskClicked(task);
        contextMenu.Items.Add(deleteItem);

        menuBtn.ContextMenu = contextMenu;
        menuBtn.Click += (_, _) => contextMenu.Open(menuBtn);

        Grid.SetColumn(menuBtn, 2);
        grid.Children.Add(menuBtn);

        border.Child = grid;
        return border;
    }

    private void OnCheckboxClicked(TodoTaskViewModel task, CheckBox checkbox)
    {
        try
        {
            var taskList = new TodoTaskList();
            if (checkbox.IsChecked == true)
            {
                taskList.CheckTask(task.Id);
            }
            else
            {
                taskList.UncheckTask(task.Id);
            }

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                RefreshTasks();
            }, Avalonia.Threading.DispatcherPriority.Background);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
    }

    private void OnMoveTask(TodoTaskViewModel task, string targetList)
    {
        try
        {
            var taskList = new TodoTaskList();
            taskList.MoveTask(task.Id, targetList);
            RefreshTasks();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
    }

    private void OnDeleteTaskClicked(TodoTaskViewModel task)
    {
        try
        {
            var taskList = new TodoTaskList();
            taskList.DeleteTask(task.Id);
            RefreshTasks();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
    }

    private void UpdateStatus()
    {
        var total = _tasks.Count;
        var pending = _tasks.Count(t => !t.IsChecked);
        StatusText.Text = $"{pending} pending / {total} total";
    }

    private void OnListFilterClick(object? sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();

        var lists = TodoTaskList.GetAllListNames().ToList();
        lists.Insert(0, "All Lists");

        foreach (var listName in lists)
        {
            var item = new MenuItem
            {
                Header = listName,
                FontSize = 12
            };
            item.Click += (_, _) => SelectList(listName);
            menu.Items.Add(item);
        }

        menu.Open(ListFilterButton);
    }

    private void SelectList(string listName)
    {
        _currentListFilter = listName == "All Lists" ? null : listName;
        ListFilterText.Text = listName;
        CancelInlineEdit();
        RefreshTasks();
    }

    private void OnListFilterChanged(object? sender, SelectionChangedEventArgs e)
    {
        // No longer used - kept for compatibility
    }

    private async void OnAddTask(object? sender, RoutedEventArgs e)
    {
        // Start inline add for the default list or current filter
        var targetList = _currentListFilter ?? AppConfig.GetDefaultList();
        StartInlineAdd(targetList);
    }

    private void OnTaskPressed(object? sender, PointerPressedEventArgs e) { }
    private void OnCheckboxClick(object? sender, RoutedEventArgs e) { }
    private void OnRenameTask(object? sender, RoutedEventArgs e) { }
    private void OnDeleteTask(object? sender, RoutedEventArgs e) { }

    private void OnShowTrash(object? sender, RoutedEventArgs e)
    {
        StatusText.Text = "Trash view coming soon";
    }

    private void OnQuit(object? sender, RoutedEventArgs e)
    {
        QuitRequested?.Invoke();
    }

    public void ShowAtPosition(PixelPoint position)
    {
        _showCount++; // Increment to invalidate any pending LostFocus handlers from previous show
        Position = position;
        CancelInlineEdit();
        RefreshTasks();
        Show();

        // Activate app and window on macOS - temporarily become regular app to get focus
        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
        {
            SetActivationPolicy(0); // Regular - shows in Dock but can focus
            ActivateApp();
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                MakeKeyWindow(this);
            }, Avalonia.Threading.DispatcherPriority.Render);
        }
        else
        {
            Activate();
        }
    }

    // Duplicate of App's HideFromDock for use in popup
    [System.Runtime.InteropServices.DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_getClass")]
    private static extern IntPtr objc_getClass(string className);

    [System.Runtime.InteropServices.DllImport("/usr/lib/libobjc.dylib", EntryPoint = "sel_registerName")]
    private static extern IntPtr sel_registerName(string selector);

    [System.Runtime.InteropServices.DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_ReturnIntPtr(IntPtr receiver, IntPtr selector);

    [System.Runtime.InteropServices.DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_SetLong(IntPtr receiver, IntPtr selector, long arg);

    private static void HideFromDock()
    {
        SetActivationPolicy(1); // Accessory - no Dock icon
    }

    private static void SetActivationPolicy(long policy)
    {
        try
        {
            var nsAppClass = objc_getClass("NSApplication");
            var sharedAppSel = sel_registerName("sharedApplication");
            var setPolicySel = sel_registerName("setActivationPolicy:");

            var nsApp = objc_msgSend_ReturnIntPtr(nsAppClass, sharedAppSel);
            objc_msgSend_SetLong(nsApp, setPolicySel, policy);
        }
        catch { }
    }

    private static void ActivateApp()
    {
        try
        {
            var nsAppClass = objc_getClass("NSApplication");
            var sharedAppSel = sel_registerName("sharedApplication");
            var activateSel = sel_registerName("activateIgnoringOtherApps:");

            var nsApp = objc_msgSend_ReturnIntPtr(nsAppClass, sharedAppSel);
            objc_msgSend_SetBool(nsApp, activateSel, true);
        }
        catch { }
    }

    [System.Runtime.InteropServices.DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_SetBool(IntPtr receiver, IntPtr selector, bool arg);

    [System.Runtime.InteropServices.DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_Void(IntPtr receiver, IntPtr selector);

    private static void MakeKeyWindow(Window window)
    {
        try
        {
            var handle = window.TryGetPlatformHandle();
            if (handle != null)
            {
                var nsWindow = handle.Handle;
                var makeKeySel = sel_registerName("makeKeyAndOrderFront:");
                objc_msgSend_SetPtr(nsWindow, makeKeySel, IntPtr.Zero);
            }
        }
        catch { }
    }

    [System.Runtime.InteropServices.DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_SetPtr(IntPtr receiver, IntPtr selector, IntPtr arg);
}
