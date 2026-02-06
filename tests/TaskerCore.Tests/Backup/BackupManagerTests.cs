namespace TaskerCore.Tests.Backup;

using TaskerCore.Backup;
using TaskerCore.Data;
using TaskerCore.Exceptions;

/// <summary>
/// Collection definition for backup tests - runs sequentially to avoid conflicts.
/// </summary>
[CollectionDefinition("BackupTests")]
public class BackupTestsCollection : ICollectionFixture<BackupTestFixture> { }

public class BackupTestFixture : IDisposable
{
    public string BaseDirectory { get; }

    public BackupTestFixture()
    {
        BaseDirectory = Path.Combine(Path.GetTempPath(), $"backup-tests-base-{Guid.NewGuid()}");
        Directory.CreateDirectory(BaseDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(BaseDirectory))
        {
            try { Directory.Delete(BaseDirectory, recursive: true); }
            catch { /* Ignore cleanup errors */ }
        }
    }
}

[Collection("BackupTests")]
public class BackupManagerTests : IDisposable
{
    private readonly string _testDir;

    public BackupManagerTests(BackupTestFixture fixture)
    {
        // Create a fresh test directory for each test
        _testDir = Path.Combine(Path.GetTempPath(), $"backup-tests-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDir);
        StoragePaths.SetDirectory(_testDir);
    }

    public void Dispose()
    {
        // Wait a moment for file handles to be released
        Thread.Sleep(50);
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    [Fact]
    public void CreateBackup_WhenNoTasksFile_DoesNotCreateBackup()
    {
        // Arrange - no tasks file exists

        // Act
        BackupManager.CreateBackup();

        // Assert
        var backups = BackupManager.ListBackups();
        Assert.Empty(backups);
    }

    [Fact]
    public void CreateBackup_WhenTasksFileExists_CreatesVersionBackup()
    {
        // Arrange - create a tasks file
        CreateTasksFile();

        // Act
        BackupManager.CreateBackup();

        // Assert - should have at least one version backup (also creates daily)
        var backups = BackupManager.ListBackups();
        Assert.NotEmpty(backups);
        var versionBackups = backups.Where(b => !b.IsDaily).ToList();
        Assert.Single(versionBackups);
    }

    [Fact]
    public void CreateBackup_CreatesDailyBackupOnce()
    {
        // Arrange
        CreateTasksFile();

        // Act - create two backups with slight delay
        BackupManager.CreateBackup();
        Thread.Sleep(1100); // 1+ second delay for different timestamp
        UpdateTasksFile("updated");
        BackupManager.CreateBackup();

        // Assert - should have 2 version backups + 1 daily backup
        var backupDir = StoragePaths.Current.BackupDirectory;
        var versionBackups = Directory.GetFiles(backupDir)
            .Where(f => !f.Contains("daily.") && !f.Contains(".trash."))
            .Count();
        var dailyBackups = Directory.GetFiles(backupDir)
            .Where(f => f.Contains("daily.") && !f.Contains(".trash."))
            .Count();

        Assert.Equal(2, versionBackups);
        Assert.Equal(1, dailyBackups);
    }

    [Fact]
    public void CreateBackup_BacksUpTrashFile()
    {
        // Arrange
        CreateTasksFile();
        CreateTrashFile();

        // Act
        BackupManager.CreateBackup();

        // Assert - both files should be backed up
        var backupDir = StoragePaths.Current.BackupDirectory;
        var tasksBackups = Directory.GetFiles(backupDir, "all-tasks.*backup.json")
            .Where(f => !f.Contains(".trash."))
            .Count();
        var trashBackups = Directory.GetFiles(backupDir, "all-tasks.trash.*backup.json")
            .Count();

        Assert.Equal(2, tasksBackups); // version + daily
        Assert.Equal(2, trashBackups); // version + daily
    }

    [Fact]
    public void ListBackups_ReturnsNewestFirst()
    {
        // Arrange - create multiple backups with delays
        CreateTasksFile();

        BackupManager.CreateBackup();
        Thread.Sleep(1100); // 1+ second delay for different timestamps
        UpdateTasksFile("updated content");
        BackupManager.CreateBackup();

        // Act
        var backups = BackupManager.ListBackups();

        // Assert - should have 2 version + 1 daily = 3 backups
        Assert.Equal(3, backups.Count);
        // Verify sorted newest first
        for (var i = 0; i < backups.Count - 1; i++)
        {
            Assert.True(backups[i].Timestamp >= backups[i + 1].Timestamp,
                "Backups should be sorted newest first");
        }
    }

    [Fact]
    public void RotateBackups_DeletesOldestWhenOverLimit()
    {
        // Arrange - manually create backup files to test rotation
        CreateTasksFile();
        StoragePaths.Current.EnsureBackupDirectory();

        // Create MaxVersionBackups + 3 backup files manually
        var baseTime = DateTime.Now.AddMinutes(-30);
        for (var i = 0; i < BackupConfig.MaxVersionBackups + 3; i++)
        {
            var timestamp = baseTime.AddSeconds(i);
            var fileName = $"all-tasks.{timestamp:yyyy-MM-ddTHH-mm-ss}.backup.json";
            var path = Path.Combine(StoragePaths.Current.BackupDirectory, fileName);
            File.WriteAllText(path, $"backup {i}");
        }

        // Act - create one more backup (triggers rotation)
        BackupManager.CreateBackup();

        // Assert - should have MaxVersionBackups + 1 (the new one)
        var versionBackups = BackupManager.ListBackups()
            .Where(b => !b.IsDaily)
            .ToList();
        Assert.True(versionBackups.Count <= BackupConfig.MaxVersionBackups + 1,
            $"Expected at most {BackupConfig.MaxVersionBackups + 1} backups, got {versionBackups.Count}");
    }

    [Fact]
    public void RestoreBackup_RestoresTasksFile()
    {
        // Arrange
        CreateTasksFile("original content");
        BackupManager.CreateBackup();

        var backups = BackupManager.ListBackups();
        var backupTimestamp = backups[0].Timestamp;

        // Modify the tasks file
        UpdateTasksFile("modified content");

        // Act
        BackupManager.RestoreBackup(backupTimestamp);

        // Assert
        var content = File.ReadAllText(StoragePaths.Current.AllTasksPath);
        Assert.Equal("original content", content);
    }

    [Fact]
    public void RestoreBackup_CreatesPreRestoreBackup()
    {
        // Arrange
        CreateTasksFile("original");
        BackupManager.CreateBackup();
        var backupTimestamp = BackupManager.ListBackups()[0].Timestamp;

        UpdateTasksFile("modified");

        // Act
        BackupManager.RestoreBackup(backupTimestamp);

        // Assert - should have a pre-restore backup
        var backupDir = StoragePaths.Current.BackupDirectory;
        var preRestoreBackups = Directory.GetFiles(backupDir, "*pre-restore*");
        Assert.NotEmpty(preRestoreBackups);

        // Pre-restore backup should contain "modified"
        var preRestoreContent = File.ReadAllText(preRestoreBackups[0]);
        Assert.Equal("modified", preRestoreContent);
    }

    [Fact]
    public void RestoreBackup_WithInvalidTimestamp_ThrowsBackupNotFoundException()
    {
        // Arrange
        CreateTasksFile();
        BackupManager.CreateBackup();

        var invalidTimestamp = DateTime.Now.AddDays(-100);

        // Act & Assert
        Assert.Throws<BackupNotFoundException>(() =>
            BackupManager.RestoreBackup(invalidTimestamp));
    }

    [Fact]
    public void RestoreBackup_AlsoRestoresTrash()
    {
        // Arrange
        CreateTasksFile("tasks");
        CreateTrashFile("original trash");
        BackupManager.CreateBackup();

        // Get the version backup (not daily)
        var versionBackups = BackupManager.ListBackups().Where(b => !b.IsDaily).ToList();
        Assert.NotEmpty(versionBackups);
        var backupTimestamp = versionBackups[0].Timestamp;

        UpdateTrashFile("modified trash");

        // Act
        BackupManager.RestoreBackup(backupTimestamp);

        // Assert
        Assert.True(File.Exists(StoragePaths.Current.AllTrashPath), "Trash file should exist after restore");
        var trashContent = File.ReadAllText(StoragePaths.Current.AllTrashPath);
        Assert.Equal("original trash", trashContent);
    }

    [Fact]
    public void AtomicWrite_Integration_SavesViaTempFile()
    {
        // This test verifies the atomic write pattern works via TodoTaskList
        // Arrange
        var taskList = new TodoTaskList();

        // Act - add a task (triggers Save with atomic write)
        taskList.AddTodoTask(
            TaskerCore.Models.TodoTask.CreateTodoTask("test task", "tasks"));

        // Assert - file should exist and be valid
        Assert.True(File.Exists(StoragePaths.Current.AllTasksPath));
        var content = File.ReadAllText(StoragePaths.Current.AllTasksPath);
        Assert.Contains("test task", content);

        // Temp file should not exist
        Assert.False(File.Exists(StoragePaths.Current.AllTasksPath + ".tmp"));
    }

    private void CreateTasksFile(string content = "[]")
    {
        StoragePaths.Current.EnsureDirectory();
        File.WriteAllText(StoragePaths.Current.AllTasksPath, content);
    }

    private void CreateTrashFile(string content = "[]")
    {
        StoragePaths.Current.EnsureDirectory();
        File.WriteAllText(StoragePaths.Current.AllTrashPath, content);
    }

    private void UpdateTasksFile(string content)
    {
        File.WriteAllText(StoragePaths.Current.AllTasksPath, content);
    }

    private void UpdateTrashFile(string content)
    {
        File.WriteAllText(StoragePaths.Current.AllTrashPath, content);
    }
}
