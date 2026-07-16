using System.Security.Cryptography;
using System.Text;
using BasketElo.Domain.Backfill;
using BasketElo.Infrastructure.Backfill;
using Microsoft.Extensions.Options;
using Xunit;

namespace BasketElo.Infrastructure.Tests.Backfill;

public class FiveThirtyEightBasketballDataProviderTests
{
    private const string Fixture = """
        gameorder,game_id,lg_id,_iscopy,year_id,date_game,seasongame,is_playoffs,team_id,fran_id,pts,elo_i,elo_n,win_equiv,opp_id,opp_fran,opp_pts,opp_elo_i,opp_elo_n,game_location,game_result,forecast,notes
        1,196310160BAL,NBA,0,1964,10/16/1963,1,0,BAL,Wizards,109,1300,1310,42,BOS,Celtics,95,1300,1290,H,W,0.6,
        1,196310160BAL,NBA,1,1964,10/16/1963,1,0,BOS,Celtics,95,1300,1290,40,BAL,Wizards,109,1300,1310,A,L,0.4,
        2,196310170NYA,ABA,0,1968,10/17/1967,1,0,NYA,Nets,100,1300,1310,42,INA,Pacers,90,1300,1290,H,W,0.6,
        """;

    [Fact]
    public async Task ReadsPrimaryNbaRowsAndIgnoresCopiesAndAba()
    {
        var archivePath = await WriteFixtureAsync(Fixture);
        try
        {
            var provider = CreateProvider(archivePath, Sha256(Fixture));
            var league = await provider.ResolveLeagueAsync(
                "United States",
                "NBA",
                new BackfillExecutionContext(0, 0),
                CancellationToken.None);

            var result = await provider.GetGamesAsync(
                league!,
                "1963-1964",
                new BackfillExecutionContext(0, 0),
                CancellationToken.None);

            var game = Assert.Single(result.Games);
            Assert.Equal("196310160BAL", game.SourceGameId);
            Assert.Equal("BAL", game.SourceHomeTeamId);
            Assert.Equal("Baltimore Bullets", game.HomeTeamName);
            Assert.Equal("Boston Celtics", game.AwayTeamName);
            Assert.Equal((short)109, game.HomeScore);
            Assert.Equal((short)95, game.AwayScore);
            Assert.Equal("1964", game.Provenance?.SourceSeasonKey);
            Assert.Equal("fixture-revision", game.Provenance?.SourceRevision);
            Assert.Empty(result.Warnings);
        }
        finally
        {
            File.Delete(archivePath);
        }
    }

    [Fact]
    public async Task RejectsArchiveWhenChecksumDoesNotMatch()
    {
        var archivePath = await WriteFixtureAsync(Fixture);
        try
        {
            var provider = CreateProvider(archivePath, new string('0', 64));
            var league = await provider.ResolveLeagueAsync(
                "United States",
                "NBA",
                new BackfillExecutionContext(0, 0),
                CancellationToken.None);

            await Assert.ThrowsAsync<InvalidDataException>(() => provider.GetGamesAsync(
                league!,
                "1963-1964",
                new BackfillExecutionContext(0, 0),
                CancellationToken.None));
        }
        finally
        {
            File.Delete(archivePath);
        }
    }

    private static FiveThirtyEightBasketballDataProvider CreateProvider(string path, string checksum) =>
        new(Options.Create(new FiveThirtyEightOptions
        {
            ArchivePath = path,
            SourceUrl = "https://data.example.test/nbaallelo.csv",
            SourceRevision = "fixture-revision",
            ExpectedSha256 = checksum
        }));

    private static async Task<string> WriteFixtureAsync(string contents)
    {
        var path = Path.Combine(Path.GetTempPath(), $"nbaallelo-{Guid.NewGuid():N}.csv");
        await File.WriteAllTextAsync(path, contents, new UTF8Encoding(false));
        return path;
    }

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
