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
    public void AdjustedV1_UsesIssueSpecifiedHomeAdvantage()
    {
        var parameters = EloCalculator.GetRulesetParameters(EloRulesetVersions.AdjustedV1);
        var result = EloCalculator.Calculate(90, 80, 1500m, 1500m, EloRulesetVersions.AdjustedV1);

        Assert.Equal(70m, parameters.HomeAdvantageElo);
        Assert.Equal(EloCalculator.ProbabilityScale, parameters.ProbabilityScale);
        Assert.Equal(EloCalculator.PointsPerEloMargin, parameters.PointsPerEloMargin);
        Assert.True(parameters.UsesMarginAdjustment);
        Assert.InRange(result.ExpectedHomeResult, 0.5993m, 0.5994m);
    }

    [Fact]
    public void CalculateExpectedResult_SmallerProbabilityScaleIsMoreConfident()
    {
        var defaultScale = EloCalculator.CalculateExpectedResult(100m);
        var smallerScale = EloCalculator.CalculateExpectedResult(100m, 300m);
        var largerScale = EloCalculator.CalculateExpectedResult(100m, 500m);

        Assert.InRange(defaultScale, 0.6400m, 0.6401m);
        Assert.True(smallerScale > defaultScale);
        Assert.True(largerScale < defaultScale);
    }

    [Fact]
    public void Calculate_UsesCustomProbabilityScaleInRulesetParameters()
    {
        var defaultScale = EloCalculator.Calculate(
            90,
            80,
            1500m,
            1400m,
            new EloRulesetParameters(1500m, 20, 0m, null, 1m, false));
        var smallerScale = EloCalculator.Calculate(
            90,
            80,
            1500m,
            1400m,
            new EloRulesetParameters(1500m, 20, 0m, null, 1m, false, 300m));

        Assert.True(smallerScale.ExpectedHomeResult > defaultScale.ExpectedHomeResult);
        Assert.True(smallerScale.HomeDelta < defaultScale.HomeDelta);
    }

    [Fact]
    public void LegacyRulesets_KeepExistingHomeAdvantage()
    {
        var basic = EloCalculator.GetRulesetParameters(EloRulesetVersions.BasicEloV1);
        var pointMargin = EloCalculator.GetRulesetParameters(EloRulesetVersions.PointMarginEloV1);

        Assert.Equal(100m, basic.HomeAdvantageElo);
        Assert.Null(basic.PointsPerEloMargin);
        Assert.False(basic.UsesMarginAdjustment);
        Assert.Equal(100m, pointMargin.HomeAdvantageElo);
        Assert.Equal(EloCalculator.PointsPerEloMargin, pointMargin.PointsPerEloMargin);
        Assert.True(pointMargin.UsesMarginAdjustment);
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
