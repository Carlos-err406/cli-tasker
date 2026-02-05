using System;
using Avalonia.Threading;
using TaskerCore;
using TaskerCore.Synchronization;
using TaskerCore.Undo;

namespace TaskerTray.Services;

/// <summary>
/// Monitors task files for external changes (e.g., from CLI).
/// Notifies the app when changes are detected so it can refresh.
/// </summary>
public class FileWatcherService : IDisposable
{
    private readonly DebouncedFileWatcher _watcher;
    private bool _disposed;
    private bool _suspended;
    private bool _pendingChange;

    public event Action? ExternalChangeDetected;

    public FileWatcherService()
    {
        // Watch the main tasks file
        _watcher = new DebouncedFileWatcher(
            StoragePaths.AllTasksPath,
            OnFileChanged,
            TimeSpan.FromMilliseconds(200) // Debounce time
        );
    }

    private void OnFileChanged(string filePath)
    {
        // Dispatch to UI thread since file watcher fires on a background thread
        Dispatcher.UIThread.Post(() =>
        {
            if (_suspended)
            {
                _pendingChange = true;
                return;
            }

            // Invalidate undo history since external changes may have occurred
            // The undo history is local to this process and may now be out of sync
            UndoManager.Instance.ClearHistory();

            ExternalChangeDetected?.Invoke();
        });
    }

    /// <summary>
    /// Suspends notifications during drag operations to prevent race conditions.
    /// </summary>
    public void SuspendNotifications() => _suspended = true;

    /// <summary>
    /// Resumes notifications and fires pending change if one occurred during suspension.
    /// </summary>
    public void ResumeNotifications()
    {
        _suspended = false;
        if (_pendingChange)
        {
            _pendingChange = false;
            UndoManager.Instance.ClearHistory();
            ExternalChangeDetected?.Invoke();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _watcher.Dispose();
    }
}
