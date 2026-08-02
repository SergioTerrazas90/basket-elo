using BasketElo.Domain.Backfill;

namespace BasketElo.Infrastructure.Backfill;

/// <summary>
/// Imports the published historical German Cup final records from the BBL-Pokal
/// overview. The historical overview exposes the final (or final series), not
/// complete early-round brackets, so this provider deliberately imports only
/// those published final games.
/// </summary>
public sealed class GermanCupWikipediaBasketballDataProvider : IBasketballDataProvider
{
    public const string Source = "wikipedia-german-cup";
    public const string ParserVersion = "wikipedia-german-cup-finals-v1";
    public const string SourceUrl = "https://en.wikipedia.org/wiki/BBL-Pokal";

    private static readonly IReadOnlyCollection<FinalRecord> Finals =
    [
        TwoLeg("1975-1976", "Bayer Giants Leverkusen", "MTV Wolfenbüttel", 84, 77, 66, 62),
        TwoLeg("1976-1977", "Heidelberg", "Bayer Giants Leverkusen", 88, 70, 87, 72),
        TwoLeg("1977-1978", "Heidelberg", "SV Hagen", 78, 69, 90, 82),
        TwoLeg("1978-1979", "GIESSEN 46ers", "ASC 46 Göttingen", 77, 72, 77, 75),
        TwoLeg("1979-1980", "BSC Saturn Köln", "GIESSEN 46ers", 78, 62, 70, 68),
        TwoLeg("1980-1981", "BSC Saturn Köln", "SV Hagen", 82, 77, 82, 85),
        TwoLeg("1981-1982", "MTV Wolfenbüttel", "BSC Saturn Köln", 97, 79, 93, 76),
        TwoLeg("1982-1983", "BSC Saturn Köln", "SV Hagen", 78, 66, 85, 92),
        TwoLeg("1983-1984", "ASC 46 Göttingen", "BSC Saturn Köln", 75, 76, 65, 83),
        Single("1984-1985", "ASC 46 Göttingen", "BBC Bayreuth", 85, 72),
        Single("1985-1986", "Bayer Giants Leverkusen", "BBC Bayreuth", 80, 68),
        Single("1986-1987", "Bayer Giants Leverkusen", "GIESSEN 46ers", 92, 71),
        Single("1987-1988", "BBC Bayreuth", "BSC Saturn Köln", 105, 88),
        Single("1988-1989", "BBC Bayreuth", "Bayer Giants Leverkusen", 89, 67),
        TwoLeg("1989-1990", "Bayer Giants Leverkusen", "Bamberg", 84, 83, 78, 99),
        TwoLeg("1990-1991", "Bayer Giants Leverkusen", "Basketball Braunschweig", 98, 71, 80, 126),
        TwoLeg("1991-1992", "Bamberg", "MHP RIESEN Ludwigsburg", 69, 72, 68, 74),
        Single("1992-1993", "Bayer Giants Leverkusen", "Brandt Hagen", 81, 60),
        Single("1993-1994", "Brandt Hagen", "Ulm", 86, 72),
        Single("1994-1995", "Bayer Giants Leverkusen", "Ulm", 77, 76),
        Single("1995-1996", "Ulm", "Bayer Giants Leverkusen", 80, 79),
        Single("1996-1997", "Alba Berlin", "GIESSEN 46ers", 82, 73),
        Single("1997-1998", "Trier", "Rhondorf", 97, 88),
        Single("1998-1999", "Alba Berlin", "GIESSEN 46ers", 69, 48),
        Single("1999-2000", "SKYLINERS", "Alba Berlin", 76, 68),
        Single("2000-2001", "Trier", "Brandt Hagen", 96, 83),
        Single("2001-2002", "Alba Berlin", "EWE Baskets Oldenburg", 105, 55),
        Single("2002-2003", "Alba Berlin", "Koln", 82, 80),
        Single("2003-2004", "Koln", "SKYLINERS", 80, 71),
        Single("2004-2005", "Koln", "Artland Dragons", 85, 75),
        Single("2005-2006", "Alba Berlin", "Bamberg", 85, 73),
        Single("2006-2007", "Koln", "Artland Dragons", 60, 58),
        Single("2007-2008", "Artland Dragons", "MHP RIESEN Ludwigsburg", 74, 60)
    ];

    public string SourceKey => Source;

    public Task<BasketballProviderLeague?> ResolveLeagueAsync(
        string country,
        string leagueName,
        BackfillExecutionContext context,
        CancellationToken cancellationToken)
    {
        var league = string.Equals(country, "Germany", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(leagueName, "German Cup", StringComparison.OrdinalIgnoreCase)
                ? new BasketballProviderLeague(Source, "BBL-POKAL", "German Cup", "DE", "start_year")
                : null;
        return Task.FromResult(league);
    }

    public Task<(IReadOnlyCollection<BasketballProviderGame> Games, bool HasMorePages, IReadOnlyCollection<string> Warnings)> GetGamesAsync(
        BasketballProviderLeague league,
        string season,
        BackfillExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(league.Source, Source, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(league.SourceLeagueId, "BBL-POKAL", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("German Wikipedia provider only supports Germany: German Cup.");
        }

        var final = Finals.FirstOrDefault(x => string.Equals(x.Season, season, StringComparison.OrdinalIgnoreCase));
        if (final is null)
        {
            return Task.FromResult<(IReadOnlyCollection<BasketballProviderGame>, bool, IReadOnlyCollection<string>)>(
                ([], false, [$"No published German Cup final record was configured for {season}."]));
        }

        var games = final.Games
            .Select((game, index) => new BasketballProviderGame(
                Source,
                $"{final.Season}-final-{index + 1}",
                DateFor(final.Season, index, final.Games.Count),
                "finished",
                SourceTeamId(game.Home),
                game.Home,
                SourceTeamId(game.Away),
                game.Away,
                game.HomeScore,
                game.AwayScore,
                new BasketballProviderGameProvenance(
                    SourceUrl,
                    final.Season,
                    DateTime.UtcNow,
                    ParserVersion,
                    "bbl-pokal-overview"),
                CompetitionPhase: "Final phase",
                CompetitionRound: "Final",
                SourceHomeTeamCountryCode: "DE",
                SourceAwayTeamCountryCode: "DE"))
            .ToArray();

        return Task.FromResult<(IReadOnlyCollection<BasketballProviderGame>, bool, IReadOnlyCollection<string>)>(
            (games, false,
            [
                "Historical BBL-Pokal source publishes final/final-series records only; earlier rounds are not exposed by this source.",
                "Historical final dates are deterministic postseason placement dates where exact dates are not published in the catalog; tip-off times are 00:00 UTC."
            ]));
    }

    internal static IReadOnlyCollection<BasketballProviderGame> ParseFinals(string season) =>
        Finals.FirstOrDefault(x => string.Equals(x.Season, season, StringComparison.OrdinalIgnoreCase)) is { } final
            ? final.Games.Select((game, index) => new BasketballProviderGame(
                Source,
                $"{final.Season}-final-{index + 1}",
                DateFor(final.Season, index, final.Games.Count),
                "finished",
                SourceTeamId(game.Home), game.Home,
                SourceTeamId(game.Away), game.Away,
                game.HomeScore, game.AwayScore,
                new BasketballProviderGameProvenance(SourceUrl, final.Season, null, ParserVersion, "bbl-pokal-overview"),
                CompetitionPhase: "Final phase",
                CompetitionRound: "Final",
                SourceHomeTeamCountryCode: "DE",
                SourceAwayTeamCountryCode: "DE")).ToArray()
            : [];

    private static DateTime DateFor(string season, int index, int count)
    {
        var endYear = int.Parse(season[..4]) + 1;
        var month = count == 1 ? 4 : 4;
        return new DateTime(endYear, month, 15 + index, 0, 0, 0, DateTimeKind.Utc);
    }

    private static string SourceTeamId(string name) =>
        $"club:{new string(name.ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray())}";

    private static FinalRecord Single(string season, string home, string away, short homeScore, short awayScore) =>
        new(season, [new GameRecord(home, away, homeScore, awayScore)]);

    private static FinalRecord TwoLeg(
        string season,
        string firstHome,
        string firstAway,
        short firstHomeScore,
        short firstAwayScore,
        short secondHomeScore,
        short secondAwayScore) =>
        new(season,
        [
            new GameRecord(firstHome, firstAway, firstHomeScore, firstAwayScore),
            new GameRecord(firstAway, firstHome, secondHomeScore, secondAwayScore)
        ]);

    private sealed record FinalRecord(string Season, IReadOnlyCollection<GameRecord> Games);

    private sealed record GameRecord(string Home, string Away, short HomeScore, short AwayScore);
}
