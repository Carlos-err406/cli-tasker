using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TaskerCore.Config;
using TaskerCore.Data;
using TaskerCore.Models;
using TaskerCore.Parsing;
using TaskerTray.ViewModels;

namespace TaskerTray.Views;

public partial class TaskListPopup : Window
{
    public event Action? QuitRequested;

    // Drag-drop state machine
    private enum PopupState { Idle, Dragging, DroppingInProgress, RefreshPending }
    private PopupState _state = PopupState.Idle;
    private int _generationId; // Incremented on BuildTaskList to invalidate stale handlers

    private List<TodoTaskViewModel> _tasks = new();
    private string? _currentListFilter;
    private TextBox? _activeInlineEditor;
    private string? _editingTaskId;
    private string? _addingToList;
    private bool _creatingNewList;
    private string? _renamingList; // Track which list is being renamed
    private int _showCount; // Incremented each time window is shown, used to ignore stale LostFocus events
    private bool _inlineAddSubmitted; // Prevents double submission between Deactivated and LostFocus
    private string? _newlyAddedTaskId; // Track newly added task for entrance animation
    private Dictionary<string, Border> _taskBorders = new(); // Track task borders for animations

    public TaskListPopup()
    {
        InitializeComponent();

        // Close when clicking outside or pressing Escape
        Deactivated += async (_, _) =>
        {
            // Save any pending inline add before closing (don't wait for LostFocus which may fire late)
            SavePendingInlineAdd();
            CancelInlineEdit();
            CancelDrag(); // Cancel any in-progress drag
            await HideWithAnimation();
        };
        KeyDown += OnKeyDown;

        // Window-level pointer handlers for smooth drag tracking
        PointerMoved += OnWindowPointerMoved;
        PointerReleased += OnWindowPointerReleased;

        LoadLists();
        RefreshTasks();
    }

    private void OnWindowPointerMoved(object? sender, PointerEventArgs e)
    {
        // Check if we're in pending drag state (waiting to cross threshold)
        if (_dragStartPoint.HasValue && !_isDragging)
        {
            var currentPoint = e.GetPosition(this);
            var distance = Math.Sqrt(
                Math.Pow(currentPoint.X - _dragStartPoint.Value.X, 2) +
                Math.Pow(currentPoint.Y - _dragStartPoint.Value.Y, 2));

            // Start drag after 5 pixel threshold
            if (distance > 5)
            {
                if (_pendingDragTask != null && _pendingDragBorder != null)
                {
                    StartTaskDrag(_pendingDragBorder, _pendingDragTask, e);
                }
                else if (_pendingDragListName != null && _pendingDragBorder != null)
                {
                    StartListDrag(_pendingDragBorder, _pendingDragListName, e);
                }
                ClearPendingDrag();
            }
            return;
        }

        // Handle active drag
        if (!_isDragging || _dragGhost == null) return;

        var pos = e.GetPosition(this);

        // Update ghost position
        Canvas.SetLeft(_dragGhost, pos.X - _dragOffset.X);
        Canvas.SetTop(_dragGhost, pos.Y - _dragOffset.Y);

        // Update drop indicator based on drag type
        if (_draggedTask != null)
        {
            UpdateTaskDropTarget(e.GetPosition(TaskListPanel));
        }
        else if (_draggedListName != null)
        {
            UpdateListDropTarget(e.GetPosition(TaskListPanel));
        }
    }

    private void OnWindowPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        // Clear pending drag if threshold wasn't crossed
        ClearPendingDrag();

        if (!_isDragging) return;

        // Complete the drag before cleanup
        if (_draggedTask != null && _dropTargetIndex >= 0)
        {
            CompleteTaskDrag(_draggedTask);
        }
        else if (_draggedListName != null && _listDropTargetIndex >= 0)
        {
            CompleteListDrag(_draggedListName);
        }

        // Cleanup (handles pending refresh if reorder wasn't completed)
        CleanupDrag();
    }

    private void ClearPendingDrag()
    {
        var hadPendingDrag = _dragStartPoint.HasValue;

        // Reset grip handle color if we have one
        if (_pendingGripHandle != null)
        {
            foreach (var dot in _pendingGripHandle.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("gripDot")))
            {
                dot.Background = new SolidColorBrush(Color.Parse("#666"));
            }
        }

        _dragStartPoint = null;
        _pendingDragBorder = null;
        _pendingDragTask = null;
        _pendingDragListName = null;
        _pendingGripHandle = null;

        // If we had a pending drag and there's a queued refresh, do it now
        if (hadPendingDrag && _state == PopupState.RefreshPending && !_isDragging)
        {
            _state = PopupState.Idle;
            DoRefreshTasks();
        }
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
                _ = HideWithAnimation();
            }
        }
        else if (e.Key == Key.W && e.KeyModifiers.HasFlag(KeyModifiers.Meta))
        {
            // Cmd+W closes the popup
            _ = HideWithAnimation();
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
        // Queue refresh if drag operation is in progress OR pending (user pressed but hasn't moved threshold)
        if (_state == PopupState.Dragging || _state == PopupState.DroppingInProgress || _dragStartPoint.HasValue)
        {
            _state = PopupState.RefreshPending;
            return;
        }

        DoRefreshTasks();
    }

    private void DoRefreshTasks()
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
        _generationId++; // Invalidate all handlers from previous builds
        TaskListPanel.Children.Clear();
        _taskBorders.Clear();
        _listTaskPanels.Clear();

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

                // Create container panel for this list's tasks (for collapse animation)
                var tasksPanel = new StackPanel
                {
                    Classes = { "listTasks" },
                    ClipToBounds = true
                };
                if (isCollapsed)
                {
                    tasksPanel.Classes.Add("collapsed");
                }

                // Set up as drop target for task reordering
                SetupTaskPanelDropTarget(tasksPanel, listName);

                // Show inline add if adding to this list
                if (_addingToList == listName)
                {
                    tasksPanel.Children.Add(CreateInlineAddField(listName));
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
                    tasksPanel.Children.Add(emptyIndicator);
                }
                else
                {
                    foreach (var task in tasksInList)
                    {
                        if (_editingTaskId == task.Id)
                        {
                            tasksPanel.Children.Add(CreateInlineEditField(task));
                        }
                        else
                        {
                            tasksPanel.Children.Add(CreateTaskItem(task));
                        }
                    }
                }

                TaskListPanel.Children.Add(tasksPanel);
            }

            // If adding to a list that doesn't exist yet (new list being created)
            if (_addingToList != null && !allListNames.Contains(_addingToList))
            {
                AddListHeader(_addingToList, false);
                var newListPanel = new StackPanel { Classes = { "listTasks" }, ClipToBounds = true };
                SetupTaskPanelDropTarget(newListPanel, _addingToList);
                newListPanel.Children.Add(CreateInlineAddField(_addingToList));
                TaskListPanel.Children.Add(newListPanel);
            }
        }
        else
        {
            // Single list view - use container panel for drop target support
            AddListHeader(_currentListFilter, false);

            var singleListPanel = new StackPanel
            {
                Classes = { "listTasks" },
                ClipToBounds = true
            };
            SetupTaskPanelDropTarget(singleListPanel, _currentListFilter);

            if (_addingToList == _currentListFilter)
            {
                singleListPanel.Children.Add(CreateInlineAddField(_currentListFilter));
            }

            foreach (var task in _tasks)
            {
                if (_editingTaskId == task.Id)
                {
                    singleListPanel.Children.Add(CreateInlineEditField(task));
                }
                else
                {
                    singleListPanel.Children.Add(CreateTaskItem(task));
                }
            }

            TaskListPanel.Children.Add(singleListPanel);
        }

    }

    private void AddListHeader(string listName, bool isCollapsed = false)
    {
        // Show inline rename field instead of normal header when renaming this list
        if (_renamingList == listName)
        {
            TaskListPanel.Children.Add(CreateInlineListRenameField(listName));
            return;
        }

        var isDefaultList = listName == ListManager.DefaultListName;
        var allListNames = TodoTaskList.GetAllListNames().ToList();
        var canReorder = allListNames.Count > 1;

        // Get task counts for summary display when collapsed
        var tasksInList = _tasks.Where(t => t.ListName == listName).ToList();
        var totalCount = tasksInList.Count;
        var pendingCount = tasksInList.Count(t => !t.IsChecked);

        // Wrap header in a Border for drag styling
        var headerBorder = new Border
        {
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(4, 8, 4, 4),
            Classes = { "listHeader" }
        };

        // Column count: grip (if reorderable) + chevron + name + add + menu (if not default)
        var columnDef = canReorder
            ? (isDefaultList ? "Auto,Auto,*,Auto" : "Auto,Auto,*,Auto,Auto")
            : (isDefaultList ? "Auto,*,Auto" : "Auto,*,Auto,Auto");
        var headerPanel = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse(columnDef)
        };

        var colOffset = canReorder ? 1 : 0;

        // Grip handle for list reordering (only if multiple lists)
        if (canReorder)
        {
            var gripHandle = CreateGripHandle();
            Grid.SetColumn(gripHandle, 0);
            headerPanel.Children.Add(gripHandle);
            SetupListDragHandlers(headerBorder, gripHandle, listName);
        }

        // Collapse chevron button - uses rotation animation
        var chevronBtn = new Button
        {
            Content = "▼",
            Width = 18,
            Height = 18,
            FontSize = 10,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.Parse("#666")),
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Classes = { "chevron" }
        };
        if (isCollapsed)
        {
            chevronBtn.Classes.Add("collapsed");
        }
        ToolTip.SetTip(chevronBtn, isCollapsed ? "Expand list" : "Collapse list");
        chevronBtn.Click += (_, _) => OnToggleListCollapsed(listName, !isCollapsed);
        Grid.SetColumn(chevronBtn, colOffset);
        headerPanel.Children.Add(chevronBtn);

        // List name + summary
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

        Grid.SetColumn(headerStack, colOffset + 1);
        headerPanel.Children.Add(headerStack);

        // Add button
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
        Grid.SetColumn(addBtn, colOffset + 2);
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

            var renameItem = new MenuItem { Header = "Rename list" };
            renameItem.Click += (_, _) => StartListRename(listName);
            contextMenu.Items.Add(renameItem);

            contextMenu.Items.Add(new Separator());

            var deleteItem = new MenuItem
            {
                Header = "Delete list",
                Foreground = new SolidColorBrush(Color.Parse("#FF6B6B"))
            };
            deleteItem.Click += (_, _) => OnDeleteListClicked(listName);
            contextMenu.Items.Add(deleteItem);

            menuBtn.ContextMenu = contextMenu;
            menuBtn.Click += (_, _) => contextMenu.Open(menuBtn);
            Grid.SetColumn(menuBtn, colOffset + 3);
            headerPanel.Children.Add(menuBtn);
        }

        headerBorder.Child = headerPanel;
        TaskListPanel.Children.Add(headerBorder);
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

    private void StartListRename(string listName)
    {
        CancelInlineEdit();
        _renamingList = listName;
        BuildTaskList();
    }

    private void SubmitListRename(string oldName, string? newName)
    {
        if (string.IsNullOrWhiteSpace(newName))
        {
            CancelInlineEdit();
            BuildTaskList();
            return;
        }

        try
        {
            ListManager.RenameList(oldName, newName.Trim());
            CancelInlineEdit();
            RefreshTasks();
            StatusText.Text = $"Renamed list '{oldName}' to '{newName.Trim()}'";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
            CancelInlineEdit();
            BuildTaskList();
        }
    }

    private Border CreateInlineListRenameField(string listName)
    {
        var border = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#3A3A3A")),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 6),
            Margin = new Thickness(4, 8, 4, 4),
            Classes = { "inlineInput", "entering" }
        };

        Dispatcher.UIThread.Post(() =>
        {
            border.Classes.Remove("entering");
        }, DispatcherPriority.Render);

        var textBox = new TextBox
        {
            Text = listName,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#888")),
            AcceptsReturn = false,
            MaxLength = 50
        };

        var submitted = false;

        textBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                if (!submitted)
                {
                    submitted = true;
                    SubmitListRename(listName, textBox.Text);
                }
            }
            else if (e.Key == Key.Escape)
            {
                submitted = true;
                CancelInlineEdit();
                BuildTaskList();
            }
        };

        var capturedShowCount = _showCount;
        textBox.LostFocus += (_, _) =>
        {
            if (capturedShowCount != _showCount)
                return;

            if (submitted)
                return;

            submitted = true;
            SubmitListRename(listName, textBox.Text);
        };

        _activeInlineEditor = textBox;
        border.Child = textBox;

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            textBox.Focus();
            textBox.SelectAll();
        }, Avalonia.Threading.DispatcherPriority.Background);

        return border;
    }

    private Border CreateInlineAddField(string listName)
    {
        var border = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#3A3A3A")),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8),
            Margin = new Thickness(0, 2),
            Classes = { "inlineInput", "entering" }
        };

        // Trigger entrance animation after render
        Dispatcher.UIThread.Post(() =>
        {
            border.Classes.Remove("entering");
        }, DispatcherPriority.Render);

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

        var submitted = false;  // Local flag to prevent double submission

        textBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                e.Handled = true;
                if (!submitted)
                {
                    submitted = true;
                    SubmitInlineAdd(textBox.Text, listName);
                }
            }
            else if (e.Key == Key.Escape)
            {
                submitted = true;  // Mark as handled to prevent LostFocus submission
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

            // Guard against double submission (Enter/Escape already processed, or Deactivated already saved)
            if (submitted || _inlineAddSubmitted)
                return;

            submitted = true;
            _inlineAddSubmitted = true;
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
            Margin = new Thickness(0, 2),
            Classes = { "inlineInput", "entering" }
        };

        // Trigger entrance animation after render
        Dispatcher.UIThread.Post(() =>
        {
            border.Classes.Remove("entering");
        }, DispatcherPriority.Render);

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
        _inlineAddSubmitted = false; // Reset for new inline add

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
            Margin = new Thickness(4, 4, 4, 8),
            Classes = { "inlineInput", "entering" }
        };

        // Trigger entrance animation after render
        Dispatcher.UIThread.Post(() =>
        {
            border.Classes.Remove("entering");
        }, DispatcherPriority.Render);

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
            // Parse inline metadata from description
            var parsed = TaskDescriptionParser.Parse(text.Trim());
            var task = TodoTask.CreateTodoTask(parsed.Description, listName);
            if (parsed.Priority.HasValue)
                task = task.SetPriority(parsed.Priority.Value);
            if (parsed.DueDate.HasValue)
                task = task.SetDueDate(parsed.DueDate.Value);
            if (parsed.Tags.Length > 0)
                task = task.SetTags(parsed.Tags);

            _newlyAddedTaskId = task.Id; // Track for entrance animation
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
        _renamingList = null;
    }

    /// <summary>
    /// Saves any pending inline add without refreshing UI (called before window hides).
    /// Sets _inlineAddSubmitted to prevent LostFocus from double-submitting.
    /// </summary>
    private void SavePendingInlineAdd()
    {
        if (_activeInlineEditor == null)
            return;

        var text = _activeInlineEditor.Text;
        if (string.IsNullOrWhiteSpace(text))
            return;

        // Mark as submitted to prevent LostFocus from creating duplicate
        _inlineAddSubmitted = true;

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
            // Parse inline metadata from description
            var parsed = TaskDescriptionParser.Parse(text.Trim());
            var task = TodoTask.CreateTodoTask(parsed.Description, _addingToList);
            if (parsed.Priority.HasValue)
                task = task.SetPriority(parsed.Priority.Value);
            if (parsed.DueDate.HasValue)
                task = task.SetDueDate(parsed.DueDate.Value);
            if (parsed.Tags.Length > 0)
                task = task.SetTags(parsed.Tags);

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
            Margin = new Thickness(0, 2),
            Classes = { "taskItem" }
        };

        // Track border for animations
        _taskBorders[task.Id] = border;

        // Add checked class if task is checked
        if (task.IsChecked)
        {
            border.Classes.Add("checked");
        }

        // Animate entrance for newly added task
        if (task.Id == _newlyAddedTaskId)
        {
            border.Classes.Add("entering");
            Dispatcher.UIThread.Post(() =>
            {
                border.Classes.Remove("entering");
            }, DispatcherPriority.Render);
            _newlyAddedTaskId = null;
        }

        // Click on task content area copies ID to clipboard
        border.PointerPressed += async (sender, e) =>
        {
            // Only handle left click, and not if it originated from checkbox or menu button
            var point = e.GetCurrentPoint(border);
            if (point.Properties.IsLeftButtonPressed)
            {
                // Check if click is in the content area (column 1), not checkbox (0) or menu (2)
                var pos = e.GetPosition(border);
                var checkboxWidth = 40; // approximate checkbox + margin width
                var menuWidth = 40; // approximate menu button width
                var borderWidth = border.Bounds.Width;

                if (pos.X > checkboxWidth && pos.X < borderWidth - menuWidth)
                {
                    await CopyTaskIdToClipboard(task.Id);
                    e.Handled = true;
                }
            }
        };

        var grid = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("Auto,Auto,*,Auto")
        };

        // Grip handle for drag-and-drop (only shown when > 1 task in list)
        var tasksInSameList = _tasks.Count(t => t.ListName == task.ListName);
        if (tasksInSameList > 1)
        {
            var gripHandle = CreateGripHandle();
            Grid.SetColumn(gripHandle, 0);
            grid.Children.Add(gripHandle);
            SetupTaskDragHandlers(border, gripHandle, task);
        }

        // Checkbox
        var checkbox = new CheckBox
        {
            IsChecked = task.IsChecked,
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top
        };
        Grid.SetColumn(checkbox, 1);
        checkbox.Click += (_, _) => OnCheckboxClicked(task, checkbox);
        grid.Children.Add(checkbox);

        // Task content (title + description + metadata)
        var contentPanel = new StackPanel
        {
            Spacing = 2
        };
        Grid.SetColumn(contentPanel, 2);

        // Title row with priority indicator - use Grid to constrain width and enable wrapping
        var titleRow = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("Auto,*")
        };

        // Priority indicator (column 0)
        if (task.HasPriority)
        {
            var priorityIndicator = new TextBlock
            {
                Text = task.PriorityDisplay,
                FontWeight = FontWeight.Bold,
                FontSize = 13,
                Foreground = task.PriorityColor,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
                Margin = new Thickness(0, 0, 6, 0)
            };
            Grid.SetColumn(priorityIndicator, 0);
            titleRow.Children.Add(priorityIndicator);
        }

        var titleColor = task.IsChecked ? "#666" : "#FFF";
        var title = new TextBlock
        {
            Text = task.DisplayText,
            FontWeight = FontWeight.SemiBold,
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.Parse(titleColor)),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top
        };
        Grid.SetColumn(title, task.HasPriority ? 1 : 0);
        if (!task.HasPriority)
            Grid.SetColumnSpan(title, 2);
        titleRow.Children.Add(title);
        contentPanel.Children.Add(titleRow);

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

        // Due date display
        if (task.HasDueDate)
        {
            var dueDate = new TextBlock
            {
                Text = task.DueDateDisplay,
                FontSize = 10,
                Foreground = task.DueDateColor,
                Margin = new Thickness(0, 2, 0, 0)
            };
            contentPanel.Children.Add(dueDate);
        }

        // Tags display - pill badges
        if (task.HasTags)
        {
            var tagsPanel = new WrapPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Margin = new Thickness(0, 4, 0, 0)
            };

            foreach (var tag in task.Tags!)
            {
                var tagPill = new Border
                {
                    Background = new SolidColorBrush(GetTagColor(tag)),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(6, 2),
                    Margin = new Thickness(0, 0, 4, 2)
                };

                var tagText = new TextBlock
                {
                    Text = $"#{tag}",
                    FontSize = 10,
                    FontWeight = FontWeight.Medium,
                    Foreground = new SolidColorBrush(Color.Parse("#FFF"))
                };

                tagPill.Child = tagText;
                tagsPanel.Children.Add(tagPill);
            }

            contentPanel.Children.Add(tagsPanel);
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

        Grid.SetColumn(menuBtn, 3);
        grid.Children.Add(menuBtn);

        border.Child = grid;
        return border;
    }

    private async void OnCheckboxClicked(TodoTaskViewModel task, CheckBox checkbox)
    {
        // Visual feedback on the task border
        if (_taskBorders.TryGetValue(task.Id, out var border))
        {
            if (checkbox.IsChecked == true)
            {
                border.Classes.Add("checked");
            }
            else
            {
                border.Classes.Remove("checked");
            }
        }

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

            // Delayed refresh to allow animation to complete
            await Task.Delay(150);
            Dispatcher.UIThread.Post(() => RefreshTasks(), DispatcherPriority.Background);
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

    private async void OnDeleteTaskClicked(TodoTaskViewModel task)
    {
        // Animate out before deleting
        if (_taskBorders.TryGetValue(task.Id, out var border))
        {
            border.Classes.Add("exiting");
            await Task.Delay(150); // Wait for animation
        }

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

    private async Task CopyTaskIdToClipboard(string taskId)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard != null)
        {
            await clipboard.SetTextAsync(taskId);
            StatusText.Text = $"Copied: {taskId}";
        }
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

        // Reset to hidden state before showing
        PopupContent.Classes.Remove("visible");

        Show();

        // Trigger fade-in animation after render
        Dispatcher.UIThread.Post(() =>
        {
            PopupContent.Classes.Add("visible");
        }, DispatcherPriority.Render);

        // Activate app and window on macOS - temporarily become regular app to get focus
        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
        {
            SetActivationPolicy(0); // Regular - shows in Dock but can focus
            ActivateApp();
            Dispatcher.UIThread.Post(() =>
            {
                MakeKeyWindow(this);
            }, DispatcherPriority.Render);
        }
        else
        {
            Activate();
        }
    }

    private async Task HideWithAnimation()
    {
        // Trigger fade-out animation
        PopupContent.Classes.Remove("visible");

        // Wait for animation to complete
        await Task.Delay(150);

        Hide();

        // Hide from Dock when popup closes
        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
        {
            HideFromDock();
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

    /// <summary>
    /// Returns a consistent color for a tag based on its hash.
    /// </summary>
    private static Color GetTagColor(string tag)
    {
        // Palette of nice tag colors (muted but visible)
        var colors = new[]
        {
            "#3B82F6", // Blue
            "#10B981", // Emerald
            "#F59E0B", // Amber
            "#EF4444", // Red
            "#8B5CF6", // Violet
            "#EC4899", // Pink
            "#06B6D4", // Cyan
            "#84CC16", // Lime
            "#F97316", // Orange
            "#6366F1", // Indigo
        };

        var index = Math.Abs(tag.GetHashCode()) % colors.Length;
        return Color.Parse(colors[index]);
    }

    [System.Runtime.InteropServices.DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_SetPtr(IntPtr receiver, IntPtr selector, IntPtr arg);

    #region Drag-and-Drop Implementation (Custom Smooth Drag)

    // Drop indicator for visual feedback
    private Border? _dropIndicator;
    private int _dropTargetIndex = -1;
    private int _listDropTargetIndex = -1;

    // Ghost element that follows cursor during drag
    private Border? _dragGhost;
    private Point _dragOffset; // Offset from cursor to ghost origin

    // Drag start tracking (before threshold is crossed)
    private Point? _dragStartPoint;
    private Border? _pendingDragBorder;
    private TodoTaskViewModel? _pendingDragTask;
    private string? _pendingDragListName;
    private Panel? _pendingGripHandle;

    // Active drag state (after threshold is crossed)
    private Border? _draggedBorder;
    private TodoTaskViewModel? _draggedTask;
    private string? _draggedListName;
    private bool _isDragging;
    private IPointer? _capturedPointer;

    // Track collapsed lists during list drag
    private List<(StackPanel panel, Button chevron)>? _collapsedDuringDrag;

    // Map list names to their task panels for drop targeting
    private Dictionary<string, StackPanel> _listTaskPanels = new();

    // Canvas overlay for drag ghost (stored reference since FindControl doesn't work for dynamic controls)
    private Canvas? _dragCanvas;

    /// <summary>
    /// Creates a 6-dot grip handle for drag-and-drop reordering.
    /// The handle has an expanded hit area for easier targeting.
    /// </summary>
    private Panel CreateGripHandle()
    {
        // Outer container with expanded hit area
        var outerPanel = new Panel
        {
            Width = 20, // Larger hit area
            MinHeight = 24,
            Background = Brushes.Transparent, // Transparent but still captures input
            Margin = new Thickness(0, 0, 4, 0),
            Classes = { "gripHandle" }
        };

        // Inner panel with the visual dots
        var dotsPanel = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Vertical,
            Spacing = 2,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
        };

        // Create 3 rows of 2 dots each
        for (var row = 0; row < 3; row++)
        {
            var dotRow = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 2,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
            };

            for (var col = 0; col < 2; col++)
            {
                var dot = new Border
                {
                    Width = 3,
                    Height = 3,
                    CornerRadius = new CornerRadius(1.5),
                    Background = new SolidColorBrush(Color.Parse("#666")),
                    Classes = { "gripDot" }
                };
                dotRow.Children.Add(dot);
            }

            dotsPanel.Children.Add(dotRow);
        }

        outerPanel.Children.Add(dotsPanel);
        return outerPanel;
    }

    /// <summary>
    /// Sets up smooth drag handlers for a task item using pointer capture and ghost element.
    /// </summary>
    private void SetupTaskDragHandlers(Border border, Panel gripHandle, TodoTaskViewModel task)
    {
        var capturedGeneration = _generationId;

        gripHandle.PointerPressed += (sender, e) =>
        {
            if (capturedGeneration != _generationId) return;
            if (_isDragging) return; // Already dragging
            if (!e.GetCurrentPoint(gripHandle).Properties.IsLeftButtonPressed) return;

            // Visual feedback - highlight the grip dots while pressed
            foreach (var dot in gripHandle.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("gripDot")))
            {
                dot.Background = new SolidColorBrush(Color.Parse("#0A84FF"));
            }

            // Set up pending drag - window handlers will detect threshold and start
            _dragStartPoint = e.GetPosition(this);
            _pendingDragBorder = border;
            _pendingDragTask = task;
            _pendingDragListName = null;
            _pendingGripHandle = gripHandle;
            e.Handled = true;
        };
    }

    private void StartTaskDrag(Border border, TodoTaskViewModel task, PointerEventArgs e)
    {
        // Suspend file watcher during drag
        if (Application.Current is App app)
        {
            app.SuspendFileWatcher();
        }

        _state = PopupState.Dragging;
        _isDragging = true;
        _draggedBorder = border;
        _draggedTask = task;

        // Capture pointer at window level for reliable tracking
        e.Pointer.Capture(this);
        _capturedPointer = e.Pointer;

        // Calculate offset from cursor to border origin
        var borderPos = border.TranslatePoint(new Point(0, 0), this) ?? new Point(0, 0);
        var cursorPos = e.GetPosition(this);
        _dragOffset = new Point(cursorPos.X - borderPos.X, cursorPos.Y - borderPos.Y);

        // Create ghost element
        CreateDragGhost(border, task);

        // Dim the original
        border.Opacity = 0.3;

        // Create drop indicator
        EnsureDropIndicator();
    }

    private void CreateDragGhost(Border original, TodoTaskViewModel task)
    {
        // Create a simple ghost representation
        _dragGhost = new Border
        {
            Width = original.Bounds.Width,
            Background = new SolidColorBrush(Color.Parse("#3A3A3A")),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8),
            Opacity = 0.9,
            BoxShadow = new BoxShadows(new BoxShadow { Blur = 16, OffsetY = 4, Color = Color.Parse("#60000000") }),
            Child = new TextBlock
            {
                Text = task.DisplayText,
                Foreground = Brushes.White,
                FontWeight = FontWeight.SemiBold,
                FontSize = 13,
                MaxWidth = original.Bounds.Width - 20,
                TextTrimming = TextTrimming.CharacterEllipsis
            }
        };

        // Add to a canvas overlay
        var canvas = GetOrCreateDragCanvas();
        canvas.Children.Add(_dragGhost);

        // Position at original location initially
        var pos = original.TranslatePoint(new Point(0, 0), this) ?? new Point(0, 0);
        Canvas.SetLeft(_dragGhost, pos.X);
        Canvas.SetTop(_dragGhost, pos.Y);
    }

    private Canvas GetOrCreateDragCanvas()
    {
        // Return existing canvas if we have one
        if (_dragCanvas != null) return _dragCanvas;

        // Create overlay canvas for drag ghost
        _dragCanvas = new Canvas
        {
            IsHitTestVisible = false // Don't interfere with pointer events
        };

        // Add to popup content as overlay
        if (PopupContent.Child is Grid grid)
        {
            grid.Children.Add(_dragCanvas);
        }
        else
        {
            // Wrap existing content in grid
            var existingChild = PopupContent.Child;
            PopupContent.Child = null;
            var newGrid = new Grid();
            if (existingChild != null)
                newGrid.Children.Add(existingChild);
            newGrid.Children.Add(_dragCanvas);
            PopupContent.Child = newGrid;
        }

        return _dragCanvas;
    }

    private void UpdateTaskDropTarget(Point panelPoint)
    {
        if (_draggedTask == null) return;

        // Find the task panel for this list
        if (!_listTaskPanels.TryGetValue(_draggedTask.ListName, out var tasksPanel))
            return;

        var localPoint = TaskListPanel.TranslatePoint(panelPoint, tasksPanel);
        if (!localPoint.HasValue) return;

        var newIndex = CalculateTaskDropIndex(tasksPanel, localPoint.Value);

        if (newIndex != _dropTargetIndex)
        {
            _dropTargetIndex = newIndex;
            UpdateTaskDropIndicator(tasksPanel, newIndex);
        }
    }

    /// <summary>
    /// Registers a task panel for drop targeting (used by custom drag implementation).
    /// </summary>
    private void SetupTaskPanelDropTarget(StackPanel tasksPanel, string listName)
    {
        // Store reference for drop calculations
        _listTaskPanels[listName] = tasksPanel;
    }

    private int CalculateTaskDropIndex(StackPanel tasksPanel, Point point)
    {
        var taskBorders = tasksPanel.Children
            .OfType<Border>()
            .Where(b => b.Classes.Contains("taskItem"))
            .ToList();

        if (taskBorders.Count == 0) return 0;

        var y = point.Y;

        for (var i = 0; i < taskBorders.Count; i++)
        {
            var bounds = taskBorders[i].Bounds;
            var midY = bounds.Top + bounds.Height / 2;

            if (y < midY)
            {
                return i;
            }
        }

        return taskBorders.Count;
    }

    private void UpdateTaskDropIndicator(StackPanel tasksPanel, int index)
    {
        EnsureDropIndicator();
        if (_dropIndicator == null) return;

        // Remove from previous location
        if (_dropIndicator.Parent is Panel oldParent)
        {
            oldParent.Children.Remove(_dropIndicator);
        }

        var taskBorders = tasksPanel.Children
            .OfType<Border>()
            .Where(b => b.Classes.Contains("taskItem"))
            .ToList();

        // Calculate insertion index in panel
        int insertIndex;
        if (index < taskBorders.Count)
        {
            var targetBorder = taskBorders[index];
            insertIndex = tasksPanel.Children.IndexOf(targetBorder);
        }
        else if (taskBorders.Count > 0)
        {
            var lastBorder = taskBorders[^1];
            insertIndex = tasksPanel.Children.IndexOf(lastBorder) + 1;
        }
        else
        {
            insertIndex = tasksPanel.Children.Count;
        }

        tasksPanel.Children.Insert(insertIndex, _dropIndicator);
        _dropIndicator.Classes.Add("visible");
    }

    private void CompleteTaskDrag(TodoTaskViewModel task)
    {
        if (_dropTargetIndex < 0) return;

        try
        {
            // Get tasks in the same list
            var tasksInList = _tasks.Where(t => t.ListName == task.ListName).ToList();
            var currentIndex = tasksInList.FindIndex(t => t.Id == task.Id);

            // Only reorder if position actually changed
            if (currentIndex != _dropTargetIndex && _dropTargetIndex != currentIndex + 1)
            {
                // Adjust target index if dragging down
                var adjustedIndex = _dropTargetIndex;
                if (_dropTargetIndex > currentIndex)
                {
                    adjustedIndex--;
                }

                TodoTaskList.ReorderTask(task.Id, adjustedIndex);
                RefreshTasks();
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
    }

    /// <summary>
    /// Sets up smooth drag handlers for a list header using pointer capture and ghost element.
    /// </summary>
    private void SetupListDragHandlers(Border border, Panel gripHandle, string listName)
    {
        var capturedGeneration = _generationId;

        gripHandle.PointerPressed += (sender, e) =>
        {
            if (capturedGeneration != _generationId) return;
            if (_isDragging) return;
            if (!e.GetCurrentPoint(gripHandle).Properties.IsLeftButtonPressed) return;

            // Visual feedback - highlight the grip dots while pressed
            foreach (var dot in gripHandle.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("gripDot")))
            {
                dot.Background = new SolidColorBrush(Color.Parse("#0A84FF"));
            }

            // Set up pending drag - window handlers will detect threshold and start
            _dragStartPoint = e.GetPosition(this);
            _pendingDragBorder = border;
            _pendingDragTask = null;
            _pendingDragListName = listName;
            _pendingGripHandle = gripHandle;
            e.Handled = true;
        };
    }

    private void StartListDrag(Border border, string listName, PointerEventArgs e)
    {
        // Suspend file watcher
        if (Application.Current is App app)
        {
            app.SuspendFileWatcher();
        }

        _state = PopupState.Dragging;
        _isDragging = true;
        _draggedBorder = border;
        _draggedListName = listName;

        // Capture pointer
        e.Pointer.Capture(this);
        _capturedPointer = e.Pointer;

        // Calculate offset
        var borderPos = border.TranslatePoint(new Point(0, 0), this) ?? new Point(0, 0);
        var cursorPos = e.GetPosition(this);
        _dragOffset = new Point(cursorPos.X - borderPos.X, cursorPos.Y - borderPos.Y);

        // Collapse all lists visually for easier reordering
        CollapseAllListsForDrag();

        // Create ghost for list header
        CreateListDragGhost(border, listName);

        // Dim the original
        border.Opacity = 0.3;

        // Create drop indicator
        EnsureDropIndicator();
    }

    private void CreateListDragGhost(Border original, string listName)
    {
        _dragGhost = new Border
        {
            Width = original.Bounds.Width,
            Background = new SolidColorBrush(Color.Parse("#3A3A3A")),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 6),
            Opacity = 0.9,
            BoxShadow = new BoxShadows(new BoxShadow { Blur = 16, OffsetY = 4, Color = Color.Parse("#60000000") }),
            Child = new TextBlock
            {
                Text = listName,
                Foreground = new SolidColorBrush(Color.Parse("#888")),
                FontWeight = FontWeight.SemiBold,
                FontSize = 11
            }
        };

        var canvas = GetOrCreateDragCanvas();
        canvas.Children.Add(_dragGhost);

        var pos = original.TranslatePoint(new Point(0, 0), this) ?? new Point(0, 0);
        Canvas.SetLeft(_dragGhost, pos.X);
        Canvas.SetTop(_dragGhost, pos.Y);
    }

    private void UpdateListDropTarget(Point panelPoint)
    {
        if (_draggedListName == null) return;

        var newIndex = CalculateListDropIndex(panelPoint);

        if (newIndex != _listDropTargetIndex)
        {
            _listDropTargetIndex = newIndex;
            UpdateListDropIndicator(newIndex);
        }
    }

    private int CalculateListDropIndex(Point point)
    {
        var listHeaders = TaskListPanel.Children
            .OfType<Border>()
            .Where(b => b.Classes.Contains("listHeader"))
            .ToList();

        if (listHeaders.Count == 0) return 0;

        var y = point.Y;

        for (var i = 0; i < listHeaders.Count; i++)
        {
            var bounds = listHeaders[i].Bounds;
            var midY = bounds.Top + bounds.Height / 2;

            if (y < midY)
            {
                return i;
            }
        }

        return listHeaders.Count;
    }

    private void UpdateListDropIndicator(int index)
    {
        EnsureDropIndicator();
        if (_dropIndicator == null) return;

        // Remove from previous location
        if (_dropIndicator.Parent is Panel oldParent)
        {
            oldParent.Children.Remove(_dropIndicator);
        }

        var listHeaders = TaskListPanel.Children
            .OfType<Border>()
            .Where(b => b.Classes.Contains("listHeader"))
            .ToList();

        if (listHeaders.Count == 0) return;

        int insertIndex;
        if (index < listHeaders.Count)
        {
            var targetHeader = listHeaders[index];
            insertIndex = TaskListPanel.Children.IndexOf(targetHeader);
        }
        else
        {
            // After last list
            var lastHeader = listHeaders[^1];
            var lastHeaderIndex = TaskListPanel.Children.IndexOf(lastHeader);

            insertIndex = lastHeaderIndex + 1;
            if (insertIndex < TaskListPanel.Children.Count &&
                TaskListPanel.Children[insertIndex] is StackPanel sp &&
                sp.Classes.Contains("listTasks"))
            {
                insertIndex++;
            }
        }

        TaskListPanel.Children.Insert(insertIndex, _dropIndicator);
        _dropIndicator.Classes.Add("visible");
    }

    private void CompleteListDrag(string listName)
    {
        if (_listDropTargetIndex < 0) return;

        try
        {
            var allLists = TodoTaskList.GetAllListNames().ToList();
            var currentIndex = allLists.IndexOf(listName);

            if (currentIndex >= 0 && currentIndex != _listDropTargetIndex && _listDropTargetIndex != currentIndex + 1)
            {
                var adjustedIndex = _listDropTargetIndex;
                if (_listDropTargetIndex > currentIndex)
                {
                    adjustedIndex--;
                }

                TodoTaskList.ReorderList(listName, adjustedIndex);
                RefreshTasks();
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
    }

    private void CollapseAllListsForDrag()
    {
        _collapsedDuringDrag = new List<(StackPanel, Button)>();
        Border? currentHeader = null;

        foreach (var child in TaskListPanel.Children)
        {
            if (child is Border b && b.Classes.Contains("listHeader"))
            {
                currentHeader = b;
            }
            else if (child is StackPanel sp && sp.Classes.Contains("listTasks"))
            {
                if (!sp.Classes.Contains("collapsed"))
                {
                    sp.Classes.Add("collapsed");

                    Button? chevron = null;
                    if (currentHeader?.Child is Grid grid)
                    {
                        chevron = grid.Children.OfType<Button>()
                            .FirstOrDefault(btn => btn.Classes.Contains("chevron"));
                        chevron?.Classes.Add("collapsed");
                    }

                    _collapsedDuringDrag.Add((sp, chevron!));
                }
                currentHeader = null;
            }
        }
    }

    private void RestoreCollapsedLists()
    {
        if (_collapsedDuringDrag == null) return;

        foreach (var (panel, chevron) in _collapsedDuringDrag)
        {
            panel.Classes.Remove("collapsed");
            chevron?.Classes.Remove("collapsed");
        }
        _collapsedDuringDrag = null;
    }

    private void EnsureDropIndicator()
    {
        if (_dropIndicator == null)
        {
            _dropIndicator = new Border
            {
                Classes = { "dropIndicator" }
            };
        }
    }

    private void RemoveDropIndicator()
    {
        if (_dropIndicator?.Parent is Panel parent)
        {
            parent.Children.Remove(_dropIndicator);
        }
        _dropIndicator?.Classes.Remove("visible");
    }

    private void CleanupDrag()
    {
        // Clear pending drag state
        ClearPendingDrag();

        // Remove ghost from canvas
        if (_dragGhost != null)
        {
            _dragCanvas?.Children.Remove(_dragGhost);
            _dragGhost = null;
        }

        // Restore original border opacity
        if (_draggedBorder != null)
        {
            _draggedBorder.Opacity = 1.0;
            _draggedBorder = null;
        }

        // Release pointer capture
        if (_capturedPointer != null)
        {
            _capturedPointer.Capture(null);
            _capturedPointer = null;
        }

        // Restore collapsed lists
        RestoreCollapsedLists();

        // Remove drop indicator
        RemoveDropIndicator();

        // Reset state
        _draggedTask = null;
        _draggedListName = null;
        _dropTargetIndex = -1;
        _listDropTargetIndex = -1;
        _isDragging = false;

        // Update popup state
        var hadPendingRefresh = _state == PopupState.RefreshPending;
        _state = PopupState.Idle;

        // Resume file watcher
        if (Application.Current is App app)
        {
            app.ResumeFileWatcher();
        }

        // Handle pending refresh
        if (hadPendingRefresh)
        {
            DoRefreshTasks();
        }
    }

    private void CancelDrag()
    {
        CleanupDrag();
    }

    #endregion
}
