namespace BasketElo.Domain.Elo;

public static class EloEvolutionLimits
{
    public const int DefaultPointsPerTeam = 120;
    public const int MinimumPointsPerTeam = 2;
    public const int MaximumPointsPerTeam = 120;

    public static int NormalizePointsPerTeam(int requested)
        => Math.Clamp(
            requested <= 0 ? DefaultPointsPerTeam : requested,
            MinimumPointsPerTeam,
            MaximumPointsPerTeam);

    public static IReadOnlyList<T> EvenlySample<T>(IReadOnlyList<T> source, int requestedPoints = DefaultPointsPerTeam)
    {
        var pointLimit = NormalizePointsPerTeam(requestedPoints);
        if (source.Count <= pointLimit)
        {
            return source;
        }

        var sampled = new List<T>(pointLimit);
        for (var index = 0; index < pointLimit; index++)
        {
            var sourceIndex = (int)Math.Round(
                index * (source.Count - 1d) / (pointLimit - 1d),
                MidpointRounding.AwayFromZero);
            sampled.Add(source[sourceIndex]);
        }

        return sampled;
    }
}
