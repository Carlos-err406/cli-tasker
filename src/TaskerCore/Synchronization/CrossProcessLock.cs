namespace TaskerCore.Synchronization;

/// <summary>
/// Cross-process lock using Named Mutex for robust synchronization
/// between CLI and GUI processes accessing the same data files.
/// </summary>
public sealed class CrossProcessLock : IDisposable
{
    private readonly Mutex _mutex;
    private readonly string _name;
    private bool _hasHandle;
    private bool _disposed;

    public CrossProcessLock(string resourceName)
    {
        _name = $"cli-tasker-{SanitizeName(resourceName)}";
        _mutex = new Mutex(initiallyOwned: false, name: _name, out _);
    }

    /// <summary>
    /// Attempts to acquire the lock within the specified timeout.
    /// </summary>
    /// <returns>True if the lock was acquired, false otherwise.</returns>
    public bool TryAcquire(TimeSpan timeout)
    {
        if (_hasHandle) return true;

        try
        {
            _hasHandle = _mutex.WaitOne(timeout);
            return _hasHandle;
        }
        catch (AbandonedMutexException)
        {
            // Previous holder crashed - we now own it
            _hasHandle = true;
            return true;
        }
    }

    /// <summary>
    /// Acquires the lock, throwing if the timeout is exceeded.
    /// </summary>
    /// <exception cref="TimeoutException">Thrown if the lock cannot be acquired within the timeout.</exception>
    public void Acquire(TimeSpan timeout)
    {
        if (!TryAcquire(timeout))
        {
            throw new TimeoutException(
                $"Could not acquire lock '{_name}' within {timeout.TotalMilliseconds}ms.");
        }
    }

    /// <summary>
    /// Releases the lock if held.
    /// </summary>
    public void Release()
    {
        if (_hasHandle)
        {
            _mutex.ReleaseMutex();
            _hasHandle = false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Release();
        _mutex.Dispose();
    }

    private static string SanitizeName(string name) =>
        name.Replace('/', '_').Replace('\\', '_').Replace(':', '_');
}
