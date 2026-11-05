using BasketElo.Domain.Admin;
using Xunit;

namespace BasketElo.Domain.Tests;

public sealed class CompetitionGameGapPolicyTests
{
    [Fact]
    public void AlertsAfterTwoWeeksWithoutAGame()
    {
        var lastGameUtc = Utc(2026, 10, 1);

        Assert.False(CompetitionGameGapPolicy.ShouldAlert(lastGameUtc, Utc(2026, 10, 14, 23, 59)));
        Assert.True(CompetitionGameGapPolicy.ShouldAlert(lastGameUtc, Utc(2026, 10, 15)));
    }

    [Theory]
    [InlineData(2026, 6, 15)]
    [InlineData(2026, 8, 20)]
    [InlineData(2026, 9, 30)]
    public void NeverAlertsDuringSummerExclusion(int year, int month, int day)
    {
        Assert.False(CompetitionGameGapPolicy.ShouldAlert(
            Utc(2026, 5, 1),
            Utc(year, month, day)));
    }

    [Fact]
    public void SummerExclusionDoesNotCountTowardTwoWeeks()
    {
        var lastGameUtc = Utc(2026, 6, 10);

        Assert.False(CompetitionGameGapPolicy.ShouldAlert(lastGameUtc, Utc(2026, 10, 9)));
        Assert.True(CompetitionGameGapPolicy.ShouldAlert(lastGameUtc, Utc(2026, 10, 10)));
    }

    [Fact]
    public void OctoberFirstIsTheFirstMonitoredDayAfterSummer()
    {
        Assert.False(CompetitionGameGapPolicy.IsSummerExclusion(Utc(2026, 10, 1)));
    }

    private static DateTime Utc(int year, int month, int day, int hour = 0, int minute = 0) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Utc);
}
