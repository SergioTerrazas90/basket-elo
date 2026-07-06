using BasketElo.Domain.Elo;
using Xunit;

namespace BasketElo.Domain.Tests;

public class EloCalculatorTests
{
    [Fact]
    public void BasicRuleset_HomeWin_UsesExpectedScoreAndNoMarginAdjustment()
    {
        var result = EloCalculator.Calculate(90, 80, 1500m, 1500m, EloRulesetVersions.BasicEloV1);

        Assert.InRange(result.ExpectedHomeResult, 0.6400m, 0.6401m);
        Assert.Equal(1m, result.HomeActualResult);
        Assert.Equal(1m, result.MarginMultiplier);
        Assert.InRange(result.HomeDelta, 7.19m, 7.21m);
    }

    [Fact]
    public void BasicRuleset_AwayUpset_TransfersEqualRatingInOppositeDirection()
    {
        var result = EloCalculator.Calculate(80, 81, 1500m, 1500m, EloRulesetVersions.BasicEloV1);

        Assert.Equal(0m, result.HomeActualResult);
        Assert.InRange(result.HomeDelta, -12.81m, -12.79m);
        Assert.Equal(1500m, 1500m + result.HomeDelta - result.HomeDelta);
    }

    [Fact]
    public void PointMarginRuleset_BiggerOverperformance_ProducesBiggerGain()
    {
        var narrow = EloCalculator.Calculate(90, 89, 1500m, 1500m, EloRulesetVersions.PointMarginEloV1);
        var blowout = EloCalculator.Calculate(110, 80, 1500m, 1500m, EloRulesetVersions.PointMarginEloV1);

        Assert.InRange(narrow.MarginMultiplier, EloCalculator.MinMarginMultiplier, EloCalculator.MaxMarginMultiplier);
        Assert.InRange(blowout.MarginMultiplier, EloCalculator.MinMarginMultiplier, EloCalculator.MaxMarginMultiplier);
        Assert.True(blowout.MarginMultiplier > narrow.MarginMultiplier);
        Assert.True(blowout.HomeDelta > narrow.HomeDelta);
    }

    [Fact]
    public void Calculate_RejectsTiesAndUnknownRulesets()
    {
        Assert.Throws<ArgumentException>(() =>
            EloCalculator.Calculate(80, 80, 1500m, 1500m, EloRulesetVersions.BasicEloV1));
        Assert.Throws<ArgumentException>(() =>
            EloCalculator.Calculate(80, 79, 1500m, 1500m, "future-v1"));
    }
}
