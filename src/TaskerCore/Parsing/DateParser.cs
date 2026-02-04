namespace TaskerCore.Parsing;

using System.Text.RegularExpressions;

public static partial class DateParser
{
    public static DateOnly? Parse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var today = DateOnly.FromDateTime(DateTime.Today);
        var normalized = input.Trim().ToLowerInvariant();

        return normalized switch
        {
            "today" => today,
            "tomorrow" => today.AddDays(1),
            "yesterday" => today.AddDays(-1),
            _ => TryParseRelative(normalized, today)
                 ?? TryParseDayOfWeek(normalized, today)
                 ?? TryParseMonthDay(normalized, today)
                 ?? TryParseStandard(input)
        };
    }

    [GeneratedRegex(@"^\+(\d+)([dwm])$")]
    private static partial Regex RelativeRegex();

    private static DateOnly? TryParseRelative(string input, DateOnly today)
    {
        var match = RelativeRegex().Match(input);
        if (!match.Success) return null;

        var count = int.Parse(match.Groups[1].Value);
        return match.Groups[2].Value switch
        {
            "d" => today.AddDays(count),
            "w" => today.AddDays(count * 7),
            "m" => today.AddMonths(count),
            _ => null
        };
    }

    private static readonly Dictionary<string, DayOfWeek> DayMap = new()
    {
        ["mon"] = DayOfWeek.Monday, ["monday"] = DayOfWeek.Monday,
        ["tue"] = DayOfWeek.Tuesday, ["tuesday"] = DayOfWeek.Tuesday,
        ["wed"] = DayOfWeek.Wednesday, ["wednesday"] = DayOfWeek.Wednesday,
        ["thu"] = DayOfWeek.Thursday, ["thursday"] = DayOfWeek.Thursday,
        ["fri"] = DayOfWeek.Friday, ["friday"] = DayOfWeek.Friday,
        ["sat"] = DayOfWeek.Saturday, ["saturday"] = DayOfWeek.Saturday,
        ["sun"] = DayOfWeek.Sunday, ["sunday"] = DayOfWeek.Sunday
    };

    private static DateOnly? TryParseDayOfWeek(string input, DateOnly today)
    {
        if (!DayMap.TryGetValue(input, out var targetDay))
            return null;

        var daysUntil = ((int)targetDay - (int)today.DayOfWeek + 7) % 7;
        if (daysUntil == 0) daysUntil = 7; // Next week if today
        return today.AddDays(daysUntil);
    }

    [GeneratedRegex(@"^(jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec)(\d{1,2})$")]
    private static partial Regex MonthDayRegex();

    private static readonly Dictionary<string, int> MonthMap = new()
    {
        ["jan"] = 1, ["feb"] = 2, ["mar"] = 3, ["apr"] = 4,
        ["may"] = 5, ["jun"] = 6, ["jul"] = 7, ["aug"] = 8,
        ["sep"] = 9, ["oct"] = 10, ["nov"] = 11, ["dec"] = 12
    };

    private static DateOnly? TryParseMonthDay(string input, DateOnly today)
    {
        var match = MonthDayRegex().Match(input);
        if (!match.Success) return null;

        var month = MonthMap[match.Groups[1].Value];
        var day = int.Parse(match.Groups[2].Value);

        try
        {
            var result = new DateOnly(today.Year, month, day);
            if (result < today) result = result.AddYears(1);
            return result;
        }
        catch
        {
            return null;
        }
    }

    private static DateOnly? TryParseStandard(string input)
    {
        if (DateOnly.TryParse(input, out var date))
            return date;
        return null;
    }
}
