namespace BasketElo.Domain.Competitions;

public enum CompetitionTypeKind
{
    League,
    DomesticFirstDivision,
    DomesticCup,
    International,
    InternationalCup,
    Qualifier,
    Cup,
    CurrentResults
}

public sealed record CompetitionTypeDescriptor(
    CompetitionTypeKind Kind,
    string Key,
    string DisplayName);

public static class CompetitionTypeCatalog
{
    public const string League = "league";
    public const string DomesticFirstDivision = "domestic_first_division";
    public const string DomesticCup = "domestic_cup";
    public const string International = "international";
    public const string InternationalCup = "international_cup";
    public const string Qualifier = "qualifier";
    public const string Cup = "cup";
    public const string CurrentResults = "current-results";

    public static readonly IReadOnlyList<CompetitionTypeDescriptor> All =
    [
        new(CompetitionTypeKind.League, League, "League"),
        new(CompetitionTypeKind.DomesticFirstDivision, DomesticFirstDivision, "Domestic first division"),
        new(CompetitionTypeKind.DomesticCup, DomesticCup, "Domestic cup / super cup"),
        new(CompetitionTypeKind.International, International, "International competition"),
        new(CompetitionTypeKind.InternationalCup, InternationalCup, "International cup / super cup"),
        new(CompetitionTypeKind.Qualifier, Qualifier, "Qualifier"),
        new(CompetitionTypeKind.Cup, Cup, "Cup (generic)"),
        new(CompetitionTypeKind.CurrentResults, CurrentResults, "Current-results feed (legacy)")
    ];

    public static bool IsValid(string? value) =>
        value is not null && All.Any(x => string.Equals(x.Key, value.Trim(), StringComparison.OrdinalIgnoreCase));

    public static string Normalize(string? value)
    {
        var match = All.FirstOrDefault(x => string.Equals(x.Key, value?.Trim(), StringComparison.OrdinalIgnoreCase));
        return match?.Key ?? throw new ArgumentException($"Unknown competition type '{value}'.");
    }
}
