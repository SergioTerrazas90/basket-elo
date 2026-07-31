using BasketElo.Infrastructure.Backfill;
using Xunit;

namespace BasketElo.Infrastructure.Tests.Backfill;

public sealed class AcbOfficialTournamentBasketballDataProviderTests
{
    [Fact]
    public void ParsesOfficialMatchCard()
    {
        const string html = """
            <div class="MatchCard-module__matchCard">
              <div class="dateText">JUEVES 7 FEBRERO</div>
              <a href="/es/copa-del-rey/equipos/iurbentia-bilbao-basket-4"><img alt="iurbentia Bilbao Basket logo" /></a>
              <a href="/es/copa-del-rey/equipos/axa-fc-barcelona-2"><img alt="Axa FC Barcelona logo" /></a>
              <p class="scoreNumber">70</p><p class="scoreNumber">69</p>
              <a href="https://live.acb.com/partidos/iurbentia-bilbao-basket-vs-axa-fc-barcelona-835/resumen">Resumen</a>
            </div>
            """;

        var document = new HtmlAgilityPack.HtmlDocument();
        document.LoadHtml(html);
        var card = document.DocumentNode.SelectSingleNode("//div[contains(@class,'matchCard')]");
        Assert.NotNull(card);
        Assert.True(AcbOfficialTournamentBasketballDataProvider.TryParseMatch(
            card!, "2007-2008", 2007, 2008, Enum.Parse<AcbOfficialTournamentBasketballDataProvider.TournamentKind>("CopaDelRey"), "https://acb.com", out var game));
        Assert.NotNull(game);
        Assert.Equal((short)70, game!.HomeScore);
        Assert.Equal("835", game.SourceGameId);
    }
}
