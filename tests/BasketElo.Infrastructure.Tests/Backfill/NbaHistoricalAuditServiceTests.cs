using BasketElo.Infrastructure.Backfill;
using Microsoft.Extensions.Options;
using Xunit;

namespace BasketElo.Infrastructure.Tests.Backfill;

public class NbaHistoricalAuditServiceTests
{
    [Fact]
    public async Task SmallRangeProducesOrderedReadOnlyDiagnosticsAndSupportsResume()
    {
        var service = CreateService(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "BasketballReference"));
        var report = await service.RunAsync(
            new NbaAuditRequest("2022-2023", "2022-2023"),
            null,
            CancellationToken.None);

        Assert.Equal(0, report.DatabaseWrites);
        Assert.Equal(0, report.RequestCount);
        var season = Assert.Single(report.Seasons);
        Assert.Equal("NBA_2023", season.SourceSeasonKey);
        Assert.Equal(4, season.GameCount);
        Assert.Equal(3, season.MissingScoreCount);
        Assert.Equal(0, season.DuplicateSourceIdCount);
        Assert.True(season.WarningCount > 0);
        Assert.Null(season.Error);

        var invalidArchiveService = CreateService(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
        var resumed = await invalidArchiveService.RunAsync(
            new NbaAuditRequest("2022-2023", "2022-2023"),
            report,
            CancellationToken.None);

        Assert.True(Assert.Single(resumed.Seasons).Resumed);
        Assert.Equal(season.GameCount, resumed.Seasons[0].GameCount);
    }

    [Fact]
    public async Task WritersProduceStableJsonAndCsvShapes()
    {
        var service = CreateService(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "BasketballReference"));
        var report = await service.RunAsync(
            new NbaAuditRequest("1946-1947", "1946-1947"),
            null,
            CancellationToken.None);
        var directory = Path.Combine(Path.GetTempPath(), $"basket-elo-audit-{Guid.NewGuid():N}");
        var jsonPath = Path.Combine(directory, "audit.json");
        var csvPath = Path.Combine(directory, "audit.csv");
        try
        {
            await NbaAuditReportWriter.WriteAsync(report, jsonPath, CancellationToken.None);
            await NbaAuditReportWriter.WriteAsync(report, csvPath, CancellationToken.None);
            var restored = await NbaAuditReportWriter.ReadResumeReportAsync(jsonPath, CancellationToken.None);
            var csv = await File.ReadAllTextAsync(csvPath);

            Assert.NotNull(restored);
            Assert.Equal(report.StartSeason, restored.StartSeason);
            Assert.Equal(report.EndSeason, restored.EndSeason);
            Assert.Equal(report.RequestCount, restored.RequestCount);
            Assert.Equal(report.DatabaseWrites, restored.DatabaseWrites);
            var expectedSeason = Assert.Single(report.Seasons);
            var restoredSeason = Assert.Single(restored.Seasons);
            Assert.Equal(expectedSeason.Season, restoredSeason.Season);
            Assert.Equal(expectedSeason.Status, restoredSeason.Status);
            Assert.Equal(expectedSeason.GameCount, restoredSeason.GameCount);
            Assert.Equal(expectedSeason.Warnings, restoredSeason.Warnings);
            Assert.Contains("season,sourceSeasonKey,status", csv, StringComparison.Ordinal);
            Assert.Contains("\"1946-1947\",\"BAA_1947\"", csv, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public void RejectsMalformedOrReversedRanges()
    {
        Assert.Throws<ArgumentException>(() =>
            NbaHistoricalAuditService.GetSeasonRange("1945-1946", "1946-1947"));
        Assert.Throws<ArgumentException>(() =>
            NbaHistoricalAuditService.GetSeasonRange("2000-2001", "1999-2000"));
    }

    private static NbaHistoricalAuditService CreateService(string archiveRoot)
    {
        var providerOptions = Options.Create(new BasketballReferenceOptions
        {
            ArchiveRoot = archiveRoot,
            NetworkAccessEnabled = false
        });
        var provider = new BasketballReferenceBasketballDataProvider(
            new HttpClient(new NoNetworkHandler())
            {
                BaseAddress = new Uri("https://www.basketball-reference.com/")
            },
            providerOptions,
            new BasketballReferenceRateLimiter(providerOptions));
        return new NbaHistoricalAuditService(provider);
    }

    private sealed class NoNetworkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Audit tests must use local fixtures.");
    }
}
