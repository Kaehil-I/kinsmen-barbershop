using Kinsmen.Web.Models.Api;

namespace Kinsmen.Web.Helpers;

/// <summary>Formats a barber's WorkingPeriod list into a short human-readable summary,
/// e.g. "Mon–Sat 09:00–17:00", grouping consecutive weekdays that share the same hours.</summary>
public static class WorkingHoursFormatter
{
    private static readonly string[] WeekdayNames =
        ["", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"]; // index 1-7, ISO weekday

    public static string Format(IReadOnlyCollection<WorkingPeriod> hours)
    {
        if (hours.Count == 0)
        {
            return "Hours not set";
        }

        var ordered = hours.OrderBy(h => h.Weekday).ToList();
        var runs = new List<(int FirstWeekday, int LastWeekday, int Start, int End)>();

        foreach (var period in ordered)
        {
            var last = runs.Count > 0 ? runs[^1] : default;

            if (runs.Count > 0
                && last.Start == period.StartMinute
                && last.End == period.EndMinute
                && last.LastWeekday == period.Weekday - 1)
            {
                runs[^1] = (last.FirstWeekday, period.Weekday, last.Start, last.End);
            }
            else
            {
                runs.Add((period.Weekday, period.Weekday, period.StartMinute, period.EndMinute));
            }
        }

        return string.Join("; ", runs.Select(FormatRun));
    }

    private static string FormatRun((int FirstWeekday, int LastWeekday, int Start, int End) run)
    {
        var days = run.FirstWeekday == run.LastWeekday
            ? WeekdayNames[run.FirstWeekday]
            : $"{WeekdayNames[run.FirstWeekday]}\u2013{WeekdayNames[run.LastWeekday]}";

        return $"{days} {FormatMinutes(run.Start)}\u2013{FormatMinutes(run.End)}";
    }

    private static string FormatMinutes(int minutesSinceMidnight) =>
        TimeSpan.FromMinutes(minutesSinceMidnight).ToString(@"hh\:mm");
}
