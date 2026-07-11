namespace BasketElo.Domain.Elo;

public static class EloCalculator
{
    public const decimal BaseRating = 1500m;
    public const int KFactor = 20;
    public const decimal AdjustedV1HomeAdvantageElo = 70m;
    public const decimal LegacyHomeAdvantageElo = 100m;
    public const decimal PointsPerEloMargin = 28m;
    public const decimal CompetitionWeight = 1m;
    public const decimal MaxMarginMultiplier = 1.5m;
    public const decimal MinMarginMultiplier = 1m / MaxMarginMultiplier;

    public static EloGameCalculation Calculate(
        short homeScore,
        short awayScore,
        decimal homeElo,
        decimal awayElo,
        string rulesetVersion)
    {
        return Calculate(homeScore, awayScore, homeElo, awayElo, GetRulesetParameters(rulesetVersion));
    }

    public static EloGameCalculation Calculate(
        short homeScore,
        short awayScore,
        decimal homeElo,
        decimal awayElo,
        EloRulesetParameters ruleset)
    {
        if (homeScore == awayScore)
        {
            throw new ArgumentException("ELO calculation requires a winner.", nameof(homeScore));
        }

        var eloDiff = homeElo + ruleset.HomeAdvantageElo - awayElo;
        var expectedHomeResult = CalculateExpectedResult(eloDiff);
        var homeActualResult = homeScore > awayScore ? 1m : 0m;
        var baseHomeDelta = ruleset.KFactor * (homeActualResult - expectedHomeResult);
        var marginMultiplier = ruleset.UsesMarginAdjustment
            ? CalculateMarginMultiplier(homeScore, awayScore, eloDiff, ruleset.PointsPerEloMargin ?? PointsPerEloMargin)
            : 1m;

        return new EloGameCalculation(
            expectedHomeResult,
            homeActualResult,
            baseHomeDelta * marginMultiplier * ruleset.CompetitionWeight,
            marginMultiplier);
    }

    public static EloRulesetParameters GetRulesetParameters(string rulesetVersion)
    {
        return rulesetVersion switch
        {
            EloRulesetVersions.AdjustedV1 => new EloRulesetParameters(
                BaseRating,
                KFactor,
                AdjustedV1HomeAdvantageElo,
                PointsPerEloMargin,
                CompetitionWeight,
                true),
            EloRulesetVersions.BasicEloV1 => new EloRulesetParameters(
                BaseRating,
                KFactor,
                LegacyHomeAdvantageElo,
                null,
                CompetitionWeight,
                false),
            EloRulesetVersions.PointMarginEloV1 => new EloRulesetParameters(
                BaseRating,
                KFactor,
                LegacyHomeAdvantageElo,
                PointsPerEloMargin,
                CompetitionWeight,
                true),
            _ => throw new ArgumentException($"Unsupported ELO ruleset '{rulesetVersion}'.", nameof(rulesetVersion))
        };
    }

    public static decimal CalculateExpectedResult(decimal eloDiff)
    {
        var expected = 1d / (1d + Math.Pow(10d, -(double)eloDiff / 400d));
        return (decimal)expected;
    }

    private static decimal CalculateMarginMultiplier(short homeScore, short awayScore, decimal eloDiff, decimal pointsPerEloMargin)
    {
        var actualMargin = homeScore - awayScore;
        var expectedMargin = eloDiff / pointsPerEloMargin;
        var winnerActualMargin = Math.Abs(actualMargin);
        var winnerExpectedMargin = actualMargin > 0 ? expectedMargin : -expectedMargin;
        var winnerOverperformance = winnerActualMargin - winnerExpectedMargin;

        if (winnerOverperformance >= 0)
        {
            var boost = Math.Min(Math.Log((double)winnerOverperformance + 1d) / 5d, 0.5d);
            return Math.Min(1m + (decimal)boost, MaxMarginMultiplier);
        }

        var dampener = Math.Min(Math.Log((double)Math.Abs(winnerOverperformance) + 1d) / 5d, 0.5d);
        return Math.Max(1m / (1m + (decimal)dampener), MinMarginMultiplier);
    }
}

public sealed record EloGameCalculation(
    decimal ExpectedHomeResult,
    decimal HomeActualResult,
    decimal HomeDelta,
    decimal MarginMultiplier);

public sealed record EloRulesetParameters(
    decimal BaseRating,
    int KFactor,
    decimal HomeAdvantageElo,
    decimal? PointsPerEloMargin,
    decimal CompetitionWeight,
    bool UsesMarginAdjustment);
