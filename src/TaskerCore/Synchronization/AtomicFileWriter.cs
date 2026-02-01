namespace TaskerCore.Synchronization;

using System.Text.Json;

/// <summary>
/// Provides atomic file write operations using the write-to-temp-then-rename pattern.
/// This prevents file corruption from partial writes or crashes during write.
/// </summary>
public static class AtomicFileWriter
{
    /// <summary>
    /// Atomically writes JSON data to a file.
    /// </summary>
    public static void WriteJson<T>(string filePath, T data, JsonSerializerOptions? options = null)
    {
        var json = JsonSerializer.Serialize(data, options);
        WriteText(filePath, json);
    }

    /// <summary>
    /// Atomically writes text content to a file.
    /// </summary>
    public static void WriteText(string filePath, string content)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        // Write to temp file in SAME directory (atomic rename requires same filesystem)
        var tempPath = Path.Combine(
            directory ?? ".",
            $".{Path.GetFileName(filePath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            // Write with explicit flush to ensure data hits disk
            using (var stream = new FileStream(tempPath, FileMode.Create,
                FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(content);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            // Atomic rename (on POSIX systems, rename() is atomic)
            File.Move(tempPath, filePath, overwrite: true);
        }
        catch
        {
            try { File.Delete(tempPath); } catch { }
            throw;
        }
    }

    /// <summary>
    /// Atomically writes binary data to a file.
    /// </summary>
    public static void WriteBytes(string filePath, byte[] data)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var tempPath = Path.Combine(
            directory ?? ".",
            $".{Path.GetFileName(filePath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (var stream = new FileStream(tempPath, FileMode.Create,
                FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(data);
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, filePath, overwrite: true);
        }
        catch
        {
            try { File.Delete(tempPath); } catch { }
            throw;
        }
    }
}
