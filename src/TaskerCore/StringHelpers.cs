namespace TaskerCore;

/// <summary>
/// Common string manipulation helpers.
/// </summary>
public static class StringHelpers
{
    /// <summary>
    /// Truncates a string to the specified maximum length, adding "..." if truncated.
    /// Only uses the first line if the text contains newlines.
    /// </summary>
    public static string Truncate(string text, int maxLength)
    {
        var firstLine = text.Split('\n')[0];
        return firstLine.Length <= maxLength ? firstLine : firstLine[..maxLength] + "...";
    }
}
