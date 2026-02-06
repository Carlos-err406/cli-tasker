---
title: Test Isolation Prevention Strategies
category: testing
tags: [xunit, test-isolation, dependency-injection, static-state, best-practices]
module: TaskerCore
date: 2026-02-05
severity: critical
symptoms:
  - Tests accidentally write to production storage
  - Tests share state causing flaky/unpredictable failures
  - User data loss from test runs
---

# Test Isolation Prevention Strategies

## Background: The Incident

On 2026-02-05, tests accidentally wiped user's real task data due to singleton isolation issues. The root cause was static singletons that allowed tests to share state and write to production storage paths.

## Solution Summary

The fix introduced:
1. **`TaskerServices`** - A dependency injection container holding all services
2. **`StoragePaths`** - An instantiable class (not static) taking a base directory
3. **`TaskerServices.SetDefault()`** - Allows tests to override the global default
4. **`[Collection("IsolatedTests")]`** - xUnit collection for sequential test execution

---

## 1. Best Practices to Prevent This Issue

### 1.1 Favor Dependency Injection Over Static State

**DO:**
```csharp
// Accept services as a parameter
public class TodoTaskList
{
    private readonly TaskerServices _services;

    public TodoTaskList(TaskerServices services, string? listName = null)
    {
        _services = services;
        // Use _services.Paths, _services.Undo, etc.
    }
}
```

**DON'T:**
```csharp
// Static singletons with hidden state
public static class StoragePaths
{
    private static string _directory = GetDefault();
    public static string Directory => _directory;
}
```

### 1.2 Provide Overloads for Backward Compatibility

When refactoring to DI, keep convenience overloads that use the default:

```csharp
// Primary method with explicit services
public static string[] GetAllListNames(TaskerServices services) { ... }

// Convenience overload using default (for production CLI)
public static string[] GetAllListNames() => GetAllListNames(TaskerServices.Default);
```

### 1.3 Every Test Class Must Own Its Storage

**Pattern 1: Inherit from `TestBase`**
```csharp
public class MyFeatureTests : TestBase
{
    [Fact]
    public void MyTest()
    {
        // Services is already set up with isolated temp directory
        var taskList = new TodoTaskList(Services);
    }
}
```

**Pattern 2: Self-contained with `IDisposable`**
```csharp
[Collection("IsolatedTests")]
public class MyFeatureTests : IDisposable
{
    private readonly string _testDir;
    private readonly TaskerServices _services;

    public MyFeatureTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"tasker-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDir);
        _services = new TaskerServices(_testDir);
        TaskerServices.SetDefault(_services);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, recursive: true);
        GC.SuppressFinalize(this);
    }
}
```

### 1.4 Use Collection Attributes for Sequential Execution

When tests modify `TaskerServices.Default`, they MUST run sequentially to avoid race conditions:

```csharp
[Collection("IsolatedTests")]  // Groups tests for sequential execution
public class UndoTests : IDisposable
{
    // ...
}
```

### 1.5 Never Hardcode Production Paths in Tests

**DO:**
```csharp
var path = _services.Paths.AllTasksPath;  // Uses injected test directory
```

**DON'T:**
```csharp
var path = "~/Library/Application Support/cli-tasker/all-tasks.json";
```

---

## 2. Code Review Checklist

### For New Classes

- [ ] Does the class accept `TaskerServices` as a constructor parameter?
- [ ] If using static methods, do they have overloads accepting `TaskerServices`?
- [ ] Are there any `new StoragePaths()` calls without parameters? (Should use DI)
- [ ] Does the class avoid storing state in static fields?

### For New Tests

- [ ] Does the test class inherit from `TestBase` OR implement `IDisposable`?
- [ ] Is `TaskerServices.SetDefault()` called in the constructor?
- [ ] Is the temp directory cleaned up in `Dispose()`?
- [ ] Is the `[Collection("IsolatedTests")]` attribute applied if modifying static state?
- [ ] Does the test avoid any hardcoded paths to user directories?

### For Refactoring Existing Code

- [ ] Are all static `StoragePaths` usages replaced with instance access?
- [ ] Is backward compatibility maintained with convenience overloads?
- [ ] Are all callers updated to pass `TaskerServices` where appropriate?
- [ ] Are existing tests updated to use the new DI pattern?

---

## 3. Test Patterns to Follow

### 3.1 The Standard Test Class Template

```csharp
namespace TaskerCore.Tests.YourFeature;

using TaskerCore;
using TaskerCore.Data;

[Collection("IsolatedTests")]
public class YourFeatureTests : IDisposable
{
    private readonly string _testDir;
    private readonly TaskerServices _services;

    public YourFeatureTests()
    {
        // Always use a unique temp directory
        _testDir = Path.Combine(Path.GetTempPath(), $"tasker-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDir);

        // Create isolated services
        _services = new TaskerServices(_testDir);
        TaskerServices.SetDefault(_services);

        // Clear any shared state (like undo history)
        _services.Undo.ClearHistory();
    }

    public void Dispose()
    {
        // Clean up undo state
        _services.Undo.ClearHistory();

        // Remove temp directory
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, recursive: true);
        }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void YourTest()
    {
        // Use _services for all operations
        var taskList = new TodoTaskList(_services);
        // ...
    }
}
```

### 3.2 Testing with Pre-existing Data

```csharp
[Fact]
public void LoadsExistingTasks()
{
    // Arrange - create test data file directly
    var json = """[{"ListName":"tasks","Tasks":[]}]""";
    File.WriteAllText(_services.Paths.AllTasksPath, json);

    // Act
    var taskList = new TodoTaskList(_services);
    var lists = TodoTaskList.GetAllListNames(_services);

    // Assert
    Assert.Contains("tasks", lists);
}
```

### 3.3 Testing Undo Operations

```csharp
[Fact]
public void Undo_RevertsChange()
{
    // Arrange
    _services.Undo.ClearHistory();  // Start clean
    var taskList = new TodoTaskList(_services);
    var task = TodoTask.CreateTodoTask("test", "tasks");
    taskList.AddTodoTask(task);  // recordUndo: true by default

    // Act
    _services.Undo.Undo();

    // Assert
    taskList = new TodoTaskList(_services);  // Reload
    Assert.Empty(taskList.GetAllTasks());
}
```

### 3.4 Pure Logic Tests (No Storage Needed)

For tests that don't touch storage, skip the fixture overhead:

```csharp
public class TaskDescriptionParserTests  // No [Collection] needed
{
    [Fact]
    public void ParsesDate()
    {
        var result = TaskDescriptionParser.Parse("Task due:2026-02-05");
        Assert.Equal(new DateOnly(2026, 2, 5), result.DueDate);
    }
}
```

---

## 4. Warning Signs to Watch For

### In Code

| Warning Sign | Risk | Fix |
|--------------|------|-----|
| `static string` fields for paths | Shared state | Use `StoragePaths` instance |
| `Environment.GetFolderPath()` in tests | Production paths | Use injected `_services.Paths` |
| `new TodoTaskList()` without services | Uses `TaskerServices.Default` | Pass explicit services |
| Missing `[Collection]` on storage tests | Race conditions | Add `[Collection("IsolatedTests")]` |
| `File.Exists(hardcoded_path)` | Production access | Use `_services.Paths.AllTasksPath` |

### In Test Behavior

| Symptom | Likely Cause | Solution |
|---------|--------------|----------|
| Tests pass alone, fail together | Shared state leakage | Add `[Collection]` attribute |
| Tests create real task files | Using production paths | Ensure `SetDefault()` is called |
| Flaky undo/redo tests | Undo history not cleared | Call `ClearHistory()` in setup |
| Temp directories not cleaned | Missing `Dispose()` | Implement `IDisposable` |
| Tests modify user data | Wrong `TaskerServices` | Verify DI setup in constructor |

### In CI/CD

| Symptom | Likely Cause | Solution |
|---------|--------------|----------|
| Different results locally vs CI | Different user home paths | Use temp directory, not hardcoded |
| Leftover test artifacts | Cleanup not running | Verify `Dispose()` is called |
| Permission errors | Writing to protected dirs | Use `Path.GetTempPath()` |

---

## 5. Automated Checks

### 5.1 Roslyn Analyzer Rules (Potential)

Create custom analyzers to detect:
- Direct usage of `Environment.SpecialFolder.ApplicationData` in test projects
- `new StoragePaths()` without parameters in test code
- Test classes without `[Collection]` attribute that use storage
- Static field assignments in `StoragePaths` or similar classes

### 5.2 CI Pipeline Checks

Add to your CI workflow:

```yaml
# .github/workflows/ci.yml
jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Run tests
        run: dotnet test --logger "console;verbosity=detailed"

      # Verify no production files were touched
      - name: Check for production file pollution
        run: |
          if [ -d "$HOME/Library/Application Support/cli-tasker" ]; then
            echo "ERROR: Tests created files in production directory!"
            ls -la "$HOME/Library/Application Support/cli-tasker"
            exit 1
          fi
```

### 5.3 Pre-commit Hook

Add a git hook to catch obvious issues:

```bash
#!/bin/bash
# .git/hooks/pre-commit

# Check for hardcoded production paths in test files
if grep -r "Library/Application Support/cli-tasker" tests/; then
    echo "ERROR: Hardcoded production path found in tests!"
    exit 1
fi

# Check for tests without Collection attribute that use storage
# (More sophisticated checks would need a proper analyzer)
```

### 5.4 Grep-Based Checks in CI

```bash
# Ensure all test files that use TaskerServices have the Collection attribute
for file in tests/**/*Tests.cs; do
    if grep -q "TaskerServices" "$file"; then
        if ! grep -q '\[Collection("IsolatedTests")\]' "$file" && \
           ! grep -q ': TestBase' "$file"; then
            echo "WARNING: $file uses TaskerServices but may lack proper isolation"
        fi
    fi
done
```

### 5.5 Runtime Safety Guard

Add a defensive check in `StoragePaths` for extra safety:

```csharp
public StoragePaths(string? baseDirectory = null)
{
    Directory = baseDirectory ?? GetDefaultDirectory();

    // Safety: Detect test environment trying to use production paths
    #if DEBUG
    var isTestAssembly = AppDomain.CurrentDomain.GetAssemblies()
        .Any(a => a.GetName().Name?.Contains("Test") == true);

    if (isTestAssembly && baseDirectory == null)
    {
        throw new InvalidOperationException(
            "Test code attempting to use production storage! " +
            "Pass an explicit baseDirectory or use TaskerServices.SetDefault().");
    }
    #endif
}
```

---

## Summary

| Category | Key Point |
|----------|-----------|
| Architecture | Use `TaskerServices` container with DI, not static singletons |
| Test Setup | Every test class gets unique temp directory via constructor |
| Test Cleanup | Implement `IDisposable` to remove temp directory |
| Concurrency | Use `[Collection("IsolatedTests")]` for tests touching static state |
| Validation | CI checks for production path pollution |
| Defense | Runtime guards in debug builds |

By following these patterns, you ensure that tests remain completely isolated from production data and from each other, preventing the catastrophic data loss that occurred when static singletons leaked state between test runs.
