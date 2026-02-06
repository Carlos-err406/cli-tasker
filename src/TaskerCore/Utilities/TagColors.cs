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

    public static string GetForegroundHex(string tag)
    {
        var hex = GetHexColor(tag);
        return IsLightColor(hex) ? "#000000" : "#FFFFFF";
    }

    private static bool IsLightColor(string hex)
    {
        var r = Convert.ToInt32(hex.Substring(1, 2), 16) / 255.0;
        var g = Convert.ToInt32(hex.Substring(3, 2), 16) / 255.0;
        var b = Convert.ToInt32(hex.Substring(5, 2), 16) / 255.0;

        // Relative luminance (WCAG formula)
        var luminance = 0.2126 * r + 0.7152 * g + 0.0722 * b;
        return luminance > 0.5;
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
