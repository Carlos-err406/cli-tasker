namespace TaskerCore.Tests.Data;

using TaskerCore.Data;
using TaskerCore.Models;
using TaskStatus = TaskerCore.Models.TaskStatus;

public class SearchTasksTests : IDisposable
{
    private readonly TaskerServices _services;

    public SearchTasksTests()
    {
        _services = TaskerServices.CreateInMemory();
        SeedTasks();
    }

    public void Dispose()
    {
        _services.Dispose();
        GC.SuppressFinalize(this);
    }

    private void SeedTasks()
    {
        var taskList = new TodoTaskList(_services);

        // Create lists
        TodoTaskList.CreateList(_services, "work");
        TodoTaskList.CreateList(_services, "personal");

        // Add tasks across lists
        taskList.AddTodoTask(TodoTask.CreateTodoTask("Buy groceries #shopping", "tasks"), recordUndo: false);
        taskList.AddTodoTask(TodoTask.CreateTodoTask("Review pull request", "work"), recordUndo: false);
        taskList.AddTodoTask(TodoTask.CreateTodoTask("Call dentist", "personal"), recordUndo: false);
        taskList.AddTodoTask(TodoTask.CreateTodoTask("Write unit tests", "work"), recordUndo: false);
        taskList.AddTodoTask(TodoTask.CreateTodoTask("Buy birthday present", "personal"), recordUndo: false);
    }

    [Fact]
    public void SearchTasks_ReturnsMatchingTasks()
    {
        var results = TodoTaskList.SearchTasks(_services, "Buy");
        Assert.Equal(2, results.Count);
        Assert.All(results, t => Assert.Contains("Buy", t.Description, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SearchTasks_IsCaseInsensitive()
    {
        var lower = TodoTaskList.SearchTasks(_services, "buy");
        var upper = TodoTaskList.SearchTasks(_services, "BUY");
        var mixed = TodoTaskList.SearchTasks(_services, "bUy");

        Assert.Equal(2, lower.Count);
        Assert.Equal(2, upper.Count);
        Assert.Equal(2, mixed.Count);
    }

    [Fact]
    public void SearchTasks_SearchesAcrossAllLists()
    {
        var results = TodoTaskList.SearchTasks(_services, "Buy");

        var lists = results.Select(t => t.ListName).Distinct().ToList();
        Assert.Contains("tasks", lists);
        Assert.Contains("personal", lists);
    }

    [Fact]
    public void SearchTasks_NoMatch_ReturnsEmpty()
    {
        var results = TodoTaskList.SearchTasks(_services, "nonexistent");
        Assert.Empty(results);
    }

    [Fact]
    public void SearchTasks_EscapesLikeWildcards()
    {
        // Add a task with % and _ in the description
        var taskList = new TodoTaskList(_services);
        taskList.AddTodoTask(TodoTask.CreateTodoTask("100% done_task", "tasks"), recordUndo: false);

        // Searching for "%" should only match the task with literal %
        var percentResults = TodoTaskList.SearchTasks(_services, "100%");
        Assert.Single(percentResults);
        Assert.Contains("100%", percentResults[0].Description);

        // Searching for "_" should only match the task with literal _
        var underscoreResults = TodoTaskList.SearchTasks(_services, "done_task");
        Assert.Single(underscoreResults);
        Assert.Contains("done_task", underscoreResults[0].Description);
    }

    [Fact]
    public void SearchTasks_PreservesSortOrder_ActiveThenDone()
    {
        var taskList = new TodoTaskList(_services);

        // Mark one "Buy" task as done
        var allTasks = taskList.GetSortedTasks();
        var buyGroceries = allTasks.First(t => t.Description.Contains("groceries"));
        taskList.SetStatus(buyGroceries.Id, TaskStatus.Done, recordUndo: false);

        var results = TodoTaskList.SearchTasks(_services, "Buy");
        Assert.Equal(2, results.Count);

        // Active task first, done task last
        Assert.NotEqual(TaskStatus.Done, results[0].Status);
        Assert.Equal(TaskStatus.Done, results[1].Status);
    }

    [Fact]
    public void SearchTasks_ExcludesTrashedTasks()
    {
        var taskList = new TodoTaskList(_services);
        var allTasks = taskList.GetSortedTasks();
        var groceries = allTasks.First(t => t.Description.Contains("groceries"));

        taskList.DeleteTask(groceries.Id, recordUndo: false);

        var results = TodoTaskList.SearchTasks(_services, "groceries");
        Assert.Empty(results);
    }

    [Fact]
    public void SearchTasks_MatchesTagsInDescription()
    {
        var results = TodoTaskList.SearchTasks(_services, "#shopping");
        Assert.Single(results);
        Assert.Contains("#shopping", results[0].Description);
    }
}
