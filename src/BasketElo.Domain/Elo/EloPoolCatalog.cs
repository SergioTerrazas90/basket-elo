namespace BasketElo.Domain.Elo;

public static class EloPoolKeys
{
    public const string Nba = "nba";
    public const string EuropeClubs = "europe-clubs";
    public const string NationalTeams = "national-teams";

    public static readonly IReadOnlyList<string> All = [Nba, EuropeClubs, NationalTeams];
    public const string Default = Nba;

    public static bool IsSupported(string? value) =>
        !string.IsNullOrWhiteSpace(value) && All.Contains(value.Trim().ToLowerInvariant());

    public static string Normalize(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return IsSupported(normalized)
            ? normalized!
            : throw new ArgumentException($"Unsupported ELO pool '{value}'.", nameof(value));
    }

    public static string DisplayName(string poolKey) => Normalize(poolKey) switch
    {
        Nba => "NBA",
        EuropeClubs => "Europe Clubs",
        NationalTeams => "National Teams",
        _ => throw new ArgumentOutOfRangeException(nameof(poolKey))
    };
}

public sealed record EloPoolDescriptor(
    string Key,
    string DisplayName,
    int DisplayOrder);

public static class EloPoolCatalog
{
    public static readonly IReadOnlyList<EloPoolDescriptor> All =
    [
        new(EloPoolKeys.Nba, "NBA", 1),
        new(EloPoolKeys.EuropeClubs, "Europe Clubs", 2),
        new(EloPoolKeys.NationalTeams, "National Teams", 3)
    ];
}
