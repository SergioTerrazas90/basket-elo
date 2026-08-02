using System.Net;
using BasketElo.Domain.Backfill;
using BasketElo.Infrastructure.Backfill;
using Microsoft.Extensions.Options;
using Xunit;

namespace BasketElo.Infrastructure.Tests.Backfill;

public sealed class GermanBasketballDataProviderTests
{
    [Fact]
    public void ParsesOfficialLeagueGamesAndPreservesCompetitionPhases()
    {
        const string payload = """
            {
              "totalPages": 1,
              "items": [
                {
                  "id": "1",
                  "sourceId": "source-1",
                  "status": "OFFICIAL",
                  "stage": "MAIN_ROUND",
                  "scheduledTime": "1966-10-01T15:00:00Z",
                  "homeTeam": { "teamId": "101", "name": "USC Heidelberg" },
                  "guestTeam": { "teamId": "102", "name": "MTV Giessen" },
                  "result": { "homeTeamFinalScore": 73, "guestTeamFinalScore": 68 }
                },
                {
                  "id": "2",
                  "sourceId": "source-2",
                  "status": "OFFICIAL",
                  "stage": "FINALS",
                  "scheduledTime": "1967-04-15T15:00:00Z",
                  "homeTeam": { "teamId": "102", "name": "MTV Giessen" },
                  "guestTeam": { "teamId": "101", "name": "USC Heidelberg" },
                  "result": { "homeTeamFinalScore": 71, "guestTeamFinalScore": 69 }
                },
                {
                  "id": "3",
                  "status": "SCHEDULED",
                  "stage": "MAIN_ROUND",
                  "scheduledTime": "1967-04-20T15:00:00Z",
                  "homeTeam": { "teamId": "101", "name": "USC Heidelberg" },
                  "guestTeam": { "teamId": "102", "name": "MTV Giessen" },
                  "result": { "homeTeamFinalScore": 0, "guestTeamFinalScore": 0 }
                }
              ]
            }
            """;

        var games = GermanBasketballDataProvider.ParseGames(
            payload,
            "1966-1967",
            "https://api.basketball-bundesliga.de/games?seasonId=1966",
            DateTime.UtcNow);

        Assert.Equal(2, games.Count);
        var regularSeason = games.Single(game => game.SourceGameId == "source-1");
        Assert.Equal("finished", regularSeason.Status);
        Assert.Equal("Regular Season", regularSeason.CompetitionPhase);
        Assert.Equal("Regular Season", regularSeason.CompetitionRound);
        Assert.Equal((short)73, regularSeason.HomeScore);
        Assert.Equal("DE", regularSeason.SourceHomeTeamCountryCode);

        var final = games.Single(game => game.SourceGameId == "source-2");
        Assert.Equal("Playoffs", final.CompetitionPhase);
        Assert.Equal("Finals", final.CompetitionRound);
        Assert.Equal("easycredit-bbl-api-v1", final.Provenance!.ParserVersion);
    }

    [Fact]
    public void ParsesNumericSourceAndTeamIdentifiersFromEarlyArchiveRecords()
    {
        const string payload = """
            {
              "totalPages": 1,
              "items": [
                {
                  "id": 7,
                  "sourceId": 8,
                  "status": "OFFICIAL",
                  "stage": "MAIN_ROUND",
                  "scheduledTime": "1966-10-01T15:00:00Z",
                  "homeTeam": { "teamId": 101, "name": "Home" },
                  "guestTeam": { "teamId": 102, "name": "Away" },
                  "result": { "homeTeamFinalScore": 73, "guestTeamFinalScore": 68 }
                }
              ]
            }
            """;

        var game = Assert.Single(GermanBasketballDataProvider.ParseGames(
            payload,
            "1966-1967",
            "https://example.test/bbl",
            DateTime.UtcNow));

        Assert.Equal("8", game.SourceGameId);
        Assert.Equal("101", game.SourceHomeTeamId);
        Assert.Equal("102", game.SourceAwayTeamId);
    }

    [Fact]
    public void InfersTheUnique1996SeasonOpponentAndNormalizesTheRoundDate()
    {
        const string payload = """
            {
              "totalPages": 1,
              "items": [
                {
                  "id": "known",
                  "sourceId": "known",
                  "status": "OFFICIAL",
                  "stage": "MAIN_ROUND",
                  "matchDay": 17,
                  "scheduledTime": "1998-01-02T23:00:00Z",
                  "homeTeam": { "teamId": "413", "name": "ALBA Berlin" },
                  "guestTeam": { "teamId": "415", "name": "Telekom Baskets Bonn" },
                  "result": { "homeTeamFinalScore": 80, "guestTeamFinalScore": 70 }
                },
                {
                  "id": "inferred",
                  "sourceId": "inferred",
                  "status": "OFFICIAL",
                  "stage": "MAIN_ROUND",
                  "matchDay": 17,
                  "scheduledTime": "1998-01-02T23:00:00Z",
                  "homeTeam": null,
                  "guestTeam": { "teamId": "412", "name": "TATAMI Rhöndorf" },
                  "result": { "homeTeamFinalScore": 64, "guestTeamFinalScore": 65 }
                }
              ]
            }
            """;

        var inferred = Assert.Single(GermanBasketballDataProvider.ParseGames(
            payload,
            "1996-1997",
            "https://example.test/bbl",
            DateTime.UtcNow), game => game.SourceGameId == "inferred");

        Assert.Equal("422", inferred.SourceHomeTeamId);
        Assert.Equal("SG Braunschweig", inferred.HomeTeamName);
        Assert.Equal(new DateTime(1997, 1, 2, 23, 0, 0, DateTimeKind.Utc), inferred.GameDateTimeUtc);
        Assert.Equal(GermanBasketballDataProvider.InferredParserVersion, inferred.Provenance!.ParserVersion);
        Assert.Contains("inferred-team=422", inferred.Provenance.SourceRevision);
    }

    [Fact]
    public void InfersOmitted1995HertenTeamFromTheOfficialFourteenTeamRoster()
    {
        const string payload = """
            {
              "totalPages": 1,
              "items": [
                {
                  "id": "herten-game",
                  "sourceId": "herten-game",
                  "status": "OFFICIAL",
                  "stage": "MAIN_ROUND",
                  "matchDay": 2,
                  "scheduledTime": "1995-09-17T13:00:00Z",
                  "homeTeam": null,
                  "guestTeam": { "teamId": "414", "name": "TV Germania Trier" },
                  "result": { "homeTeamFinalScore": 78, "guestTeamFinalScore": 81 }
                }
              ]
            }
            """;

        var inferred = Assert.Single(GermanBasketballDataProvider.ParseGames(
            payload,
            "1995-1996",
            "https://example.test/bbl",
            DateTime.UtcNow));

        Assert.Equal("herten-1995", inferred.SourceHomeTeamId);
        Assert.Equal("TuS Herten", inferred.HomeTeamName);
        Assert.Equal(GermanBasketballDataProvider.HertenInferredParserVersion, inferred.Provenance!.ParserVersion);
        Assert.Contains("inferred-roster=14-team-1995-1996", inferred.Provenance.SourceRevision);
    }

    [Fact]
    public void InfersOmitted1994PaderbornTeamFromTheOfficialTwelveTeamRoster()
    {
        const string payload = """
            {
              "totalPages": 1,
              "items": [
                {
                  "id": "paderborn-game",
                  "sourceId": "paderborn-game",
                  "status": "OFFICIAL",
                  "stage": "MAIN_ROUND",
                  "matchDay": 1,
                  "scheduledTime": "1994-09-16T18:00:00Z",
                  "homeTeam": { "teamId": "421", "name": "MTV 1846 Gießen" },
                  "guestTeam": null,
                  "result": { "homeTeamFinalScore": 100, "guestTeamFinalScore": 83 }
                }
              ]
            }
            """;

        var inferred = Assert.Single(GermanBasketballDataProvider.ParseGames(
            payload,
            "1994-1995",
            "https://example.test/bbl",
            DateTime.UtcNow));

        Assert.Equal("paderborn-1994", inferred.SourceAwayTeamId);
        Assert.Equal("Forbo Paderborn 91", inferred.AwayTeamName);
        Assert.Equal(GermanBasketballDataProvider.PaderbornInferredParserVersion, inferred.Provenance!.ParserVersion);
        Assert.Contains("inferred-roster=12-team-1994-1995", inferred.Provenance.SourceRevision);
    }

    [Fact]
    public void InfersOmitted1992BramscheAndDortmundTeamsFromTheRoundSchedule()
    {
        const string payload = """
            {
              "totalPages": 1,
              "items": [
                {
                  "id": "29582",
                  "sourceId": "29582",
                  "status": "OFFICIAL",
                  "stage": "MAIN_ROUND",
                  "matchDay": 1,
                  "scheduledTime": "1992-09-11T18:00:00Z",
                  "homeTeam": null,
                  "guestTeam": { "teamId": "422", "name": "SG Braunschweig" },
                  "result": { "homeTeamFinalScore": 75, "guestTeamFinalScore": 86 }
                },
                {
                  "id": "29624",
                  "sourceId": "29624",
                  "status": "OFFICIAL",
                  "stage": "MAIN_ROUND",
                  "matchDay": 8,
                  "scheduledTime": "1992-10-16T19:00:00Z",
                  "homeTeam": null,
                  "guestTeam": null,
                  "result": { "homeTeamFinalScore": 96, "guestTeamFinalScore": 99 }
                }
              ]
            }
            """;

        var games = GermanBasketballDataProvider.ParseGames(
            payload,
            "1992-1993",
            "https://example.test/bbl",
            DateTime.UtcNow);

        var oneSided = Assert.Single(games, game => game.SourceGameId == "29582");
        Assert.Equal("bramsche-1992", oneSided.SourceHomeTeamId);
        Assert.Equal("BG Bramsche/Osnabrück", oneSided.HomeTeamName);
        Assert.Equal(GermanBasketballDataProvider.BramscheDortmundInferredParserVersion, oneSided.Provenance!.ParserVersion);
        Assert.Contains("inferred-1992-teams=bramsche-1992", oneSided.Provenance.SourceRevision);

        var mutual = Assert.Single(games, game => game.SourceGameId == "29624");
        Assert.Equal("bramsche-1992", mutual.SourceHomeTeamId);
        Assert.Equal("dortmund-1992", mutual.SourceAwayTeamId);
        Assert.Equal("SVD 49 Dortmund", mutual.AwayTeamName);
    }

    [Fact]
    public void InfersOmitted1991BramscheTeamFromTheOfficialTwelveTeamRoster()
    {
        const string payload = """
            {
              "totalPages": 1,
              "items": [
                {
                  "id": "29392",
                  "sourceId": "29392",
                  "status": "OFFICIAL",
                  "stage": "MAIN_ROUND",
                  "matchDay": 1,
                  "scheduledTime": "1991-09-06T18:00:00Z",
                  "homeTeam": { "teamId": "416", "name": "Bayer Giants Leverkusen" },
                  "guestTeam": null,
                  "result": { "homeTeamFinalScore": 113, "guestTeamFinalScore": 81 }
                }
              ]
            }
            """;

        var inferred = Assert.Single(GermanBasketballDataProvider.ParseGames(
            payload,
            "1991-1992",
            "https://example.test/bbl",
            DateTime.UtcNow));

        Assert.Equal("bramsche-1991", inferred.SourceAwayTeamId);
        Assert.Equal("TuS Bramsche", inferred.AwayTeamName);
        Assert.Equal(GermanBasketballDataProvider.Bramsche1991InferredParserVersion, inferred.Provenance!.ParserVersion);
        Assert.Contains("roster=12-team-1991-1992", inferred.Provenance.SourceRevision);
    }

    [Fact]
    public void InfersOmitted1990BramscheTeamFromTheOfficialTwelveTeamRoster()
    {
        const string payload = """
            {
              "totalPages": 1,
              "items": [
                {
                  "id": "29207",
                  "sourceId": "29207",
                  "status": "OFFICIAL",
                  "stage": "MAIN_ROUND",
                  "matchDay": 1,
                  "scheduledTime": "1990-09-09T14:30:00Z",
                  "homeTeam": { "teamId": "421", "name": "MTV 1846 Gießen" },
                  "guestTeam": null,
                  "result": { "homeTeamFinalScore": 108, "guestTeamFinalScore": 118 }
                }
              ]
            }
            """;

        var inferred = Assert.Single(GermanBasketballDataProvider.ParseGames(
            payload,
            "1990-1991",
            "https://example.test/bbl",
            DateTime.UtcNow));

        Assert.Equal("bramsche-1990", inferred.SourceAwayTeamId);
        Assert.Equal("TuS Bramsche", inferred.AwayTeamName);
        Assert.Equal(GermanBasketballDataProvider.Bramsche1990InferredParserVersion, inferred.Provenance!.ParserVersion);
        Assert.Contains("roster=12-team-1990-1991", inferred.Provenance.SourceRevision);
    }

    [Fact]
    public void InfersOmitted1989HagenTeamFromTheOfficialTwelveTeamRoster()
    {
        const string payload = """
            {
              "totalPages": 1,
              "items": [
                {
                  "id": "29071",
                  "sourceId": "29071",
                  "status": "OFFICIAL",
                  "stage": "MAIN_ROUND",
                  "matchDay": 1,
                  "scheduledTime": "1989-09-30T18:30:00Z",
                  "homeTeam": { "teamId": "413", "name": "ALBA BERLIN" },
                  "guestTeam": null,
                  "result": { "homeTeamFinalScore": 111, "guestTeamFinalScore": 82 }
                }
              ]
            }
            """;

        var inferred = Assert.Single(GermanBasketballDataProvider.ParseGames(
            payload,
            "1989-1990",
            "https://example.test/bbl",
            DateTime.UtcNow));

        Assert.Equal("tsv-hagen-1860-1989", inferred.SourceAwayTeamId);
        Assert.Equal("TSV Hagen 1860", inferred.AwayTeamName);
        Assert.Equal(GermanBasketballDataProvider.Hagen1989InferredParserVersion, inferred.Provenance!.ParserVersion);
        Assert.Contains("roster=12-team-1989-1990", inferred.Provenance.SourceRevision);
    }

    [Fact]
    public void InfersOmitted1988HagenTeamFromTheOfficialTwelveTeamRoster()
    {
        const string payload = """
            {
              "totalPages": 1,
              "items": [
                {
                  "id": "28942",
                  "sourceId": "28942",
                  "status": "OFFICIAL",
                  "stage": "MAIN_ROUND",
                  "matchDay": 1,
                  "scheduledTime": "1988-09-16T17:30:00Z",
                  "homeTeam": { "teamId": "417", "name": "Brandt Hagen" },
                  "guestTeam": null,
                  "result": { "homeTeamFinalScore": 98, "guestTeamFinalScore": 66 }
                }
              ]
            }
            """;

        var inferred = Assert.Single(GermanBasketballDataProvider.ParseGames(
            payload,
            "1988-1989",
            "https://example.test/bbl",
            DateTime.UtcNow));

        Assert.Equal("tsv-hagen-1860-1988", inferred.SourceAwayTeamId);
        Assert.Equal("TSV Hagen 1860", inferred.AwayTeamName);
        Assert.Equal(GermanBasketballDataProvider.Hagen1988InferredParserVersion, inferred.Provenance!.ParserVersion);
        Assert.Contains("roster=12-team-1988-1989", inferred.Provenance.SourceRevision);
    }

    [Fact]
    public void Infers1987PostseasonDatesAfterTheRegularSeasonInStageOrder()
    {
        const string payload = """
            {
              "totalPages": 1,
              "items": [
                {
                  "id": "regular",
                  "sourceId": "regular",
                  "status": "OFFICIAL",
                  "stage": "MAIN_ROUND",
                  "scheduledTime": "1988-02-20T18:00:00Z",
                  "homeTeam": { "teamId": "1", "name": "Home" },
                  "guestTeam": { "teamId": "2", "name": "Away" },
                  "result": { "homeTeamFinalScore": 80, "guestTeamFinalScore": 70 }
                },
                {
                  "id": "quarterfinal-known",
                  "sourceId": "quarterfinal-known",
                  "status": "OFFICIAL",
                  "stage": "ROUND_OF_8",
                  "scheduledTime": "1988-03-11T23:00:00Z",
                  "homeTeam": { "teamId": "1", "name": "Home" },
                  "guestTeam": { "teamId": "2", "name": "Away" },
                  "result": { "homeTeamFinalScore": 81, "guestTeamFinalScore": 72 }
                },
                {
                  "id": "quarterfinal-inferred",
                  "sourceId": "quarterfinal-inferred",
                  "status": "OFFICIAL",
                  "stage": "ROUND_OF_8",
                  "scheduledTime": null,
                  "homeTeam": { "teamId": "1", "name": "Home" },
                  "guestTeam": { "teamId": "2", "name": "Away" },
                  "result": { "homeTeamFinalScore": 77, "guestTeamFinalScore": 75 }
                },
                {
                  "id": "semifinal-inferred",
                  "sourceId": "semifinal-inferred",
                  "status": "OFFICIAL",
                  "stage": "SEMI_FINALS",
                  "scheduledTime": null,
                  "homeTeam": { "teamId": "1", "name": "Home" },
                  "guestTeam": { "teamId": "2", "name": "Away" },
                  "result": { "homeTeamFinalScore": 79, "guestTeamFinalScore": 74 }
                },
                {
                  "id": "final-inferred",
                  "sourceId": "final-inferred",
                  "status": "OFFICIAL",
                  "stage": "FINALS",
                  "scheduledTime": null,
                  "homeTeam": { "teamId": "1", "name": "Home" },
                  "guestTeam": { "teamId": "2", "name": "Away" },
                  "result": { "homeTeamFinalScore": 82, "guestTeamFinalScore": 80 }
                }
              ]
            }
            """;

        var games = GermanBasketballDataProvider.ParseGames(
            payload,
            "1987-1988",
            "https://example.test/bbl",
            DateTime.UtcNow);

        var quarterfinal = Assert.Single(games, game => game.SourceGameId == "quarterfinal-inferred");
        var semifinal = Assert.Single(games, game => game.SourceGameId == "semifinal-inferred");
        var final = Assert.Single(games, game => game.SourceGameId == "final-inferred");
        Assert.Equal(new DateTime(1988, 2, 21, 12, 0, 0, DateTimeKind.Utc), quarterfinal.GameDateTimeUtc);
        Assert.Equal(new DateTime(1988, 3, 12, 12, 0, 0, DateTimeKind.Utc), semifinal.GameDateTimeUtc);
        Assert.Equal(new DateTime(1988, 3, 13, 12, 0, 0, DateTimeKind.Utc), final.GameDateTimeUtc);
        Assert.Equal(GermanBasketballDataProvider.HistoricalPostseasonDateInferredParserVersion, final.Provenance!.ParserVersion);
        Assert.Contains("inferred-date-after-regular-season", final.Provenance.SourceRevision);
    }

    [Fact]
    public void InfersMissingPostseasonOpponentsFor1989And1990Seasons()
    {
        const string payload = """
            {
              "totalPages": 1,
              "items": [
                {
                  "id": "regular",
                  "sourceId": "regular",
                  "status": "OFFICIAL",
                  "stage": "MAIN_ROUND",
                  "scheduledTime": "1990-02-20T18:00:00Z",
                  "homeTeam": { "teamId": "1", "name": "Home" },
                  "guestTeam": { "teamId": "2", "name": "Away" },
                  "result": { "homeTeamFinalScore": 80, "guestTeamFinalScore": 70 }
                },
                {
                  "id": "2000805",
                  "sourceId": "2000805",
                  "status": "OFFICIAL",
                  "stage": "ROUND_OF_8",
                  "scheduledTime": null,
                  "homeTeam": { "teamId": "420", "name": "Bamberg" },
                  "guestTeam": null,
                  "result": { "homeTeamFinalScore": 105, "guestTeamFinalScore": 82 }
                }
              ]
            }
            """;

        var hagen = Assert.Single(GermanBasketballDataProvider.ParseGames(
            payload,
            "1989-1990",
            "https://example.test/bbl",
            DateTime.UtcNow), game => game.SourceGameId == "2000805");
        Assert.Equal("tsv-hagen-1860-1989", hagen.SourceAwayTeamId);
        Assert.Equal("TSV Hagen 1860", hagen.AwayTeamName);
        Assert.Equal(GermanBasketballDataProvider.HistoricalPostseasonTeamAndDateInferredParserVersion, hagen.Provenance!.ParserVersion);

        var bramsche = Assert.Single(GermanBasketballDataProvider.ParseGames(
            payload.Replace("2000805", "2000813").Replace("1989-1990", "1990-1991"),
            "1990-1991",
            "https://example.test/bbl",
            DateTime.UtcNow), game => game.SourceGameId == "2000813");
        Assert.Equal("bramsche-1990", bramsche.SourceAwayTeamId);
        Assert.Equal("TuS Bramsche", bramsche.AwayTeamName);
        Assert.Equal(GermanBasketballDataProvider.HistoricalPostseasonTeamAndDateInferredParserVersion, bramsche.Provenance!.ParserVersion);
    }

    [Fact]
    public void InfersSingleMissingTeamFromTheHistorical1976Roster()
    {
        const string payload = """
            {
              "totalPages": 1,
              "items": [
                {
                  "id": "31240",
                  "sourceId": "31240",
                  "status": "OFFICIAL",
                  "stage": "MAIN_ROUND",
                  "matchDay": 1,
                  "scheduledTime": "1976-10-02T18:00:00Z",
                  "homeTeam": { "teamId": "298", "name": "BSC Saturn Köln" },
                  "guestTeam": null,
                  "result": { "homeTeamFinalScore": 70, "guestTeamFinalScore": 76 }
                }
              ]
            }
            """;

        var inferred = Assert.Single(GermanBasketballDataProvider.ParseGames(
            payload,
            "1976-1977",
            "https://example.test/bbl",
            DateTime.UtcNow));

        Assert.Equal("bc-usc-muenchen-1976", inferred.SourceAwayTeamId);
        Assert.Equal("BC/USC München", inferred.AwayTeamName);
        Assert.Equal(GermanBasketballDataProvider.HistoricalRosterInferredParserVersion, inferred.Provenance!.ParserVersion);
    }

    [Fact]
    public void InfersMissing1975TeamsFromTheHistoricalTenTeamSchedule()
    {
        const string payload = """
            {
              "totalPages": 1,
              "items": [
                {
                  "id": "31328",
                  "sourceId": "31328",
                  "status": "OFFICIAL",
                  "stage": "MAIN_ROUND",
                  "matchDay": 1,
                  "scheduledTime": "1975-10-04T18:00:00Z",
                  "homeTeam": null,
                  "guestTeam": null,
                  "result": { "homeTeamFinalScore": 80, "guestTeamFinalScore": 73 }
                },
                {
                  "id": "31330",
                  "sourceId": "31330",
                  "status": "OFFICIAL",
                  "stage": "MAIN_ROUND",
                  "matchDay": 1,
                  "scheduledTime": "1975-10-04T18:00:00Z",
                  "homeTeam": { "teamId": "417", "name": "Brandt Hagen" },
                  "guestTeam": null,
                  "result": { "homeTeamFinalScore": 106, "guestTeamFinalScore": 73 }
                }
              ]
            }
            """;

        var games = GermanBasketballDataProvider.ParseGames(
            payload,
            "1975-1976",
            "https://example.test/bbl",
            DateTime.UtcNow);

        Assert.Collection(
            games,
            bothMissing =>
            {
                Assert.Equal("adb-koblenz-1975", bothMissing.SourceHomeTeamId);
                Assert.Equal("ruwa-dellwig-1975", bothMissing.SourceAwayTeamId);
                Assert.Equal(GermanBasketballDataProvider.Historical1975RosterInferredParserVersion, bothMissing.Provenance!.ParserVersion);
            },
            oneMissing =>
            {
                Assert.Equal("bc-usc-muenchen-1975", oneMissing.SourceAwayTeamId);
                Assert.Equal(GermanBasketballDataProvider.Historical1975RosterInferredParserVersion, oneMissing.Provenance!.ParserVersion);
            });
    }

    [Fact]
    public void InfersTwoMissing1979TeamsUsingTheHistoricalScheduleAssignment()
    {
        const string payload = """
            {
              "totalPages": 1,
              "items": [
                {
                  "id": "30993",
                  "sourceId": "30993",
                  "status": "OFFICIAL",
                  "stage": "MAIN_ROUND",
                  "matchDay": 6,
                  "scheduledTime": "1979-12-01T18:00:00Z",
                  "homeTeam": null,
                  "guestTeam": null,
                  "result": { "homeTeamFinalScore": 102, "guestTeamFinalScore": 86 }
                }
              ]
            }
            """;

        var inferred = Assert.Single(GermanBasketballDataProvider.ParseGames(
            payload,
            "1979-1980",
            "https://example.test/bbl",
            DateTime.UtcNow));

        Assert.Equal("hamburger-tb-1979", inferred.SourceHomeTeamId);
        Assert.Equal("Hamburger TB", inferred.HomeTeamName);
        Assert.Equal("eintracht-frankfurt-1979", inferred.SourceAwayTeamId);
        Assert.Equal("Eintracht Frankfurt", inferred.AwayTeamName);
        Assert.Equal(GermanBasketballDataProvider.HistoricalRosterInferredParserVersion, inferred.Provenance!.ParserVersion);
    }

    [Fact]
    public void InfersBothOmitted1985TeamsFromTheArchivedTwelveTeamSchedule()
    {
        const string payload = """
            {
              "totalPages": 1,
              "items": [
                {
                  "id": "28590",
                  "sourceId": "28590",
                  "status": "OFFICIAL",
                  "stage": "MAIN_ROUND",
                  "matchDay": 4,
                  "scheduledTime": "1985-11-02T18:00:00Z",
                  "homeTeam": null,
                  "guestTeam": null,
                  "result": { "homeTeamFinalScore": 81, "guestTeamFinalScore": 80 }
                }
              ]
            }
            """;

        var inferred = Assert.Single(GermanBasketballDataProvider.ParseGames(
            payload,
            "1985-1986",
            "https://example.test/bbl",
            DateTime.UtcNow));

        Assert.Equal("bc-giants-osnabrueck-1985", inferred.SourceHomeTeamId);
        Assert.Equal("BC Giants Osnabrück", inferred.HomeTeamName);
        Assert.Equal("tsv-hagen-1860-1985", inferred.SourceAwayTeamId);
        Assert.Equal("TSV Hagen 1860", inferred.AwayTeamName);
        Assert.Equal(GermanBasketballDataProvider.Osnabrueck1985InferredParserVersion, inferred.Provenance!.ParserVersion);
    }

    [Fact]
    public void InfersOmitted1986OsnabrueckTeamFromTheOfficialElevenTeamRoster()
    {
        const string payload = """
            {
              "totalPages": 1,
              "items": [
                {
                  "id": "28702",
                  "sourceId": "28702",
                  "status": "OFFICIAL",
                  "stage": "MAIN_ROUND",
                  "matchDay": 1,
                  "scheduledTime": "1986-09-27T18:00:00Z",
                  "homeTeam": { "teamId": "296", "name": "ASC 46 Göttingen" },
                  "guestTeam": null,
                  "result": { "homeTeamFinalScore": 99, "guestTeamFinalScore": 89 }
                }
              ]
            }
            """;

        var inferred = Assert.Single(GermanBasketballDataProvider.ParseGames(
            payload,
            "1986-1987",
            "https://example.test/bbl",
            DateTime.UtcNow));

        Assert.Equal("bc-giants-osnabrueck-1986", inferred.SourceAwayTeamId);
        Assert.Equal("BC Giants Osnabrück", inferred.AwayTeamName);
        Assert.Equal(GermanBasketballDataProvider.Osnabrueck1986InferredParserVersion, inferred.Provenance!.ParserVersion);
        Assert.Contains("roster=11-team-1986-1987", inferred.Provenance.SourceRevision);
    }

    [Fact]
    public async Task FetchesRuntimeCredentialAndRequestsTheBblCompetitionFeed()
    {
        var handler = new BblHandler();
        var options = Options.Create(new GermanBasketballOptions
        {
            OfficialBaseUrl = "https://www.easycredit-bbl.de/",
            ApiBaseUrl = "https://api.basketball-bundesliga.de/",
            AuthPagePath = "teams/413/2006",
            MinRequestIntervalMilliseconds = 0,
            MaxTransientRetries = 0
        });
        var provider = new GermanBasketballDataProvider(new HttpClient(handler), options);
        var context = new BackfillExecutionContext(2, 0);
        var league = await provider.ResolveLeagueAsync("Germany", "Basketball Bundesliga", context, CancellationToken.None);

        var result = await provider.GetGamesAsync(league!, "1975-1976", context, CancellationToken.None);

        Assert.Single(result.Games);
        Assert.False(result.HasMorePages);
        Assert.Equal(2, context.RequestsUsed);
        Assert.Contains("competition=BBL", handler.ApiRequest!.RequestUri!.Query);
        Assert.Equal("publicWebUser", handler.ApiRequest.Headers.GetValues("x-api-key").Single());
        Assert.Equal("test-secret", handler.ApiRequest.Headers.GetValues("x-api-secret").Single());
    }

    private sealed class BblHandler : HttpMessageHandler
    {
        public HttpRequestMessage? ApiRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.Host == "www.easycredit-bbl.de")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("<script id=\"__NEXT_DATA__\">{\"props\":{\"pageProps\":{\"key\":\"test-secret\"}}}</script>")
                });
            }

            ApiRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {"totalPages":1,"items":[{"id":"game-1","sourceId":"game-1","status":"OFFICIAL","stage":"MAIN_ROUND","scheduledTime":"1975-10-01T15:00:00Z","homeTeam":{"teamId":"1","name":"Home"},"guestTeam":{"teamId":"2","name":"Away"},"result":{"homeTeamFinalScore":80,"guestTeamFinalScore":70}}]}
                    """)
            });
        }
    }
}
