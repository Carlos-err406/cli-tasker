---
title: Inconsistent task sort order across UI consumers — use GetSortedTasks everywhere
category: logic-errors
tags: [sorting, consistency, tray, tui, ux]
module: TaskerTray, Tui
date: 2026-02-06
severity: medium
symptoms:
  - Checked tasks appear mixed with unchecked tasks in tray popup
  - Task order differs between CLI and tray app
  - Priority and due date not respected in tray sort order
root_cause: >
  TaskListPopup.DoRefreshTasks() and TuiApp.LoadTasks() called GetAllTasks()
  (unsorted) instead of GetSortedTasks(), while the CLI ListCommand already
  used GetSortedTasks(). Each consumer had its own ad-hoc sort or no sort at all.
related:
  - docs/solutions/ui-bugs/hide-irrelevant-controls-in-filtered-view.md
---

# Inconsistent Task Sort Order Across Consumers

## Problem

The tray popup and TUI showed tasks in storage order (insertion order), while the CLI sorted by IsChecked, Priority, DueDate, and CreatedAt. The tray's `TaskListViewModel` had a partial sort (only IsChecked + CreatedAt), missing priority and due date.

## Solution

Replace all `GetAllTasks()` calls in display code with `GetSortedTasks()`, which applies the canonical sort: **IsChecked → Priority → DueDate → CreatedAt**.

```csharp
// WRONG — unsorted, each consumer rolls its own sort
var tasks = taskList.GetAllTasks();

// RIGHT — canonical sort from TodoTaskList
var tasks = taskList.GetSortedTasks();
```

Three files fixed:
- `TaskListPopup.axaml.cs` — `DoRefreshTasks()`
- `TaskListViewModel.cs` — `LoadTasks()`
- `TuiApp.cs` — `LoadTasks()`

## Prevention

When displaying tasks to users, always use `GetSortedTasks()`. Reserve `GetAllTasks()` for internal operations (undo, reorder, migration) where storage order matters.
