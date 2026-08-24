namespace BasketElo.Web.Components.Charts;

public sealed record EloChartSeries(
    Guid? TeamId,
    string TeamName,
    string Color,
    IReadOnlyList<EloChartPoint> Points,
    int RatedPointCount = 0,
    int? VisibleRatedPointCount = null);

public sealed record EloChartPoint(
    DateTime GameDateTimeUtc,
    decimal Elo,
    string TeamName,
    string Color,
    decimal? EloDelta = null,
    int? Rank = null,
    Guid? GameId = null)
{
    public int ActiveDateIndex { get; init; }
}

public sealed record EloChartViewportRange(
    DateTime FromUtc,
    DateTime ToUtc);

public static class EloChartData
{
    public static IReadOnlyList<EloChartPoint> MergePoints(
        IReadOnlyList<EloChartPoint> basePoints,
        IReadOnlyList<EloChartPoint> refinedPoints)
    {
        if (refinedPoints.Count == 0)
        {
            return basePoints;
        }

        var refinedDates = refinedPoints
            .Select(point => point.GameDateTimeUtc)
            .ToHashSet();

        return basePoints
            .Where(point => !refinedDates.Contains(point.GameDateTimeUtc))
            .Concat(refinedPoints)
            .OrderBy(point => point.GameDateTimeUtc)
            .ToList();
    }

    public static IReadOnlyList<EloChartPoint> ReplacePointsInRange(
        IReadOnlyList<EloChartPoint> basePoints,
        IReadOnlyList<EloChartPoint> refinedPoints,
        DateTime? fromUtc = null,
        DateTime? toUtc = null)
    {
        if (refinedPoints.Count == 0)
        {
            return basePoints;
        }

        var orderedRefinedPoints = refinedPoints
            .OrderBy(point => point.GameDateTimeUtc)
            .ToList();
        var rangeFromUtc = fromUtc ?? orderedRefinedPoints[0].GameDateTimeUtc;
        var rangeToUtc = toUtc ?? orderedRefinedPoints[^1].GameDateTimeUtc;

        return basePoints
            .Where(point => point.GameDateTimeUtc < rangeFromUtc || point.GameDateTimeUtc > rangeToUtc)
            .Concat(orderedRefinedPoints)
            .OrderBy(point => point.GameDateTimeUtc)
            .ToList();
    }
}

public enum EloChartSamplingMode
{
    GameByGame,
    Weekly,
    Monthly
}
