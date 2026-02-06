namespace TaskerCore.Utilities;

public static class TagColors
{
    // Curated palette - dark theme friendly, visually distinct
    private static readonly string[] Palette =
    [
        "#3B82F6", // Blue
        "#10B981", // Emerald
        "#F59E0B", // Amber
        "#EF4444", // Red
        "#8B5CF6", // Violet
        "#EC4899", // Pink
        "#06B6D4", // Cyan
        "#84CC16", // Lime
        "#F97316", // Orange
        "#6366F1", // Indigo
    ];

    public static string GetHexColor(string tag)
    {
        var index = Math.Abs(GetDeterministicHash(tag)) % Palette.Length;
        return Palette[index];
    }

    public static string GetSpectreMarkup(string tag)
    {
        var hex = GetHexColor(tag);
        return $"[{hex}]";
    }

    /// <summary>
    /// Returns a deterministic hash that's consistent across process restarts.
    /// string.GetHashCode() is randomized per-process in .NET Core for security.
    /// </summary>
    private static int GetDeterministicHash(string str)
    {
        unchecked
        {
            int hash = 5381;
            foreach (char c in str)
            {
                hash = ((hash << 5) + hash) ^ c; // DJB2 hash algorithm
            }
            return hash;
        }
    }
}
