using BasketElo.Infrastructure.Backfill;
using Xunit;

namespace BasketElo.Infrastructure.Tests.Backfill;

public sealed class FlashscoreDomesticHistoricalDataProviderTests
{
    [Fact]
    public void ParsesFlashscoreDomesticFeedRecord()
    {
        const string feed = "SAÃ·3Â¬~AAÃ·evt123Â¬ADÃ·1055086200Â¬ABÃ·3Â¬AEÃ·CSKA MoscowÂ¬AFÃ·Khimki M.Â¬AGÃ·89Â¬AHÃ·81Â¬PXÃ·cska-idÂ¬PYÃ·khimki-idÂ¬ERÃ·FinalÂ¬";

        var games = FlashscoreDomesticHistoricalDataProvider.ParseGames(
            feed,
            "2007-2008",
            "https://www.flashscore.com.gh/basketball/russia/pbl-2007-2008/results/",
            "PBL",
            DateTime.UtcNow,
            "revision");

        var game = Assert.Single(games);
        Assert.Equal("evt123", game.SourceGameId);
        Assert.Equal("CSKA Moscow", game.HomeTeamName);
        Assert.Equal("Khimki M.", game.AwayTeamName);
        Assert.Equal((short)89, game.HomeScore);
        Assert.Equal((short)81, game.AwayScore);
        Assert.Equal("flashscore-team:cska-id", game.SourceHomeTeamId);
        Assert.Equal("flashscore-team:khimki-id", game.SourceAwayTeamId);
        Assert.Equal("PBL", game.CompetitionPhase);
    }
}
