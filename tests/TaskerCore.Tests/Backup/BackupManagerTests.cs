namespace TaskerCore.Tests.Backup;

using TaskerCore.Backup;
using TaskerCore.Data;
using TaskerCore.Exceptions;

[Collection("IsolatedTests")]
public class BackupManagerTests : IDisposable
{
    private readonly string _testDir;
    private readonly TaskerServices _services;

    public BackupManagerTests()
    {
        // Create a fresh test directory for each test
        _testDir = Path.Combine(Path.GetTempPath(), $"backup-tests-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDir);
        _services = new TaskerServices(_testDir);
        TaskerServices.SetDefault(_services);
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
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void CreateBackup_WhenNoTasksFile_DoesNotCreateBackup()
    {
        // Arrange - no tasks file exists

        // Act
        _services.Backup.CreateBackup();

        // Assert
        var backups = _services.Backup.ListBackups();
        Assert.Empty(backups);
    }

    [Fact]
    public void CreateBackup_WhenTasksFileExists_CreatesVersionBackup()
    {
        // Arrange - create a tasks file
        CreateTasksFile();

        // Act
        _services.Backup.CreateBackup();

        // Assert - should have at least one version backup (also creates daily)
        var backups = _services.Backup.ListBackups();
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
        _services.Backup.CreateBackup();
        Thread.Sleep(1100); // 1+ second delay for different timestamp
        UpdateTasksFile("updated");
        _services.Backup.CreateBackup();

        // Assert - should have 2 version backups + 1 daily backup
        var backupDir = _services.Paths.BackupDirectory;
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
        _services.Backup.CreateBackup();

        // Assert - both files should be backed up
        var backupDir = _services.Paths.BackupDirectory;
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

        _services.Backup.CreateBackup();
        Thread.Sleep(1100); // 1+ second delay for different timestamps
        UpdateTasksFile("updated content");
        _services.Backup.CreateBackup();

        // Act
        var backups = _services.Backup.ListBackups();

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
        _services.Paths.EnsureBackupDirectory();

        // Create MaxVersionBackups + 3 backup files manually
        var baseTime = DateTime.Now.AddMinutes(-30);
        for (var i = 0; i < BackupConfig.MaxVersionBackups + 3; i++)
        {
            var timestamp = baseTime.AddSeconds(i);
            var fileName = $"all-tasks.{timestamp:yyyy-MM-ddTHH-mm-ss}.backup.json";
            var path = Path.Combine(_services.Paths.BackupDirectory, fileName);
            File.WriteAllText(path, $"backup {i}");
        }

        // Act - create one more backup (triggers rotation)
        _services.Backup.CreateBackup();

        // Assert - should have MaxVersionBackups + 1 (the new one)
        var versionBackups = _services.Backup.ListBackups()
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
        _services.Backup.CreateBackup();

        var backups = _services.Backup.ListBackups();
        var backupTimestamp = backups[0].Timestamp;

        // Modify the tasks file
        UpdateTasksFile("modified content");

        // Act
        _services.Backup.RestoreBackup(backupTimestamp);

        // Assert
        var content = File.ReadAllText(_services.Paths.AllTasksPath);
        Assert.Equal("original content", content);
    }

    [Fact]
    public void RestoreBackup_CreatesPreRestoreBackup()
    {
        // Arrange
        CreateTasksFile("original");
        _services.Backup.CreateBackup();
        var backupTimestamp = _services.Backup.ListBackups()[0].Timestamp;

        UpdateTasksFile("modified");

        // Act
        _services.Backup.RestoreBackup(backupTimestamp);

        // Assert - should have a pre-restore backup
        var backupDir = _services.Paths.BackupDirectory;
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
        _services.Backup.CreateBackup();

        var invalidTimestamp = DateTime.Now.AddDays(-100);

        // Act & Assert
        Assert.Throws<BackupNotFoundException>(() =>
            _services.Backup.RestoreBackup(invalidTimestamp));
    }

    [Fact]
    public void RestoreBackup_AlsoRestoresTrash()
    {
        // Arrange
        CreateTasksFile("tasks");
        CreateTrashFile("original trash");
        _services.Backup.CreateBackup();

        // Get the version backup (not daily)
        var versionBackups = _services.Backup.ListBackups().Where(b => !b.IsDaily).ToList();
        Assert.NotEmpty(versionBackups);
        var backupTimestamp = versionBackups[0].Timestamp;

        UpdateTrashFile("modified trash");

        // Act
        _services.Backup.RestoreBackup(backupTimestamp);

        // Assert
        Assert.True(File.Exists(_services.Paths.AllTrashPath), "Trash file should exist after restore");
        var trashContent = File.ReadAllText(_services.Paths.AllTrashPath);
        Assert.Equal("original trash", trashContent);
    }

    [Fact]
    public void AtomicWrite_Integration_SavesViaTempFile()
    {
        // This test verifies the atomic write pattern works via TodoTaskList
        // Arrange
        var taskList = new TodoTaskList(_services);

        // Act - add a task (triggers Save with atomic write)
        taskList.AddTodoTask(
            TaskerCore.Models.TodoTask.CreateTodoTask("test task", "tasks"));

        // Assert - file should exist and be valid
        Assert.True(File.Exists(_services.Paths.AllTasksPath));
        var content = File.ReadAllText(_services.Paths.AllTasksPath);
        Assert.Contains("test task", content);

        // Temp file should not exist
        Assert.False(File.Exists(_services.Paths.AllTasksPath + ".tmp"));
    }

    private void CreateTasksFile(string content = "[]")
    {
        _services.Paths.EnsureDirectory();
        File.WriteAllText(_services.Paths.AllTasksPath, content);
    }

    private void CreateTrashFile(string content = "[]")
    {
        _services.Paths.EnsureDirectory();
        File.WriteAllText(_services.Paths.AllTrashPath, content);
    }

    private void UpdateTasksFile(string content)
    {
        File.WriteAllText(_services.Paths.AllTasksPath, content);
    }

    private void UpdateTrashFile(string content)
    {
        File.WriteAllText(_services.Paths.AllTrashPath, content);
    }
}
