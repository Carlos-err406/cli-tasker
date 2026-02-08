namespace TaskerCore.Tests.Tui;

using cli_tasker.Tui;
using TaskerCore.Models;

public class HelpPanelTests
{
    private readonly TuiKeyHandler _handler;

    public HelpPanelTests()
    {
        _handler = new TuiKeyHandler(new TuiApp());
    }

    private static ConsoleKeyInfo MakeKey(ConsoleKey key, char keyChar = '\0', ConsoleModifiers modifiers = 0) =>
        new(keyChar, key, (modifiers & ConsoleModifiers.Shift) != 0,
            (modifiers & ConsoleModifiers.Alt) != 0,
            (modifiers & ConsoleModifiers.Control) != 0);

    [Fact]
    public void QuestionMark_InNormalMode_SetsShowHelp()
    {
        var state = new TuiState();
        var key = MakeKey(ConsoleKey.Oem2, '?', ConsoleModifiers.Shift);

        var result = _handler.Handle(key, state, Array.Empty<TodoTask>());

        Assert.True(result.ShowHelp);
    }

    [Fact]
    public void QuestionMark_WhenHelpShown_ClearsShowHelp()
    {
        var state = new TuiState { ShowHelp = true };
        var key = MakeKey(ConsoleKey.Oem2, '?', ConsoleModifiers.Shift);

        var result = _handler.Handle(key, state, Array.Empty<TodoTask>());

        Assert.False(result.ShowHelp);
    }

    [Fact]
    public void Escape_WhenHelpShown_ClearsShowHelp()
    {
        var state = new TuiState { ShowHelp = true };
        var key = MakeKey(ConsoleKey.Escape);

        var result = _handler.Handle(key, state, Array.Empty<TodoTask>());

        Assert.False(result.ShowHelp);
    }

    [Fact]
    public void SlashKey_InNormalMode_EntersSearchMode()
    {
        var state = new TuiState();
        var key = MakeKey(ConsoleKey.Oem2, '/');

        var result = _handler.Handle(key, state, Array.Empty<TodoTask>());

        Assert.Equal(TuiMode.Search, result.Mode);
        Assert.False(result.ShowHelp);
    }

    [Fact]
    public void OtherKeys_WhenHelpShown_AreIgnored()
    {
        var state = new TuiState { ShowHelp = true, CursorIndex = 3 };
        var key = MakeKey(ConsoleKey.DownArrow);

        var result = _handler.Handle(key, state, Array.Empty<TodoTask>());

        // State should be unchanged (help still shown, cursor didn't move)
        Assert.True(result.ShowHelp);
        Assert.Equal(3, result.CursorIndex);
    }

    [Fact]
    public void ShowHelp_DefaultsToFalse()
    {
        var state = new TuiState();
        Assert.False(state.ShowHelp);
    }
}
