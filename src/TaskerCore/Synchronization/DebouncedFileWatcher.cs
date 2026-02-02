namespace TaskerCore.Synchronization;

using System.Security.Cryptography;

/// <summary>
/// File watcher with debouncing and polling fallback.
/// Uses FileSystemWatcher for immediate notification with a polling
/// fallback every 2 seconds to catch missed events (common on macOS).
/// </summary>
public sealed class DebouncedFileWatcher : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly Timer _pollingTimer;
    private readonly TimeSpan _debounceTime;
    private readonly Action<string> _onChanged;

    private CancellationTokenSource? _debounceCts;
    private readonly object _lock = new();
    private DateTime _lastKnownModified = DateTime.MinValue;
    private string _lastKnownHash = "";
    private bool _disposed;

    /// <summary>
    /// Creates a new file watcher with hybrid watching and polling.
    /// </summary>
    /// <param name="filePath">Path to the file to watch.</param>
    /// <param name="onChanged">Callback invoked when the file changes.</param>
    /// <param name="debounceTime">Time to debounce rapid events. Defaults to 100ms.</param>
    public DebouncedFileWatcher(
        string filePath,
        Action<string> onChanged,
        TimeSpan? debounceTime = null)
    {
        _debounceTime = debounceTime ?? TimeSpan.FromMilliseconds(100);
        _onChanged = onChanged;

        var directory = Path.GetDirectoryName(filePath)!;
        var fileName = Path.GetFileName(filePath);

        // Primary: FileSystemWatcher for immediate notification
        _watcher = new FileSystemWatcher(directory, fileName)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true
        };
        _watcher.Changed += OnFileEvent;
        _watcher.Created += OnFileEvent;
        _watcher.Error += OnWatcherError;

        // Backup: Polling every 2 seconds to catch missed events
        _pollingTimer = new Timer(_ => CheckForChanges(filePath), null,
            TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));

        // Initialize baseline
        UpdateBaseline(filePath);
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        // Debounce rapid events
        lock (_lock)
        {
            if (_disposed) return;
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = new CancellationTokenSource();

            var token = _debounceCts.Token;
            Task.Delay(_debounceTime, token).ContinueWith(t =>
            {
                if (!t.IsCanceled && !_disposed)
                    CheckForChanges(e.FullPath);
            }, TaskScheduler.Default);
        }
    }

    private void CheckForChanges(string path)
    {
        try
        {
            if (!File.Exists(path)) return;

            var info = new FileInfo(path);
            if (info.LastWriteTimeUtc <= _lastKnownModified) return;

            // Verify content actually changed (not just timestamp)
            var currentHash = ComputeFileHash(path);
            if (currentHash == _lastKnownHash) return;

            _lastKnownModified = info.LastWriteTimeUtc;
            _lastKnownHash = currentHash;
            _onChanged(path);
        }
        catch (IOException) { /* File may be locked - try next poll */ }
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        // Restart watcher on error
        try
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.EnableRaisingEvents = true;
        }
        catch { }
    }

    private void UpdateBaseline(string path)
    {
        if (File.Exists(path))
        {
            _lastKnownModified = new FileInfo(path).LastWriteTimeUtc;
            _lastKnownHash = ComputeFileHash(path);
        }
    }

    /// <summary>
    /// Updates the baseline to the current file state.
    /// Call this after making your own changes to prevent
    /// triggering the callback for self-initiated changes.
    /// </summary>
    public void RefreshBaseline()
    {
        if (File.Exists(StoragePaths.AllTasksPath))
        {
            UpdateBaseline(StoragePaths.AllTasksPath);
        }
    }

    private static string ComputeFileHash(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var hash = SHA256.HashData(stream);
            return Convert.ToHexString(hash);
        }
        catch
        {
            return "";
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _pollingTimer.Dispose();
            _watcher.Dispose();
        }
    }
}
