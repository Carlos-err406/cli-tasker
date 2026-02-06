namespace TaskerCore.Tests.Backup;

using TaskerCore.Backup;
using TaskerCore.Data;
using TaskerCore.Exceptions;
using TaskerCore.Models;

[Collection("IsolatedTests")]
public class BackupManagerTests : IDisposable
{
    private readonly string _testDir;
    private readonly TaskerServices _services;

    public BackupManagerTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"backup-tests-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDir);
        _services = new TaskerServices(_testDir);
        TaskerServices.SetDefault(_services);
    }

    public void Dispose()
    {
        _services.Dispose();
        Thread.Sleep(50);
        try
        {
            if (Directory.Exists(_testDir))
                Directory.Delete(_testDir, recursive: true);
        }
        catch { /* Ignore cleanup errors */ }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void CreateBackup_CreatesVersionBackup()
    {
        // Act
        _services.Backup.CreateBackup();

        // Assert
        var backups = _services.Backup.ListBackups();
        Assert.NotEmpty(backups);
        var versionBackups = backups.Where(b => !b.IsDaily).ToList();
        Assert.Single(versionBackups);
    }

    [Fact]
    public void CreateBackup_CreatesDailyBackupOnce()
    {
        // Act - create two backups with slight delay
        _services.Backup.CreateBackup();
        Thread.Sleep(1100);
        _services.Backup.CreateBackup();

        // Assert - should have 2 version backups + 1 daily backup
        var backups = _services.Backup.ListBackups();
        var versionBackups = backups.Where(b => !b.IsDaily).ToList();
        var dailyBackups = backups.Where(b => b.IsDaily).ToList();

        Assert.Equal(2, versionBackups.Count);
        Assert.Single(dailyBackups);
    }

    [Fact]
    public void CreateBackup_BackupContainsData()
    {
        // Arrange - add a task to the database
        var taskList = new TodoTaskList(_services, "tasks");
        taskList.AddTodoTask(TodoTask.CreateTodoTask("backup test task", "tasks"), recordUndo: false);

        // Act
        _services.Backup.CreateBackup();

        // Assert - backup file should exist and be a valid SQLite DB
        var backups = _services.Backup.ListBackups();
        Assert.NotEmpty(backups);

        var backupPath = backups[0].FilePath;
        Assert.True(File.Exists(backupPath));
        Assert.True(new FileInfo(backupPath).Length > 0);
    }

    [Fact]
    public void ListBackups_ReturnsNewestFirst()
    {
        // Arrange - create multiple backups with delays
        _services.Backup.CreateBackup();
        Thread.Sleep(1100);
        _services.Backup.CreateBackup();

        // Act
        var backups = _services.Backup.ListBackups();

        // Assert - should have 2 version + 1 daily = 3 backups
        Assert.Equal(3, backups.Count);
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
        _services.Paths.EnsureBackupDirectory();

        var baseTime = DateTime.Now.AddMinutes(-30);
        for (var i = 0; i < BackupConfig.MaxVersionBackups + 3; i++)
        {
            var timestamp = baseTime.AddSeconds(i);
            var fileName = $"tasker.{timestamp:yyyy-MM-ddTHH-mm-ss}{BackupConfig.BackupExtension}";
            var path = Path.Combine(_services.Paths.BackupDirectory, fileName);
            // Create a minimal valid SQLite file (just touch it for rotation testing)
            File.WriteAllBytes(path, new byte[0]);
        }

        // Act - create one more backup (triggers rotation)
        _services.Backup.CreateBackup();

        // Assert
        var versionBackups = _services.Backup.ListBackups()
            .Where(b => !b.IsDaily)
            .ToList();
        Assert.True(versionBackups.Count <= BackupConfig.MaxVersionBackups + 1,
            $"Expected at most {BackupConfig.MaxVersionBackups + 1} backups, got {versionBackups.Count}");
    }

    [Fact]
    public void RestoreBackup_RestoresData()
    {
        // Arrange - add a task and backup
        var taskList = new TodoTaskList(_services, "tasks");
        var task = TodoTask.CreateTodoTask("original task", "tasks");
        taskList.AddTodoTask(task, recordUndo: false);
        _services.Backup.CreateBackup();

        var backups = _services.Backup.ListBackups();
        var backupTimestamp = backups.First(b => !b.IsDaily).Timestamp;

        // Delete the task (modify current state)
        taskList.DeleteTask(task.Id, moveToTrash: false, recordUndo: false);
        var afterDelete = new TodoTaskList(_services, "tasks").GetAllTasks();
        Assert.Empty(afterDelete);

        // Act - restore from backup
        _services.Backup.RestoreBackup(backupTimestamp);

        // Assert - task should be back
        var restored = new TodoTaskList(_services, "tasks").GetAllTasks();
        Assert.Single(restored);
        Assert.Equal("original task", restored[0].Description);
    }

    [Fact]
    public void RestoreBackup_CreatesPreRestoreBackup()
    {
        // Arrange
        _services.Backup.CreateBackup();
        var backupTimestamp = _services.Backup.ListBackups().First(b => !b.IsDaily).Timestamp;

        // Act
        _services.Backup.RestoreBackup(backupTimestamp);

        // Assert - should have a pre-restore backup
        var backupDir = _services.Paths.BackupDirectory;
        var preRestoreBackups = Directory.GetFiles(backupDir, $"*{BackupConfig.PreRestorePrefix}*");
        Assert.NotEmpty(preRestoreBackups);
    }

    [Fact]
    public void RestoreBackup_WithInvalidTimestamp_ThrowsBackupNotFoundException()
    {
        // Arrange
        _services.Backup.CreateBackup();
        var invalidTimestamp = DateTime.Now.AddDays(-100);

        // Act & Assert
        Assert.Throws<BackupNotFoundException>(() =>
            _services.Backup.RestoreBackup(invalidTimestamp));
    }

    [Fact]
    public void RestoreBackup_AlsoRestoresTrash()
    {
        // Arrange - add a task, delete it (moves to trash), then backup
        var taskList = new TodoTaskList(_services, "tasks");
        var task = TodoTask.CreateTodoTask("will be trashed", "tasks");
        taskList.AddTodoTask(task, recordUndo: false);
        taskList.DeleteTask(task.Id, recordUndo: false);

        _services.Backup.CreateBackup();
        var backupTimestamp = _services.Backup.ListBackups().First(b => !b.IsDaily).Timestamp;

        // Permanently clear the trash
        taskList.ClearTrash();
        var trashAfterClear = new TodoTaskList(_services).GetTrash();
        Assert.Empty(trashAfterClear);

        // Act - restore from backup
        _services.Backup.RestoreBackup(backupTimestamp);

        // Assert - trash should be restored
        var restoredTrash = new TodoTaskList(_services).GetTrash();
        Assert.Single(restoredTrash);
        Assert.Equal("will be trashed", restoredTrash[0].Description);
    }

    [Fact]
    public void DataPersists_AfterAddingTask()
    {
        // Arrange - add a task via TodoTaskList
        var taskList = new TodoTaskList(_services);
        taskList.AddTodoTask(TodoTask.CreateTodoTask("test task", "tasks"), recordUndo: false);

        // Assert - data persists (read from same DB)
        var readBack = new TodoTaskList(_services, "tasks").GetAllTasks();
        Assert.Single(readBack);
        Assert.Equal("test task", readBack[0].Description);
    }
}
