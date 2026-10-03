using Kinsmen.Web.Helpers;
using Kinsmen.Web.Models.Api;

namespace Kinsmen.Web.Tests;

public sealed class WorkingHoursFormatterTests
{
    // The formatter separates days and times with an en dash, not a hyphen.
    private const string Dash = "\u2013";

    private static WorkingPeriod Day(int weekday, int startMinute, int endMinute)
        => new() { Weekday = weekday, StartMinute = startMinute, EndMinute = endMinute };

    private static int Hm(int hour, int minute = 0) => hour * 60 + minute;

    [Fact]
    public void NoHoursSaysSo()
        => Assert.Equal("Hours not set", WorkingHoursFormatter.Format(new List<WorkingPeriod>()));

    [Fact]
    public void ConsecutiveDaysWithTheSameHoursCollapseToARange()
    {
        var hours = new List<WorkingPeriod>
        {
            Day(1, Hm(9), Hm(17)), Day(2, Hm(9), Hm(17)), Day(3, Hm(9), Hm(17)),
            Day(4, Hm(9), Hm(17)), Day(5, Hm(9), Hm(17)), Day(6, Hm(9), Hm(17))
        };

        Assert.Equal($"Mon{Dash}Sat 09:00{Dash}17:00", WorkingHoursFormatter.Format(hours));
    }

    [Fact]
    public void ASingleDayIsNotShownAsARange()
    {
        var hours = new List<WorkingPeriod> { Day(6, Hm(9), Hm(13)) };

        Assert.Equal($"Sat 09:00{Dash}13:00", WorkingHoursFormatter.Format(hours));
    }

    [Fact]
    public void SundayIsWeekdaySeven()
    {
        var hours = new List<WorkingPeriod> { Day(7, Hm(9), Hm(15)) };

        Assert.Equal($"Sun 09:00{Dash}15:00", WorkingHoursFormatter.Format(hours));
    }

    [Fact]
    public void ADayWithDifferentHoursStartsANewRun()
    {
        var hours = new List<WorkingPeriod>
        {
            Day(1, Hm(9), Hm(18)), Day(2, Hm(9), Hm(18)), Day(3, Hm(9), Hm(18)),
            Day(4, Hm(9), Hm(18)), Day(5, Hm(9), Hm(18)), Day(6, Hm(9), Hm(18)),
            Day(7, Hm(9), Hm(15))
        };

        Assert.Equal(
            $"Mon{Dash}Sat 09:00{Dash}18:00; Sun 09:00{Dash}15:00",
            WorkingHoursFormatter.Format(hours));
    }

    [Fact]
    public void HoursChangingMidWeekSplitTheRun()
    {
        var hours = new List<WorkingPeriod>
        {
            Day(1, Hm(9), Hm(17)), Day(2, Hm(9), Hm(17)), Day(3, Hm(10), Hm(14))
        };

        Assert.Equal(
            $"Mon{Dash}Tue 09:00{Dash}17:00; Wed 10:00{Dash}14:00",
            WorkingHoursFormatter.Format(hours));
    }

    [Fact]
    public void DaysWithAGapBetweenThemStaySeparateEvenWithIdenticalHours()
    {
        var hours = new List<WorkingPeriod> { Day(1, Hm(9), Hm(17)), Day(3, Hm(9), Hm(17)) };

        Assert.Equal(
            $"Mon 09:00{Dash}17:00; Wed 09:00{Dash}17:00",
            WorkingHoursFormatter.Format(hours));
    }

    [Fact]
    public void InputOrderDoesNotMatter()
    {
        var hours = new List<WorkingPeriod>
        {
            Day(3, Hm(9), Hm(17)), Day(1, Hm(9), Hm(17)), Day(2, Hm(9), Hm(17))
        };

        Assert.Equal($"Mon{Dash}Wed 09:00{Dash}17:00", WorkingHoursFormatter.Format(hours));
    }

    [Fact]
    public void MinutesPastTheHourArePreserved()
    {
        var hours = new List<WorkingPeriod> { Day(2, Hm(9, 30), Hm(17, 30)) };

        Assert.Equal($"Tue 09:30{Dash}17:30", WorkingHoursFormatter.Format(hours));
    }
}
