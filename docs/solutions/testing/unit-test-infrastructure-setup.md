---
title: Unit Test Infrastructure Setup
category: testing
tags: [xunit, test-isolation, dotnet, storage]
module: TaskerCore
date: 2026-02-05
symptoms:
  - No way to verify implementations without manual CLI testing
  - Tests would interfere with real user data
---

# Unit Test Infrastructure Setup

## Problem

When implementing new features or fixing bugs, there was no way to verify the implementation worked correctly other than manually running the CLI and checking results. This slowed development and made it harder to catch regressions.

Additionally, any storage-based tests would risk affecting real user task data in `~/Library/Application Support/cli-tasker/`.

## Solution

### 1. Create xUnit Test Project

```bash
dotnet new xunit -n TaskerCore.Tests -o tests/TaskerCore.Tests
dotnet add tests/TaskerCore.Tests reference src/TaskerCore/TaskerCore.csproj
dotnet sln add tests/TaskerCore.Tests/TaskerCore.Tests.csproj
```

### 2. Make Storage Path Configurable for Test Isolation

Modified `src/TaskerCore/StoragePaths.cs` to support a test override:

```csharp
public static class StoragePaths
{
    private static string? _overrideDirectory;

    public static string Directory => _overrideDirectory ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "cli-tasker");

    /// <summary>
    /// Sets a custom directory for testing. Pass null to reset to default.
    /// </summary>
    internal static void SetDirectory(string? path) => _overrideDirectory = path;

    // ... rest of paths derived from Directory
}
```

### 3. Expose Internals to Test Assembly

Added to `src/TaskerCore/TaskerCore.csproj`:

```xml
<ItemGroup>
  <InternalsVisibleTo Include="TaskerCore.Tests" />
</ItemGroup>
```

### 4. Create Test Fixture for Storage Isolation

`tests/TaskerCore.Tests/TestFixture.cs`:

```csharp
public class StorageFixture : IDisposable
{
    public string TestDirectory { get; }

    public StorageFixture()
    {
        TestDirectory = Path.Combine(Path.GetTempPath(), $"tasker-tests-{Guid.NewGuid()}");
        Directory.CreateDirectory(TestDirectory);
        StoragePaths.SetDirectory(TestDirectory);
    }

    public void Dispose()
    {
        StoragePaths.SetDirectory(null);
        if (Directory.Exists(TestDirectory))
            Directory.Delete(TestDirectory, recursive: true);
    }
}

[CollectionDefinition("Storage")]
public class StorageCollection : ICollectionFixture<StorageFixture> { }
```

### 5. Exclude Tests from Main Project Build

The main `cli-tasker.csproj` was picking up test files. Added exclusion:

```xml
<DefaultItemExcludes>$(DefaultItemExcludes);src/**;tests/**</DefaultItemExcludes>
```

## Running Tests

```bash
# Run all tests
dotnet test

# Run specific test class
dotnet test --filter "FullyQualifiedName~TaskDescriptionParserTests"

# Run with verbose output
dotnet test -v n
```

## Key Insight

Using an `internal` setter for `StoragePaths.Directory` with `InternalsVisibleTo` keeps the API clean for production code while allowing tests to override storage location. Tests run in complete isolation with unique temp directories that are cleaned up after each test run.

## Prevention

- When adding features that touch storage, write tests first
- Use the `[Collection("Storage")]` attribute on test classes that need isolated storage
- Parser and pure logic tests don't need storage isolation and can run faster

## Files Modified

| File | Change |
|------|--------|
| `tests/TaskerCore.Tests/TaskerCore.Tests.csproj` | New test project |
| `tests/TaskerCore.Tests/TestFixture.cs` | Storage isolation fixture |
| `tests/TaskerCore.Tests/Parsing/TaskDescriptionParserTests.cs` | Initial parser tests |
| `src/TaskerCore/StoragePaths.cs` | Added `SetDirectory()` method |
| `src/TaskerCore/TaskerCore.csproj` | Added `InternalsVisibleTo` |
| `cli-tasker.csproj` | Added `tests/**` exclusion |
| `CLAUDE.md` | Added test commands section |
