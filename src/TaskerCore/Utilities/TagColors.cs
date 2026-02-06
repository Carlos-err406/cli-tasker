namespace TaskerCore.Utilities;

public static class TagColors
{
    // Curated palette - all colors are dark enough for white text on dark backgrounds
    private static readonly string[] Palette =
    [
        "#2563EB", // Blue
        "#059669", // Emerald
        "#B45309", // Amber
        "#DC2626", // Red
        "#7C3AED", // Violet
        "#DB2777", // Pink
        "#0891B2", // Cyan
        "#4D7C0F", // Lime (darkened)
        "#EA580C", // Orange
        "#4F46E5", // Indigo
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
