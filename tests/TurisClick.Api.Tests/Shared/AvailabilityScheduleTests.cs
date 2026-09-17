using TurisClick.Api.Shared.Exceptions;
using TurisClick.Api.Shared.Scheduling;

namespace TurisClick.Api.Tests.Shared;

public class AvailabilityScheduleTests
{
    // 2027-03-01 es lunes.
    private static readonly DateOnly Monday = new(2027, 3, 1);

    [Fact]
    public void Presets_SelectTheExpectedDays()
    {
        Assert.Equal(7, AvailabilitySchedule.ResolveWeekdays("EVERY_DAY", null).Count);
        Assert.Equal(
            new HashSet<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday },
            AvailabilitySchedule.ResolveWeekdays("weekdays", null));
        Assert.Equal(
            new HashSet<DayOfWeek> { DayOfWeek.Saturday, DayOfWeek.Sunday },
            AvailabilitySchedule.ResolveWeekdays("WEEKENDS", [1, 2, 3]));
    }

    [Fact]
    public void Custom_UsesExactlyTheGivenDays_AndIsTheDefault()
    {
        Assert.Equal(new HashSet<DayOfWeek> { DayOfWeek.Sunday, DayOfWeek.Wednesday }, AvailabilitySchedule.ResolveWeekdays("CUSTOM", [0, 3, 3]));
        Assert.Equal(new HashSet<DayOfWeek> { DayOfWeek.Friday }, AvailabilitySchedule.ResolveWeekdays(null, [5]));
    }

    [Theory]
    [InlineData("CUSTOM", new int[0])]
    [InlineData("CUSTOM", new[] { 7 })]
    [InlineData("CUSTOM", new[] { -1 })]
    [InlineData("MONTHLY", new[] { 1 })]
    public void InvalidPatterns_Throw(string preset, int[] days)
    {
        Assert.Throws<ValidationAppException>(() => AvailabilitySchedule.ResolveWeekdays(preset, days));
    }

    [Fact]
    public void ExpandDates_IncludesBothEnds_AndOnlyMatchingDays()
    {
        var weekends = AvailabilitySchedule.ResolveWeekdays("WEEKENDS", null);

        var dates = AvailabilitySchedule.ExpandDates(Monday, Monday.AddDays(13), weekends);

        Assert.Equal([new(2027, 3, 6), new(2027, 3, 7), new(2027, 3, 13), new(2027, 3, 14)], dates);
    }

    [Fact]
    public void ExpandDates_SingleDayRange()
    {
        var everyDay = AvailabilitySchedule.ResolveWeekdays("EVERY_DAY", null);
        Assert.Equal([Monday], AvailabilitySchedule.ExpandDates(Monday, Monday, everyDay));
    }

    [Fact]
    public void Range_RejectsBackwardsPastAndTooLong()
    {
        Assert.Throws<ValidationAppException>(() => AvailabilitySchedule.EnsureValidRange(Monday, Monday.AddDays(-1), Monday));
        Assert.Throws<ValidationAppException>(() => AvailabilitySchedule.EnsureValidRange(Monday.AddDays(-1), Monday, Monday));
        Assert.Throws<ValidationAppException>(() => AvailabilitySchedule.EnsureValidRange(Monday, Monday.AddDays(366), Monday));

        AvailabilitySchedule.EnsureValidRange(Monday, Monday.AddDays(365), Monday); // 366 días exactos: válido
    }

    [Fact]
    public void StartTimes_AreDistinctSorted_AndEmptyMeansFullDay()
    {
        Assert.Equal([null], AvailabilitySchedule.NormalizeStartTimes([]));
        Assert.Equal(
            [new TimeOnly(9, 0), new TimeOnly(15, 30)],
            AvailabilitySchedule.NormalizeStartTimes([new TimeOnly(15, 30), new TimeOnly(9, 0), new TimeOnly(9, 0)]));
        Assert.Throws<ValidationAppException>(() =>
            AvailabilitySchedule.NormalizeStartTimes(Enumerable.Range(0, 13).Select(h => new TimeOnly(h, 0))));
    }

    [Fact]
    public void Limit_RejectsEmptyAndOversizedGenerations()
    {
        Assert.Throws<ValidationAppException>(() => AvailabilitySchedule.EnsureWithinLimit(0));
        Assert.Throws<ValidationAppException>(() => AvailabilitySchedule.EnsureWithinLimit(AvailabilitySchedule.MaxSlotsPerRequest + 1));
        AvailabilitySchedule.EnsureWithinLimit(AvailabilitySchedule.MaxSlotsPerRequest);
    }
}
