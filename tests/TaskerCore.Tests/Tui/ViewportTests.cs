namespace TaskerCore.Tests.Tui;

using cli_tasker.Tui;

public class ViewportTests
{
    [Fact]
    public void SingleLineTasks_CursorInMiddle_ShowsSurroundingTasks()
    {
        var lineHeights = new int[] { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 }; // 10 tasks, 1 line each
        var availableLines = 5;
        var cursorIndex = 5;

        var (start, end) = TuiRenderer.ComputeViewport(cursorIndex, lineHeights, availableLines);

        // Cursor task must be in the visible range
        Assert.True(start <= cursorIndex);
        Assert.True(end > cursorIndex);
        // Should show exactly 5 tasks
        Assert.Equal(5, end - start);
    }

    [Fact]
    public void MultiLineTasks_CursorNearBottom_CursorIsVisible()
    {
        // Tasks: 5 single-line, then a 3-line task at index 5, then 4 more single-line
        var lineHeights = new int[] { 1, 1, 1, 1, 1, 3, 1, 1, 1, 1 };
        var availableLines = 6;
        var cursorIndex = 5; // the 3-line task

        var (start, end) = TuiRenderer.ComputeViewport(cursorIndex, lineHeights, availableLines);

        // Cursor task must be in the visible range
        Assert.True(start <= cursorIndex);
        Assert.True(end > cursorIndex);

        // Total visible lines should not exceed available
        var totalLines = 0;
        for (var i = start; i < end; i++)
            totalLines += lineHeights[i];
        Assert.True(totalLines <= availableLines);
    }

    [Fact]
    public void GroupHeaders_CursorOnFirstTaskOfSecondGroup_HeaderIncluded()
    {
        // Task 0: 1 line, Task 1: 1 line (both in group A with +1 header on task 0)
        // Task 2: 2 lines (+1 header = 3 total, first in group B), Task 3: 1 line
        var lineHeights = new int[] { 2, 1, 3, 1 }; // headers already included
        var availableLines = 5;
        var cursorIndex = 2; // first task of group B (3 lines including header)

        var (start, end) = TuiRenderer.ComputeViewport(cursorIndex, lineHeights, availableLines);

        // Cursor task must be in the visible range
        Assert.True(start <= cursorIndex);
        Assert.True(end > cursorIndex);

        var totalLines = 0;
        for (var i = start; i < end; i++)
            totalLines += lineHeights[i];
        Assert.True(totalLines <= availableLines);
    }

    [Fact]
    public void SingleTaskExceedsAvailableLines_NoException()
    {
        var lineHeights = new int[] { 20 }; // one task with 20 lines
        var availableLines = 10;
        var cursorIndex = 0;

        var (start, end) = TuiRenderer.ComputeViewport(cursorIndex, lineHeights, availableLines);

        // Should still include the task (even though it doesn't fully fit)
        Assert.Equal(0, start);
        Assert.Equal(1, end);
    }

    [Fact]
    public void EmptyTaskList_ReturnsZeroRange()
    {
        var lineHeights = Array.Empty<int>();
        var (start, end) = TuiRenderer.ComputeViewport(0, lineHeights, 10);

        Assert.Equal(0, start);
        Assert.Equal(0, end);
    }

    [Fact]
    public void AvailableLinesNegative_ClampsToOne()
    {
        var lineHeights = new int[] { 1, 1, 1 };
        var cursorIndex = 1;

        var (start, end) = TuiRenderer.ComputeViewport(cursorIndex, lineHeights, -5);

        // Should still show at least the cursor task
        Assert.True(start <= cursorIndex);
        Assert.True(end > cursorIndex);
    }
}
