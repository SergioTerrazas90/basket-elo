using BasketElo.Infrastructure.Backfill;
using Xunit;

namespace BasketElo.Infrastructure.Tests.Backfill;

public sealed class GermanCupWikipediaBasketballDataProviderTests
{
    [Fact]
    public void ParsesTwoLegged1975FinalWithStableOrderAndScores()
    {
        var games = GermanCupWikipediaBasketballDataProvider.ParseFinals("1975-1976").ToList();

        Assert.Equal(2, games.Count);
        Assert.Equal("Bayer Giants Leverkusen", games[0].HomeTeamName);
        Assert.Equal("MTV Wolfenbüttel", games[0].AwayTeamName);
        Assert.Equal((short)84, games[0].HomeScore);
        Assert.Equal((short)77, games[0].AwayScore);
        Assert.Equal("MTV Wolfenbüttel", games[1].HomeTeamName);
        Assert.Equal("Bayer Giants Leverkusen", games[1].AwayTeamName);
        Assert.Equal("Final", games[0].CompetitionRound);
        Assert.Equal("DE", games[0].SourceHomeTeamCountryCode);
        Assert.Equal(GermanCupWikipediaBasketballDataProvider.ParserVersion, games[0].Provenance!.ParserVersion);
    }

    [Fact]
    public void Parses2007SingleFinal()
    {
        var game = Assert.Single(GermanCupWikipediaBasketballDataProvider.ParseFinals("2007-2008"));

        Assert.Equal("Artland Dragons", game.HomeTeamName);
        Assert.Equal("MHP RIESEN Ludwigsburg", game.AwayTeamName);
        Assert.Equal((short)74, game.HomeScore);
        Assert.Equal((short)60, game.AwayScore);
        Assert.Equal(new DateTime(2008, 4, 15, 0, 0, 0, DateTimeKind.Utc), game.GameDateTimeUtc);
    }

    [Fact]
    public void UnknownSeasonReturnsNoFinal()
    {
        Assert.Empty(GermanCupWikipediaBasketballDataProvider.ParseFinals("1974-1975"));
    }
}
