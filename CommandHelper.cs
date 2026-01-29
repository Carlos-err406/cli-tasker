namespace cli_tasker;

using System.CommandLine;

static class CommandHelper
{
    public static Action<ParseResult> WithErrorHandling(Action<ParseResult> action)
    {
        return parseResult =>
        {
            try
            {
                action(parseResult);
            }
            catch (TaskerException ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        };
    }
}
