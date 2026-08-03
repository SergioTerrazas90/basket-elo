using BasketElo.Infrastructure.Backfill;
using Xunit;

namespace BasketElo.Infrastructure.Tests.Backfill;

public sealed class SerbianHistoricalBasketballDataProviderTests
{
    [Fact]
    public void ParsesRoundCountFromSerbianSportDropdown()
    {
        const string html = "<a onclick=\"SF.a(this, '#[$view->switchRound(1)]')\">1. kolo</a>" +
                            "<a onclick=\"SF.a(this, '#[$view->switchRound(22)]')\">22. kolo</a>";

        Assert.Equal(22, SerbianHistoricalBasketballDataProvider.ParseRoundCount(html));
    }

    [Fact]
    public void ParsesJsonLdGameAndNormalizesHistoricalTeamNames()
    {
        const string html = """
            <div class="game-row" data-id="12345">
              <script type="application/ld+json">
              {
                "@type": "SportsEvent",
                "url": "https://srbijasport.net/game/12345-partizan-buducnost",
                "homeTeam": { "name": "Partizan Mobtel" },
                "awayTeam": { "name": "Budućnost" },
                "description": "Partizan Mobtel – Budućnost 82:71",
                "startDate": "2003-05-31T00:00:00+02:00"
              }
              </script>
            </div>
            """;

        var warnings = new List<string>();
        var games = SerbianHistoricalBasketballDataProvider.ParseRoundPage(
            html,
            "2002-2003",
            2002,
            new SerbianHistoricalBasketballDataProvider.ArchiveStage("106-playoff", "Playoff", "Playoffs"),
            1,
            DateTime.UtcNow,
            "fixture",
            warnings);

        var game = Assert.Single(games);
        Assert.Equal("srbijasport-12345", game.SourceGameId);
        Assert.Equal("Partizan", game.HomeTeamName);
        Assert.Equal("Budućnost", game.AwayTeamName);
        Assert.Equal("RS", game.SourceHomeTeamCountryCode);
        Assert.Equal("ME", game.SourceAwayTeamCountryCode);
        Assert.Equal((short)82, game.HomeScore);
        Assert.Equal((short)71, game.AwayScore);
        Assert.Equal(new DateTime(2003, 5, 30, 22, 0, 0, DateTimeKind.Utc), game.GameDateTimeUtc);
        Assert.Empty(warnings);
    }
}
