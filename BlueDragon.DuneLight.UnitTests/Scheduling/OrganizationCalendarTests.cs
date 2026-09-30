#nullable disable
using System;
using System.Collections.Generic;
using BlueDragon.DuneLight.Core.Shared;
using BlueDragon.DuneLight.Infrastructure.Utils;

namespace BlueDragon.DuneLight.UnitTests.Scheduling;

/// <summary>
/// Timezone foundation: <see cref="OrganizationCalendar"/> is the only local ↔ UTC conversion for scheduling. Pure unit
/// tests — the results must be identical on any host timezone (the suite is run under UTC, Europe/Zagreb, New York
/// and Tokyo). Europe/Zagreb in 2031: CET (+01:00) until 2031-03-30 02:00, CEST (+02:00) until 2031-10-26 03:00.
/// </summary>
public class OrganizationCalendarTests
{
    private static readonly OrganizationCalendar Zagreb = OrganizationCalendar.For("Europe/Zagreb");
    private static readonly OrganizationCalendar Utc = OrganizationCalendar.For("UTC");

    private static DateTimeOffset Z(int y, int mo, int d, int h, int mi = 0) => new(y, mo, d, h, mi, 0, TimeSpan.Zero);

    #region Standard / summer time

    [Fact]
    public void StandardTime_LocalWallClockIsUtcPlusOne()
    {
        DateTimeOffset instant = Zagreb.ToInstant(new DateOnly(2031, 1, 15), new TimeSpan(10, 0, 0));

        Assert.Equal(Z(2031, 1, 15, 9), instant);
        Assert.Equal(TimeSpan.Zero, instant.Offset);
        Assert.Equal(new DateOnly(2031, 1, 15), Zagreb.LocalDate(instant));
        Assert.Equal(new TimeSpan(10, 0, 0), Zagreb.LocalTimeOfDay(instant));
    }

    [Fact]
    public void SummerTime_LocalWallClockIsUtcPlusTwo()
    {
        DateTimeOffset instant = Zagreb.ToInstant(new DateOnly(2031, 7, 15), new TimeSpan(10, 0, 0));

        Assert.Equal(Z(2031, 7, 15, 8), instant);
        Assert.Equal(new TimeSpan(10, 0, 0), Zagreb.LocalTimeOfDay(instant));
    }

    [Fact]
    public void LocalDate_IsTheOrganizationsCalendarDate_NotTheUtcDate()
    {
        // 23:30 UTC on the 2nd is already 00:30 on the 3rd in Zagreb; 22:30 UTC on the 3rd is still the 3rd.
        Assert.Equal(new DateOnly(2031, 3, 3), Zagreb.LocalDate(Z(2031, 3, 2, 23, 30)));
        Assert.Equal(new TimeSpan(0, 30, 0), Zagreb.LocalTimeOfDay(Z(2031, 3, 2, 23, 30)));
        Assert.Equal(new DateOnly(2031, 3, 3), Zagreb.LocalDate(Z(2031, 3, 3, 22, 30)));
        Assert.Equal(new DateOnly(2031, 3, 2), Utc.LocalDate(Z(2031, 3, 2, 23, 30)));
    }

    [Fact]
    public void Conversion_DoesNotDependOnTheOffsetOfTheInput()
    {
        DateTimeOffset utc = Z(2031, 3, 3, 9);
        DateTimeOffset sameInstantAsTokyo = utc.ToOffset(TimeSpan.FromHours(9));
        DateTimeOffset sameInstantAsNewYork = utc.ToOffset(TimeSpan.FromHours(-5));

        Assert.Equal(Zagreb.ToLocal(utc), Zagreb.ToLocal(sameInstantAsTokyo));
        Assert.Equal(Zagreb.ToLocal(utc), Zagreb.ToLocal(sameInstantAsNewYork));
        Assert.Equal(new DateTime(2031, 3, 3, 10, 0, 0), Zagreb.ToLocal(utc));
        Assert.Equal(DateTimeKind.Unspecified, Zagreb.ToLocal(utc).Kind);
    }

    [Fact]
    public void Utc_IsTheIdentityCalendar()
    {
        Assert.Equal(Z(2031, 3, 3, 10), Utc.ToInstant(new DateOnly(2031, 3, 3), new TimeSpan(10, 0, 0)));
        Assert.Equal(new TimeSpan(10, 0, 0), Utc.LocalTimeOfDay(Z(2031, 3, 3, 10)));
    }

    #endregion

    #region DST transitions

    [Fact]
    public void SpringForward_BeforeAndAfterTheGap_MapExactly()
    {
        DateOnly transition = new(2031, 3, 30);

        Assert.Equal(Z(2031, 3, 30, 0, 30), Zagreb.ToInstant(transition, new TimeSpan(1, 30, 0))); // CET
        Assert.Equal(Z(2031, 3, 30, 1, 30), Zagreb.ToInstant(transition, new TimeSpan(3, 30, 0))); // CEST
        Assert.Equal(Z(2031, 3, 29, 23), Zagreb.StartOfDay(transition));
    }

    [Fact]
    public void SpringForward_ANonExistentLocalTime_IsShiftedForwardByTheGap()
    {
        // 02:30 does not exist on 2031-03-30 in Zagreb: read with the pre-transition offset → 01:30Z = 03:30 CEST.
        DateTimeOffset instant = Zagreb.ToInstant(new DateOnly(2031, 3, 30), new TimeSpan(2, 30, 0));

        Assert.Equal(Z(2031, 3, 30, 1, 30), instant);
        Assert.Equal(new TimeSpan(3, 30, 0), Zagreb.LocalTimeOfDay(instant));
    }

    [Fact]
    public void FallBack_AnAmbiguousLocalTime_TakesTheFirstOccurrence()
    {
        // 02:30 happens twice on 2031-10-26 (CEST then CET): the first one (CEST, 00:30Z) is chosen.
        Assert.Equal(Z(2031, 10, 26, 0, 30), Zagreb.ToInstant(new DateOnly(2031, 10, 26), new TimeSpan(2, 30, 0)));
        Assert.Equal(Z(2031, 10, 26, 9), Zagreb.ToInstant(new DateOnly(2031, 10, 26), new TimeSpan(10, 0, 0))); // CET after the change
        Assert.Equal(Z(2031, 10, 25, 22), Zagreb.StartOfDay(new DateOnly(2031, 10, 26)));
    }

    [Fact]
    public void RoundTrip_LocalToUtcToLocal_IsExact_ThroughoutTheYear_OutsideTheSpringGap()
    {
        for (DateOnly date = new(2031, 1, 1); date <= new DateOnly(2031, 12, 31); date = date.AddDays(1))
        {
            foreach (TimeSpan time in new[] { TimeSpan.Zero, new TimeSpan(1, 59, 0), new TimeSpan(3, 0, 0), new TimeSpan(10, 15, 0), new TimeSpan(23, 59, 0) })
            {
                DateTimeOffset instant = Zagreb.ToInstant(date, time);
                Assert.Equal(TimeSpan.Zero, instant.Offset);
                Assert.Equal(date, Zagreb.LocalDate(instant));
                Assert.Equal(time, Zagreb.LocalTimeOfDay(instant));
            }
        }
    }

    [Fact]
    public void RepeatAtLocalTime_KeepsTheLocalWallClockAcrossDst()
    {
        DateTimeOffset first = Zagreb.ToInstant(new DateOnly(2031, 3, 24), new TimeSpan(10, 0, 0)); // Monday, CET

        List<DateTimeOffset> weekly = Zagreb.RepeatAtLocalTime(first, Z(2031, 4, 7, 12), stepDays: 7);

        Assert.Equal(new[] { Z(2031, 3, 24, 9), Z(2031, 3, 31, 8), Z(2031, 4, 7, 8) }, weekly);
        Assert.All(weekly, o => Assert.Equal(new TimeSpan(10, 0, 0), Zagreb.LocalTimeOfDay(o)));
    }

    [Fact]
    public void RepeatAtLocalTime_StopsAtTheEndInstant_AndStartsExactlyAtFirst()
    {
        DateTimeOffset first = new(2031, 3, 3, 10, 0, 0, TimeSpan.FromHours(1)); // same instant as 09:00Z

        List<DateTimeOffset> daily = Zagreb.RepeatAtLocalTime(first, Z(2031, 3, 5, 9), stepDays: 1);

        Assert.Equal(3, daily.Count);
        Assert.Equal(first, daily[0]);
        Assert.Equal(Z(2031, 3, 5, 9), daily[2]); // end is inclusive
        Assert.Throws<ArgumentOutOfRangeException>(() => Zagreb.RepeatAtLocalTime(first, first, 0));
    }

    #endregion

    #region Timezone ids

    [Theory]
    [InlineData("Europe/Zagreb", true)]
    [InlineData("UTC", true)]
    [InlineData("America/New_York", true)]
    [InlineData("Central European Standard Time", false)] // Windows id
    [InlineData("Europe/Atlantis", false)]
    [InlineData("", false)]
    [InlineData(" Europe/Zagreb", false)]
    [InlineData(null, false)]
    public void OnlyKnownIanaIdsAreSupported(string id, bool supported)
    {
        Assert.Equal(supported, OrganizationTimeZones.IsSupported(id));
        if (!supported)
            Assert.Throws<ArgumentException>(() => OrganizationCalendar.For(id));
    }

    [Fact]
    public void TheDefaultTimeZoneIsSupported()
    {
        Assert.Equal("Europe/Zagreb", OrganizationTimeZones.Default);
        Assert.True(OrganizationTimeZones.IsSupported(OrganizationTimeZones.Default));
    }

    #endregion
}
