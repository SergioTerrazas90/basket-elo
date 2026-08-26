namespace BasketElo.Domain.Elo;

public static class HomeAdvantagePolicies
{
    public const string Automatic = "automatic";
    public const string Neutral = "neutral";
    public const string Home = "home";

    public static readonly IReadOnlyCollection<string> All =
        [Automatic, Neutral, Home];

    public static bool IsValid(string? value) =>
        value is not null && All.Contains(value.Trim().ToLowerInvariant(), StringComparer.Ordinal);
}

public static class HomeAdvantagePolicy
{
    private static readonly string[] NeutralStageMarkers =
    [
        "final four",
        "final 4",
        "final-four",
        "final eight",
        "final 8",
        "final-eight",
        "top four",
        "top 4",
        "top-four",
        "final day",
        "final-day"
    ];

    private static readonly string[] NeutralCompetitionMarkers =
    [
        "league cup",
        "leaders cup",
        "semaine des as"
    ];

    private static readonly string[] NeutralCompetitionNames =
    [
        "eurobasket",
        "fiba eurobasket",
        "fiba eurobasket division b",
        "afrobasket",
        "fiba afrobasket",
        "fiba asia cup",
        "fiba americup",
        "fiba oceania championship",
        "centrobasket championship",
        "cocaba championship",
        "south american championship",
        "caribbean basketball championship",
        "fiba world cup",
        "fiba basketball world cup",
        "asian games",
        "summer olympics",
        "olympics",
        "fiba men's olympic basketball tournament",
        "fiba olympic qualifying tournament",
        "fiba olympic pre-qualifying tournament",
        "olympic qualifying tournament"
    ];

    public static EloRulesetParameters Apply(
        EloRulesetParameters ruleset,
        bool? gameIsNeutralSite,
        string? competitionHomeAdvantagePolicy,
        string? competitionName,
        string? competitionType,
        string? competitionPhase,
        string? competitionRound)
        => ruleset with
        {
            HomeAdvantageElo = IsNeutralSite(
                gameIsNeutralSite,
                competitionHomeAdvantagePolicy,
                competitionName,
                competitionType,
                competitionPhase,
                competitionRound)
                ? 0m
                : ruleset.HomeAdvantageElo
        };

    public static bool IsNeutralSite(
        bool? gameIsNeutralSite,
        string? competitionHomeAdvantagePolicy,
        string? competitionName,
        string? competitionType,
        string? competitionPhase,
        string? competitionRound)
    {
        if (gameIsNeutralSite.HasValue)
        {
            return gameIsNeutralSite.Value;
        }

        var policy = competitionHomeAdvantagePolicy?.Trim().ToLowerInvariant();
        if (policy == HomeAdvantagePolicies.Neutral)
        {
            return true;
        }

        if (policy == HomeAdvantagePolicies.Home)
        {
            return false;
        }

        var normalizedCompetition = Normalize(competitionName);
        var normalizedStage = Normalize($"{competitionPhase} {competitionRound}");

        // Final Four/Final Eight formats are hosted tournaments rather than
        // home fixtures. This also covers Euroleague, EuroChallenge, FIBA
        // Europe Cup and domestic cup editions when the provider exposes the
        // format in the phase or round metadata.
        if (ContainsAny(normalizedStage, NeutralStageMarkers))
        {
            return true;
        }

        // Final FIBA championships and centralized Olympic events are neutral
        // competitions. Qualifier names are deliberately not included here:
        // modern FIBA qualifier windows are home-and-away, while exceptional
        // historic neutral games can be corrected with the per-game override.
        if (NeutralCompetitionNames.Contains(normalizedCompetition, StringComparer.Ordinal))
        {
            return true;
        }

        // These competitions are commonly represented as a single hosted
        // tournament in the source catalog instead of a regular home/away
        // league. The competition-level policy remains available for any
        // edition whose format differs.
        if (ContainsAny(normalizedCompetition, NeutralCompetitionMarkers))
        {
            return true;
        }

        return false;
    }

    private static string Normalize(string? value) =>
        value?.Trim().ToLowerInvariant() ?? string.Empty;

    private static bool ContainsAny(string value, IEnumerable<string> markers) =>
        markers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));
}
