using BasketElo.Infrastructure.Backfill;
using Xunit;

namespace BasketElo.Infrastructure.Tests.Backfill;

public sealed class FlashscorePolandBasketLigaHistoricalDataProviderTests
{
    [Fact]
    public void ParsesFlashscoreBasketballFeedRecord()
    {
        const string feed = "SAÃ·3Â¬~AAÃ·evt123Â¬ADÃ·1055086200Â¬ABÃ·3Â¬AEÃ·Anwil WloclawekÂ¬AFÃ·Slask WroclawÂ¬AGÃ·89Â¬AHÃ·81Â¬PXÃ·anwil-idÂ¬PYÃ·slask-idÂ¬ERÃ·FinalÂ¬";

        var games = FlashscorePolandBasketLigaHistoricalDataProvider.ParseGames(
            feed,
            "2001-2002",
            "https://www.flashscore.com/basketball/poland/basket-liga-2001-2002/results/",
            DateTime.UtcNow,
            "revision");

        var game = Assert.Single(games);
        Assert.Equal("evt123", game.SourceGameId);
        Assert.Equal("Anwil Wloclawek", game.HomeTeamName);
        Assert.Equal("Slask Wroclaw", game.AwayTeamName);
        Assert.Equal((short)89, game.HomeScore);
        Assert.Equal((short)81, game.AwayScore);
        Assert.Equal("flashscore-team:anwil-id", game.SourceHomeTeamId);
        Assert.Equal("flashscore-team:slask-id", game.SourceAwayTeamId);
    }
}
