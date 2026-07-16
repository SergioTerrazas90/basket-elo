using System.Globalization;
using System.Security.Cryptography;
using BasketElo.Domain.Backfill;
using Microsoft.Extensions.Options;
using Microsoft.VisualBasic.FileIO;

namespace BasketElo.Infrastructure.Backfill;

public sealed class FiveThirtyEightBasketballDataProvider(IOptions<FiveThirtyEightOptions> options)
    : IBasketballDataProvider
{
    public const string Source = "fivethirtyeight";
    public const string ParserVersion = "fivethirtyeight-nbaallelo-v1";

    private readonly SemaphoreSlim loadLock = new(1, 1);
    private IReadOnlyDictionary<string, IReadOnlyCollection<BasketballProviderGame>>? gamesBySeason;
    private string? loadedArchivePath;
    private DateTime loadedArchiveWriteUtc;

    public string SourceKey => Source;

    public Task<BasketballProviderLeague?> ResolveLeagueAsync(
        string country,
        string leagueName,
        BackfillExecutionContext context,
        CancellationToken cancellationToken)
    {
        var league = string.Equals(country, "United States", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(leagueName, "NBA", StringComparison.OrdinalIgnoreCase)
                ? new BasketballProviderLeague(Source, "NBA", "NBA", "USA", "end_year")
                : null;
        return Task.FromResult(league);
    }

    public async Task<(IReadOnlyCollection<BasketballProviderGame> Games, bool HasMorePages, IReadOnlyCollection<string> Warnings)> GetGamesAsync(
        BasketballProviderLeague league,
        string season,
        BackfillExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(league.Source, Source, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(league.SourceLeagueId, "NBA", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("FiveThirtyEight provider only supports United States: NBA.");
        }

        var archive = await LoadArchiveAsync(cancellationToken);
        var canonicalSeason = SeasonLabelNormalizer.ToFullSeasonLabel(season);
        return (
            archive.GetValueOrDefault(canonicalSeason) ?? [],
            false,
            []);
    }

    private async Task<IReadOnlyDictionary<string, IReadOnlyCollection<BasketballProviderGame>>> LoadArchiveAsync(
        CancellationToken cancellationToken)
    {
        var archivePath = Path.GetFullPath(options.Value.ArchivePath);
        if (!File.Exists(archivePath))
        {
            throw new FileNotFoundException(
                $"Licensed FiveThirtyEight NBA archive is missing: {archivePath}.",
                archivePath);
        }

        var writeUtc = File.GetLastWriteTimeUtc(archivePath);
        if (gamesBySeason is not null &&
            string.Equals(loadedArchivePath, archivePath, StringComparison.Ordinal) &&
            loadedArchiveWriteUtc == writeUtc)
        {
            return gamesBySeason;
        }

        await loadLock.WaitAsync(cancellationToken);
        try
        {
            writeUtc = File.GetLastWriteTimeUtc(archivePath);
            if (gamesBySeason is not null &&
                string.Equals(loadedArchivePath, archivePath, StringComparison.Ordinal) &&
                loadedArchiveWriteUtc == writeUtc)
            {
                return gamesBySeason;
            }

            await VerifyChecksumAsync(archivePath, cancellationToken);
            var parsed = await Task.Run(
                () => ParseArchive(archivePath, writeUtc, cancellationToken),
                cancellationToken);
            gamesBySeason = parsed;
            loadedArchivePath = archivePath;
            loadedArchiveWriteUtc = writeUtc;
            return parsed;
        }
        finally
        {
            loadLock.Release();
        }
    }

    private async Task VerifyChecksumAsync(string archivePath, CancellationToken cancellationToken)
    {
        var expected = options.Value.ExpectedSha256.Trim().ToLowerInvariant();
        if (expected.Length != 64 || expected.Any(ch => !Uri.IsHexDigit(ch)))
        {
            throw new InvalidOperationException("FiveThirtyEight:ExpectedSha256 must be a SHA-256 hex digest.");
        }

        await using var stream = File.OpenRead(archivePath);
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"FiveThirtyEight archive checksum mismatch for '{archivePath}'. Expected {expected}, got {actual}.");
        }
    }

    private IReadOnlyDictionary<string, IReadOnlyCollection<BasketballProviderGame>> ParseArchive(
        string archivePath,
        DateTime fetchedAtUtc,
        CancellationToken cancellationToken)
    {
        using var parser = new TextFieldParser(archivePath)
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = false
        };
        parser.SetDelimiters(",");

        var headers = parser.ReadFields() ?? throw new InvalidDataException("FiveThirtyEight archive has no header row.");
        var columns = headers
            .Select((name, index) => new { name, index })
            .ToDictionary(x => x.name, x => x.index, StringComparer.OrdinalIgnoreCase);
        var requiredColumns = new[]
        {
            "game_id", "lg_id", "_iscopy", "year_id", "date_game", "team_id", "fran_id",
            "pts", "opp_id", "opp_fran", "opp_pts"
        };
        foreach (var required in requiredColumns)
        {
            if (!columns.ContainsKey(required))
            {
                throw new InvalidDataException($"FiveThirtyEight archive is missing required column '{required}'.");
            }
        }

        var parsed = new Dictionary<string, List<BasketballProviderGame>>(StringComparer.Ordinal);
        var sourceGameIds = new HashSet<string>(StringComparer.Ordinal);
        var rowNumber = 1;
        while (!parser.EndOfData)
        {
            cancellationToken.ThrowIfCancellationRequested();
            rowNumber++;
            var fields = parser.ReadFields();
            if (fields is null || fields.Length == 0)
            {
                continue;
            }

            string Field(string name) => columns[name] < fields.Length ? fields[columns[name]] : string.Empty;
            if (!string.Equals(Field("lg_id"), "NBA", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Field("_iscopy"), "0", StringComparison.Ordinal))
            {
                continue;
            }

            if (!int.TryParse(Field("year_id"), NumberStyles.None, CultureInfo.InvariantCulture, out var endYear) ||
                !DateTime.TryParseExact(
                    Field("date_game"),
                    "M/d/yyyy",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var gameDateUtc) ||
                !short.TryParse(Field("pts"), NumberStyles.None, CultureInfo.InvariantCulture, out var homeScore) ||
                !short.TryParse(Field("opp_pts"), NumberStyles.None, CultureInfo.InvariantCulture, out var awayScore))
            {
                throw new InvalidDataException($"FiveThirtyEight archive row {rowNumber} has invalid season, date, or score data.");
            }

            var season = $"{endYear - 1}-{endYear}";
            var seasonStartYear = endYear - 1;
            var sourceGameId = Field("game_id").Trim();
            if (!sourceGameIds.Add(sourceGameId))
            {
                throw new InvalidDataException($"FiveThirtyEight archive contains duplicate primary game ID '{sourceGameId}'.");
            }

            var homeId = Field("team_id").Trim().ToUpperInvariant();
            var awayId = Field("opp_id").Trim().ToUpperInvariant();
            var homeName = ResolveObservedName(homeId, Field("fran_id"), seasonStartYear);
            var awayName = ResolveObservedName(awayId, Field("opp_fran"), seasonStartYear);
            if (!parsed.TryGetValue(season, out var games))
            {
                games = [];
                parsed[season] = games;
            }

            games.Add(new BasketballProviderGame(
                Source,
                sourceGameId,
                DateTime.SpecifyKind(gameDateUtc, DateTimeKind.Utc),
                "finished",
                homeId,
                homeName,
                awayId,
                awayName,
                homeScore,
                awayScore,
                new BasketballProviderGameProvenance(
                    options.Value.SourceUrl,
                    endYear.ToString(CultureInfo.InvariantCulture),
                    fetchedAtUtc,
                    ParserVersion,
                    options.Value.SourceRevision)));
        }

        return parsed.ToDictionary(
            x => x.Key,
            x => (IReadOnlyCollection<BasketballProviderGame>)x.Value,
            StringComparer.Ordinal);
    }

    private static string ResolveObservedName(string sourceTeamId, string franchiseName, int seasonStartYear)
    {
        var match = NbaFranchiseCatalog.Resolve(sourceTeamId, franchiseName, seasonStartYear);
        return match?.Alias.Name ?? throw new InvalidDataException(
            $"FiveThirtyEight NBA team '{sourceTeamId}' ({franchiseName}) is not in the franchise catalog for {seasonStartYear}-{seasonStartYear + 1}.");
    }
}
