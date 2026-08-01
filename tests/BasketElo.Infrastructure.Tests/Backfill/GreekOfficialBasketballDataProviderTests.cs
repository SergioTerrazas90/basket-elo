using BasketElo.Infrastructure.Backfill;
using Xunit;

namespace BasketElo.Infrastructure.Tests.Backfill;

public sealed class GreekOfficialBasketballDataProviderTests
{
    [Fact]
    public void CanonicalizesSportGrLarisaToExistingGsLarissaIdentity()
    {
        Assert.Equal("GS Larissa", GreekOfficialBasketballDataProvider.CanonicalizeTeamName("Λάρισα"));
    }

    [Fact]
    public void BitzenisTableParsesBothLegsWithRoundDates()
    {
        const string html = """
            <TABLE><TR><TH colspan=2>Game 1 & 14<TH>14-09-96<TH>04-01-97
            <TR><TD>Panathinaikos<TD>Apollon Patras<TD>89-80<TD>64-75
            <TR><TD>Peristeri<TD>Papagou<TD>80-70<TD>99-89
            <TR><TD>Iraklis<TD>Olympiakos<TD>72-76<TD>51-69
            <TR><TD>PAOK<TD>Aris<TD>62-73<TD>80-76
            <TR><TD>Peiraikos<TD>BAO<TD>93-71<TD>69-70
            <TR><TD>Larissa<TD>Sporting<TD>62-66<TD>54-73
            <TR><TD>Panionios<TD>AEK<TD>48-60<TD>75-84</TABLE>
            """;

        var games = GreekOfficialBasketballDataProvider.ParseBitzenisRegularSeason(
            html, "1996-1997", "https://bitzenis.gr/retro/bask.htm");

        Assert.Equal(14, games.Count);
        var first = Assert.Single(games, game => game.SourceGameId == "bitzenis:1996:01:1");
        Assert.Equal(new DateTime(1996, 9, 14, 12, 0, 0, DateTimeKind.Utc), first.GameDateTimeUtc);
        Assert.Equal("Panathinaikos", first.HomeTeamName);
        Assert.Equal("Apollon Patras", first.AwayTeamName);
        var returnLeg = Assert.Single(games, game => game.SourceGameId == "bitzenis:1996:14:1");
        Assert.Equal("Apollon Patras", returnLeg.HomeTeamName);
        Assert.Equal((short)64, returnLeg.HomeScore);
    }

    [Fact]
    public void SportGrPairedRoundUsesPerGameHrefDatesAndReversesReturnLeg()
    {
        const string html = """
            <table>
              <tr><td>27/09</td><td>1η Αγωνιστική 14η</td><td>03/01</td></tr>
              <tr><td><a href="../970927/peristerihrakleio.htm">78-59</a></td><td>Περιστέρι - Ηράκλειο</td><td><a href="../980103/hrakleioperisteri.htm">54-65</a></td></tr>
            </table>
            """;

        var games = GreekOfficialBasketballDataProvider.ParseSportGrRegularSeason(
            html, "1997-1998", 1997, "https://example.test/a1/1-14.htm");

        Assert.Equal(2, games.Count);
        Assert.Contains(games, game => game.HomeTeamName == "Peristeri" && game.AwayTeamName == "Irakleio" &&
            game.GameDateTimeUtc == new DateTime(1997, 9, 27, 12, 0, 0, DateTimeKind.Utc));
        Assert.Contains(games, game => game.HomeTeamName == "Irakleio" && game.AwayTeamName == "Peristeri" &&
            game.GameDateTimeUtc == new DateTime(1998, 1, 3, 12, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void SportGrPairedRoundKeepsScoredReturnLegWhenFirstLegWasInterrupted()
    {
        const string html = """
            <table>
              <tr><td>17/10</td><td>5η Αγωνιστική 18η</td><td>30/01</td></tr>
              <tr><td>Διεκόπη</td><td>Πανιώνιος - ΑΕΚ</td><td><a href="../990130/aekpan.htm">78-70</a></td></tr>
            </table>
            """;

        var games = GreekOfficialBasketballDataProvider.ParseSportGrRegularSeason(
            html, "1998-1999", 1998, "https://example.test/a1/5-18.htm");

        var game = Assert.Single(games);
        Assert.Equal("Round 18", game.CompetitionRound);
        Assert.Equal("AEK Athens", game.HomeTeamName);
        Assert.Equal("Panionios", game.AwayTeamName);
        Assert.Equal(new DateTime(1999, 1, 30, 12, 0, 0, DateTimeKind.Utc), game.GameDateTimeUtc);
    }

    [Fact]
    public void BasketballReferenceGreekScheduleParsesRegularSeasonAndPlayoffs()
    {
        const string html = """
            <table id="regular-season-games"><tbody>
              <tr><th data-stat="date_game">Sat, Oct 10, 2015</th><td data-stat="visitor_team_name">PAOK</td><td data-stat="visitor_pts">72</td><td data-stat="home_team_name">Kolossos H Hotels</td><td data-stat="home_pts">79</td></tr>
            </tbody></table>
            <table id="playoffs-games"><tbody>
              <tr><th data-stat="date_game">Sun, May 1, 2016</th><td data-stat="visitor_team_name">Olympiacos</td><td data-stat="visitor_pts">84</td><td data-stat="home_team_name">Panathinaikos</td><td data-stat="home_pts">88</td></tr>
            </tbody></table>
            """;

        var games = GreekOfficialBasketballDataProvider.ParseBasketballReferenceGreekLeague(
            html, "2015-2016", "https://www.basketball-reference.com/international/greek-basket-league/2016-schedule.html");

        Assert.Equal(2, games.Count);
        var regular = Assert.Single(games, game => game.CompetitionPhase == "Regular Season");
        Assert.Equal("Kolossos Rhodes", regular.HomeTeamName);
        Assert.Equal("PAOK", regular.AwayTeamName);
        Assert.Equal((short)79, regular.HomeScore);
        Assert.Equal((short)72, regular.AwayScore);
        Assert.Equal(new DateTime(2015, 10, 10, 12, 0, 0, DateTimeKind.Utc), regular.GameDateTimeUtc);
        var playoff = Assert.Single(games, game => game.CompetitionPhase == "Playoffs");
        Assert.Equal("Panathinaikos", playoff.HomeTeamName);
        Assert.Equal("Olympiacos", playoff.AwayTeamName);
    }

    [Fact]
    public void SportGrPlayoffParsesDatedRowsAndSkipsStarredAdministrativeResult()
    {
        const string datedHtml = """
            <table><tr><th>ΗΜΙΤΕΛΙΚΟΙ</th></tr>
              <tr><td>2-5-98</td><td>18:30</td><td>Ολυμπιακός - ΠΑΟΚ</td><td><a href="../osfppaok/1game.htm">66-65</a></td></tr>
            </table>
            """;
        const string seriesHtml = """
            <table><tr><th>ΤΕΛΙΚΟΙ</th></tr>
              <tr><td>ΠΑΟΚ - Αρης</td><td><a href="../990505/pakari.htm">79-73</a></td><td>* <a href="../990508/aripak.htm">64-75</a></td><td><a href="../990512/pakari.htm">78-69</a></td></tr>
            </table>
            """;

        var dated = GreekOfficialBasketballDataProvider.ParseSportGrPlayoffs(
            datedHtml, "1997-1998", 1997, "https://example.test/playoffs/index.htm");
        var series = GreekOfficialBasketballDataProvider.ParseSportGrPlayoffs(
            seriesHtml, "1998-1999", 1998, "https://example.test/playoffs/index.htm");

        Assert.Equal(new DateTime(1998, 5, 2, 12, 0, 0, DateTimeKind.Utc), Assert.Single(dated).GameDateTimeUtc);
        Assert.Equal(2, series.Count);
        Assert.DoesNotContain(series, game => game.GameDateTimeUtc.Day == 8);
    }

    [Fact]
    public void EsakeRoundParsesStableGameIdGreekDateAndTeams()
    {
        const string html = """
            <div class="esake-program-game">
              <a href="/el/action/EsakegameView?idgame=ABC123"></a>
              <div class="esake-program-game-info">ΚΥΡΙΑΚΗ 14 ΟΚΤΩΒΡΙΟΥ 2007</div>
              <div class="esake-program-game-final-score">
                <div><img src="/esaketeam/00000001/logo.png" />ΠΑΝΑΘΗΝΑΪΚΟΣ ΑΟ</div>
                <div>82 - 74</div>
                <div><img src="/esaketeam/00000002/logo.png" />ΑΕΚ</div>
              </div>
            </div>
            """;

        var game = Assert.Single(GreekOfficialBasketballDataProvider.ParseEsakeRound(
            html, "2007-2008", 2007, "Regular Season", "01", "https://www.esake.gr/el/action/EsakeResults"));

        Assert.Equal("esake:ABC123", game.SourceGameId);
        Assert.Equal(new DateTime(2007, 10, 14, 12, 0, 0, DateTimeKind.Utc), game.GameDateTimeUtc);
        Assert.Equal("Panathinaikos", game.HomeTeamName);
        Assert.Equal("AEK Athens", game.AwayTeamName);
        Assert.Equal((short)82, game.HomeScore);
        Assert.Equal("Round 1", game.CompetitionRound);
    }

    [Fact]
    public void WikipediaMatrixParsesEveryHomeAwayCell()
    {
        const string html = """
            <table><tbody>
              <tr><th>Home / Away</th><th>PAOK</th><th>Olympiacos</th><th>AEK</th></tr>
              <tr><th>PAOK</th><td></td><td>74–68</td><td>90-60</td></tr>
              <tr><th>Olympiacos</th><td>71-85</td><td></td><td>85-71</td></tr>
              <tr><th>AEK</th><td>67-88</td><td>76-78</td><td></td></tr>
            </tbody></table>
            """;

        var games = GreekOfficialBasketballDataProvider.ParseWikipediaRegularSeasonMatrix(html);

        Assert.Equal(6, games.Count);
        Assert.Contains(games, game => game.Home == "Olympiacos" && game.Away == "AEK Athens" &&
                                      game.HomeScore == 85 && game.AwayScore == 71);
    }

    [Fact]
    public void OlympiacosScheduleIgnoresOutOfSeasonPlaceholderDates()
    {
        const string html = """
            <table><tbody>
              <tr><td>Olympiacos</td><td>AEK</td><td>21/02/1993 19:00</td><td>85-71</td><td>Regular Season</td><td>Round 23</td></tr>
              <tr><td>Olympiacos</td><td>Aris</td><td>01/08/2026 23:54</td><td>59-72</td><td>Regular Season</td><td>Round 25</td></tr>
              <tr><td>Olympiacos</td><td>Aris</td><td>07/03/1993 19:00</td><td>89-72</td><td>Regular Season</td><td>Round 25</td></tr>
            </tbody></table>
            """;

        var dates = GreekOfficialBasketballDataProvider.ParseOlympiacosRegularSeasonRoundDates(html, 1992);

        Assert.Equal(new DateTime(1993, 2, 21), dates[23]);
        Assert.Equal(new DateTime(1993, 3, 7), dates[25]);
    }

    [Fact]
    public void EokLegacyPageSkipsAdministrativeTwentyZeroResult()
    {
        const string html = """
            <p>Α ΦΑΣΗ<br>12/09/1999<br>1&nbsp;&nbsp;ΑΕΚ&nbsp;&nbsp;ΑΡΗΣ&nbsp;&nbsp;80-70<br>2&nbsp;&nbsp;ΠΑΟΚ&nbsp;&nbsp;ΔΑΦΝΗ&nbsp;&nbsp;20-0</p>
            """;

        var result = GreekOfficialBasketballDataProvider.ParseEokCup(
            html, "1999-2000", 1753, "https://www.basket.gr/cup-men/example", "fixture");

        var game = Assert.Single(result.Games);
        Assert.Equal("AEK Athens", game.HomeTeamName);
        Assert.Equal("Aris", game.AwayTeamName);
        Assert.Equal(new DateTime(1999, 9, 12, 12, 0, 0, DateTimeKind.Utc), game.GameDateTimeUtc);
        Assert.Contains(result.Warnings, warning => warning.Contains("20-0", StringComparison.Ordinal));
    }

    [Fact]
    public void EokTablePageParsesDateTeamsAndScore()
    {
        const string html = """
            <h3>ΗΜΙΤΕΛΙΚΟΙ</h3>
            <table><tbody><tr><td>23/03/2008</td><td>44</td><td>ΟΛΥΜΠΙΑΚΟΣ</td><td>ΠΑΝΑΘΗΝΑΪΚΟΣ</td><td>81-79</td></tr></tbody></table>
            """;

        var result = GreekOfficialBasketballDataProvider.ParseEokCup(
            html, "2007-2008", 1765, "https://www.basket.gr/cup-men/example", "fixture");

        var game = Assert.Single(result.Games);
        Assert.Equal("Olympiacos", game.HomeTeamName);
        Assert.Equal("Panathinaikos", game.AwayTeamName);
        Assert.Equal("Semifinals", game.CompetitionRound);
        Assert.Equal("eok-cup:1765:44", game.SourceGameId);
    }

    [Fact]
    public void EokJanuaryHeadingWithPreviousYearTypoStaysInsideEditionSeason()
    {
        const string html = """
            <p>ΠΡΟΗΜΙΤΕΛΙΚΟΙ 19/1/1999<br>34&nbsp;&nbsp;ΗΡΑΚΛΗΣ&nbsp;&nbsp;ΑΡΗΣ&nbsp;&nbsp;65-55</p>
            """;

        var result = GreekOfficialBasketballDataProvider.ParseEokCup(
            html, "1999-2000", 1753, "https://www.basket.gr/cup-men/example", "fixture");

        Assert.Equal(new DateTime(2000, 1, 19, 12, 0, 0, DateTimeKind.Utc), Assert.Single(result.Games).GameDateTimeUtc);
    }

    [Fact]
    public void EokAutumnHeadingWithNextYearTypoStaysInsideEditionSeason()
    {
        const string html = """
            <p>1η αγωνιστική 5-6/10/03<br>45&nbsp;&nbsp;ΑΕΚ&nbsp;&nbsp;ΜΑΚΕΔΟΝΙΚΟΣ&nbsp;&nbsp;70-78</p>
            """;

        var result = GreekOfficialBasketballDataProvider.ParseEokCup(
            html, "2002-2003", 1756, "https://www.basket.gr/cup-men/example", "fixture");

        Assert.Equal(new DateTime(2002, 10, 5, 12, 0, 0, DateTimeKind.Utc), Assert.Single(result.Games).GameDateTimeUtc);
    }

    [Fact]
    public void EokSplitMonthRangeAndKnown1997FinalTyposAreNormalized()
    {
        const string html = """
            <p>First Phase Round 1 31-8/1-9/1996<br>01&nbsp;&nbsp;Esperos&nbsp;&nbsp;MENT&nbsp;&nbsp;76-59</p>
            <p>Final Four 12/4/1997<br>41&nbsp;&nbsp;AEK&nbsp;&nbsp;Panathinaikos&nbsp;&nbsp;72-63<br>42&nbsp;&nbsp;Olympiacos&nbsp;&nbsp;Apollon Patras&nbsp;&nbsp;80-78</p>
            """;

        var result = GreekOfficialBasketballDataProvider.ParseEokCup(
            html, "1996-1997", 1750, "https://www.basket.gr/cup-men/example", "fixture");

        Assert.Equal(new DateTime(1996, 8, 31, 12, 0, 0, DateTimeKind.Utc), result.Games[0].GameDateTimeUtc);
        Assert.Equal(new DateTime(1997, 4, 13, 12, 0, 0, DateTimeKind.Utc), result.Games[1].GameDateTimeUtc);
        Assert.Equal("Third Place", result.Games[1].CompetitionRound);
        Assert.Equal("Final", result.Games[2].CompetitionRound);
    }
}
