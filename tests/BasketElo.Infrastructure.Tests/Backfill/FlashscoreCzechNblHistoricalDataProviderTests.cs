using BasketElo.Infrastructure.Backfill;
using Xunit;

namespace BasketElo.Infrastructure.Tests.Backfill;

public sealed class FlashscoreCzechNblHistoricalDataProviderTests
{
    [Fact]
    public void ParsesFlashscoreBasketballFeedRecord()
    {
        const string feed = "SA÷3¬~AA÷evt123¬AD÷1055086200¬AB÷3¬AE÷Opava¬AF÷Nymburk¬AG÷89¬AH÷81¬PX÷opava-id¬PY÷nymburk-id¬ER÷Final¬";

        var games = FlashscoreCzechNblHistoricalDataProvider.ParseGames(
            feed,
            "2002-2003",
            "https://www.flashscore.com/basketball/czech-republic/nbl-2002-2003/results/",
            DateTime.UtcNow,
            "revision");

        var game = Assert.Single(games);
        Assert.Equal("evt123", game.SourceGameId);
        Assert.Equal("Opava", game.HomeTeamName);
        Assert.Equal("Nymburk", game.AwayTeamName);
        Assert.Equal((short)89, game.HomeScore);
        Assert.Equal((short)81, game.AwayScore);
        Assert.Equal("flashscore-team:opava-id", game.SourceHomeTeamId);
        Assert.Equal("flashscore-team:nymburk-id", game.SourceAwayTeamId);
    }
}
