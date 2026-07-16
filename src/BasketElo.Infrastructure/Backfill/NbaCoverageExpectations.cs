namespace BasketElo.Infrastructure.Backfill;

public static class NbaCoverageExpectations
{
    public static NbaCoverageExpectation? ForSeason(string season)
    {
        var startYear = SeasonLabelNormalizer.ParseStartYear(season);
        return startYear switch
        {
            1946 => new(340, "inaugural BAA season"),
            1947 => new(200, "eight-team BAA season"),
            1948 => new(370, "expanded BAA season"),
            1949 => new(540, "first NBA season"),
            >= 1950 and <= 1960 => new(280, "early eight-to-eleven-team NBA era"),
            >= 1961 and <= 1965 => new(340, "early 80-game NBA era"),
            >= 1966 and <= 1975 => new(420, "NBA expansion era"),
            >= 1976 and <= 1979 => new(850, "post-merger NBA era"),
            >= 1980 and <= 1987 => new(900, "23-team NBA era"),
            >= 1988 and <= 1997 => new(1_000, "late-1980s and 1990s expansion era"),
            1998 => new(760, "1998-1999 lockout season", IsExceptionalSchedule: true),
            >= 1999 and <= 2003 => new(1_180, "29-team NBA era"),
            >= 2004 and <= 2010 => new(1_250, "30-team 82-game era"),
            2011 => new(1_020, "2011-2012 lockout season", IsExceptionalSchedule: true),
            >= 2012 and <= 2018 => new(1_250, "30-team 82-game era"),
            2019 => new(1_080, "2019-2020 interrupted season", IsExceptionalSchedule: true),
            2020 => new(1_100, "2020-2021 shortened season", IsExceptionalSchedule: true),
            >= 2021 => new(1_250, "modern 30-team NBA era"),
            _ => null
        };
    }

    public static bool IsNba(string provider, string country, string leagueName) =>
        string.Equals(provider, BasketballReferenceBasketballDataProvider.Source, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(country, "United States", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(leagueName, "NBA", StringComparison.OrdinalIgnoreCase);
}

public sealed record NbaCoverageExpectation(
    int MinimumCompleteGames,
    string EraDescription,
    bool IsExceptionalSchedule = false);
