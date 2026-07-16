using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using BasketElo.Domain.Backfill;

namespace BasketElo.Infrastructure.Backfill;

public interface INbaHistoricalAuditService
{
    Task<NbaAuditReport> RunAsync(
        NbaAuditRequest request,
        NbaAuditReport? resumeFrom,
        CancellationToken cancellationToken);
}

public sealed class NbaHistoricalAuditService(
    BasketballReferenceBasketballDataProvider provider) : INbaHistoricalAuditService
{
    public async Task<NbaAuditReport> RunAsync(
        NbaAuditRequest request,
        NbaAuditReport? resumeFrom,
        CancellationToken cancellationToken)
    {
        var seasons = GetSeasonRange(request.StartSeason, request.EndSeason);
        if (request.MaxRequests < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "MaxRequests cannot be negative.");
        }

        var context = new BackfillExecutionContext(request.MaxRequests, 0);
        var league = await provider.ResolveLeagueAsync(
            "United States",
            "NBA",
            context,
            cancellationToken) ?? throw new InvalidOperationException("NBA could not be resolved by the provider.");
        var resumable = (resumeFrom?.Seasons ?? [])
            .Where(result => result.Status.StartsWith("completed", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(result => result.Season, StringComparer.OrdinalIgnoreCase);
        var results = new List<NbaAuditSeasonResult>();
        var totalStopwatch = Stopwatch.StartNew();

        foreach (var season in seasons)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (resumable.TryGetValue(season, out var existing))
            {
                results.Add(existing with { Resumed = true });
                continue;
            }

            var seasonStopwatch = Stopwatch.StartNew();
            var requestsBefore = context.RequestsUsed;
            try
            {
                var response = await provider.GetGamesAsync(league, season, context, cancellationToken);
                var duplicateCount = response.Games
                    .GroupBy(game => game.SourceGameId, StringComparer.Ordinal)
                    .Sum(group => Math.Max(0, group.Count() - 1));
                var missingScoreCount = response.Games.Count(game =>
                    game.HomeScore is null || game.AwayScore is null);
                results.Add(new NbaAuditSeasonResult(
                    season,
                    BasketballReferenceBasketballDataProvider.ToSourceSeasonKey(season),
                    response.Warnings.Count > 0 ? "completed_with_warnings" : "completed",
                    response.Games.Count,
                    missingScoreCount,
                    duplicateCount,
                    response.Warnings.Count,
                    response.Warnings.Order(StringComparer.Ordinal).ToList(),
                    context.RequestsUsed - requestsBefore,
                    seasonStopwatch.ElapsedMilliseconds,
                    null,
                    false));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                results.Add(new NbaAuditSeasonResult(
                    season,
                    BasketballReferenceBasketballDataProvider.ToSourceSeasonKey(season),
                    "failed",
                    0,
                    0,
                    0,
                    0,
                    [],
                    context.RequestsUsed - requestsBefore,
                    seasonStopwatch.ElapsedMilliseconds,
                    exception.Message,
                    false));
            }
        }

        totalStopwatch.Stop();
        var ordered = results.OrderBy(result => result.Season, StringComparer.Ordinal).ToList();
        return new NbaAuditReport(
            request.StartSeason,
            request.EndSeason,
            BasketballReferenceBasketballDataProvider.Source,
            ordered.Sum(result => result.RequestCount),
            totalStopwatch.ElapsedMilliseconds,
            0,
            ordered);
    }

    public static IReadOnlyList<string> GetSeasonRange(string startSeason, string endSeason)
    {
        var normalizedStart = SeasonLabelNormalizer.ToFullSeasonLabel(startSeason);
        var normalizedEnd = SeasonLabelNormalizer.ToFullSeasonLabel(endSeason);
        var startYear = SeasonLabelNormalizer.ParseStartYear(normalizedStart);
        var endYear = SeasonLabelNormalizer.ParseStartYear(normalizedEnd);
        if (startYear < 1946 || endYear < startYear)
        {
            throw new ArgumentException("NBA audit range must start at 1946-1947 or later and end at or after the start season.");
        }

        return Enumerable.Range(startYear, endYear - startYear + 1)
            .Select(year => $"{year}-{year + 1}")
            .ToList();
    }
}

public static class NbaAuditReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static async Task<NbaAuditReport?> ReadResumeReportAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        if (!Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Resume is supported only for JSON audit reports.");
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<NbaAuditReport>(stream, JsonOptions, cancellationToken);
    }

    public static async Task WriteAsync(
        NbaAuditReport report,
        string path,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        if (Path.GetExtension(fullPath).Equals(".csv", StringComparison.OrdinalIgnoreCase))
        {
            await File.WriteAllTextAsync(fullPath, ToCsv(report), new UTF8Encoding(false), cancellationToken);
            return;
        }

        if (!Path.GetExtension(fullPath).Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Audit output must use a .json or .csv extension.");
        }

        await using var stream = File.Create(fullPath);
        await JsonSerializer.SerializeAsync(stream, report, JsonOptions, cancellationToken);
        await stream.WriteAsync("\n"u8.ToArray(), cancellationToken);
    }

    private static string ToCsv(NbaAuditReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("season,sourceSeasonKey,status,gameCount,missingScoreCount,duplicateSourceIdCount,warningCount,requestCount,elapsedMilliseconds,error,warnings");
        foreach (var result in report.Seasons.OrderBy(row => row.Season, StringComparer.Ordinal))
        {
            var values = new[]
            {
                result.Season,
                result.SourceSeasonKey,
                result.Status,
                result.GameCount.ToString(CultureInfo.InvariantCulture),
                result.MissingScoreCount.ToString(CultureInfo.InvariantCulture),
                result.DuplicateSourceIdCount.ToString(CultureInfo.InvariantCulture),
                result.WarningCount.ToString(CultureInfo.InvariantCulture),
                result.RequestCount.ToString(CultureInfo.InvariantCulture),
                result.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture),
                result.Error ?? string.Empty,
                string.Join(" | ", result.Warnings)
            };
            builder.AppendLine(string.Join(',', values.Select(Escape)));
        }

        return builder.ToString();
    }

    private static string Escape(string value) =>
        $"\"{value.Replace("\"", "\"\"")}\"";
}

public sealed record NbaAuditRequest(
    string StartSeason,
    string EndSeason,
    int MaxRequests = 0);

public sealed record NbaAuditReport(
    string StartSeason,
    string EndSeason,
    string Provider,
    int RequestCount,
    long ElapsedMilliseconds,
    int DatabaseWrites,
    IReadOnlyList<NbaAuditSeasonResult> Seasons);

public sealed record NbaAuditSeasonResult(
    string Season,
    string SourceSeasonKey,
    string Status,
    int GameCount,
    int MissingScoreCount,
    int DuplicateSourceIdCount,
    int WarningCount,
    IReadOnlyList<string> Warnings,
    int RequestCount,
    long ElapsedMilliseconds,
    string? Error,
    bool Resumed);
