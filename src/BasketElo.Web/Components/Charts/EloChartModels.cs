namespace BasketElo.Web.Components.Charts;

public sealed record EloChartSeries(
    Guid? TeamId,
    string TeamName,
    string Color,
    IReadOnlyList<EloChartPoint> Points);

public sealed record EloChartPoint(
    DateTime GameDateTimeUtc,
    decimal Elo,
    string TeamName,
    string Color,
    decimal? EloDelta = null,
    int? Rank = null)
{
    public int ActiveDateIndex { get; init; }
}

public sealed record EloChartViewportRange(
    DateTime FromUtc,
    DateTime ToUtc);

public enum EloChartSamplingMode
{
    GameByGame,
    Weekly,
    Monthly
}
