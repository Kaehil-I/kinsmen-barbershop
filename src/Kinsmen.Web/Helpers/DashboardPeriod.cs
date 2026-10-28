namespace Kinsmen.Web.Helpers;

/// <summary>The dates the dashboard covers, worked out from the filter in the URL. Presets are relative to today
/// in the shop's timezone. A bad custom range falls back to this month and says why, rather than showing an error page.</summary>
public sealed record DashboardPeriod(string Preset, DateOnly From, DateOnly To, string Label, string? Problem = null)
{
    public const string Week = "week", Month = "month", Last30 = "last30", Next30 = "next30", Custom = "custom";
    public const int MaxDays = 366;

    public static readonly (string Preset, string Label)[] Presets =
        [(Week, "This week"), (Month, "This month"), (Last30, "Last 30 days"), (Next30, "Next 30 days")];

    public static DashboardPeriod Resolve(string? preset, DateOnly? from, DateOnly? to, DateOnly today)
    {
        switch (preset)
        {
            case Week:
                var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7)); // Weeks run Monday to Sunday, like the shop's calendar.
                return new(Week, monday, monday.AddDays(6), "This week");
            case Last30:
                return new(Last30, today.AddDays(-29), today, "Last 30 days");
            case Next30:
                return new(Next30, today, today.AddDays(29), "Next 30 days");
            case Custom:
                if (from is null || to is null) return Fallback(today, "Choose both a start and an end date.");
                if (to < from) return Fallback(today, "The end date can't be before the start date.");
                if (to.Value.DayNumber - from.Value.DayNumber >= MaxDays) return Fallback(today, $"Choose a period of at most {MaxDays} days.");
                return new(Custom, from.Value, to.Value, string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{from:d MMM yyyy} to {to:d MMM yyyy}"));
            default:
                return Fallback(today, null);
        }
    }

    private static DashboardPeriod Fallback(DateOnly today, string? problem)
    {
        var first = new DateOnly(today.Year, today.Month, 1);
        return new(Month, first, first.AddMonths(1).AddDays(-1), "This month", problem);
    }
}
