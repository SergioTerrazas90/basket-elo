using System.Globalization;
using System.Text;
using BasketElo.Domain.Backfill;
using Microsoft.VisualBasic.FileIO;

namespace BasketElo.Infrastructure.Backfill;

/// <summary>
/// Imports the 2000-01 through 2007-08 ULEB EuroLeague bridge from the
/// euroleagueR community release, whose match-results CSV is derived from the
/// historical EuroLeague results API and contains one row per finished game.
/// </summary>
public sealed class EuroleagueRHistoricalDataProvider(HttpClient httpClient) : IBasketballDataProvider
{
    public const string Source = "euroleagueR";
    public const string ParserVersion = "euroleagueR-match-results-csv-v1";
    public const string ResultsUrl = "https://github.com/JaseZiv/euroleagueR_data/releases/download/csv_copies/euroleague_match_results.csv";

    public string SourceKey => Source;

    public Task<BasketballProviderLeague?> ResolveLeagueAsync(
        string country,
        string leagueName,
        BackfillExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (!country.Equals("Europe", StringComparison.OrdinalIgnoreCase) ||
            !leagueName.Equals("Euroleague", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<BasketballProviderLeague?>(null);
        }

        return Task.FromResult<BasketballProviderLeague?>(
            new BasketballProviderLeague(Source, "euroleagueR", "Euroleague", "EUR", "literal"));
    }

    public async Task<(IReadOnlyCollection<BasketballProviderGame> Games, bool HasMorePages, IReadOnlyCollection<string> Warnings)> GetGamesAsync(
        BasketballProviderLeague league,
        string season,
        BackfillExecutionContext context,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var (startYear, endYear) = ParseSeason(season);
        if (startYear is < 2000 or > 2007)
        {
            warnings.Add($"euroleagueR historical coverage is configured for 2000-2001 through 2007-2008; {season} is outside that range.");
            return ([], false, warnings);
        }

        if (!context.CanUseRequest())
        {
            warnings.Add($"Request budget reached before the euroleagueR match-results archive could be fetched for {season}.");
            return ([], false, warnings);
        }

        context.ConsumeRequest();
        using var response = await httpClient.GetAsync(ResultsUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var fetchedAtUtc = DateTime.UtcNow;
        var games = ParseCsv(stream, startYear, endYear, fetchedAtUtc, warnings, cancellationToken);
        warnings.Add($"euroleagueR parsed {games.Count} distinct Euroleague game(s) for {season}.");
        return (games, false, warnings);
    }

    internal static IReadOnlyCollection<BasketballProviderGame> ParseCsv(
        Stream stream,
        int startYear,
        int endYear,
        DateTime fetchedAtUtc,
        ICollection<string> warnings,
        CancellationToken cancellationToken)
    {
        using var parser = new TextFieldParser(stream, Encoding.UTF8, true)
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = false
        };
        parser.SetDelimiters(",");

        var headers = parser.ReadFields() ?? throw new InvalidDataException("euroleagueR results archive has no header row.");
        var columns = headers
            .Select((name, index) => new { Name = name.Trim('"'), Index = index })
            .ToDictionary(x => x.Name, x => x.Index, StringComparer.OrdinalIgnoreCase);
        var required = new[]
        {
            "code", "date", "season_code", "audience_confirmed", "code_home", "name_home",
            "score_home", "code_away", "name_away", "score_away", "phase_type_name", "round_name"
        };
        foreach (var column in required)
        {
            if (!columns.ContainsKey(column))
            {
                throw new InvalidDataException($"euroleagueR results archive is missing required column '{column}'.");
            }
        }

        var seasonCode = $"E{startYear}";
        var games = new Dictionary<string, BasketballProviderGame>(StringComparer.Ordinal);
        while (!parser.EndOfData)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fields = parser.ReadFields();
            if (fields is null || fields.Length == 0)
            {
                continue;
            }

            string Field(string name) => columns[name] < fields.Length ? fields[columns[name]] : string.Empty;
            if (!string.Equals(Field("season_code"), seasonCode, StringComparison.OrdinalIgnoreCase) ||
                !IsTrue(Field("audience_confirmed")) ||
                !short.TryParse(Field("score_home"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var homeScore) ||
                !short.TryParse(Field("score_away"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var awayScore) ||
                !DateTimeOffset.TryParse(Field("date"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var gameDate) ||
                string.IsNullOrWhiteSpace(Field("code")) ||
                string.IsNullOrWhiteSpace(Field("code_home")) ||
                string.IsNullOrWhiteSpace(Field("code_away")) ||
                string.IsNullOrWhiteSpace(Field("name_home")) ||
                string.IsNullOrWhiteSpace(Field("name_away")))
            {
                continue;
            }

            var sourceGameId = $"euroleagueR:{seasonCode}:{Field("code")}";
            games[sourceGameId] = new BasketballProviderGame(
                Source,
                sourceGameId,
                gameDate.UtcDateTime,
                "finished",
                $"euroleagueR-team:{Field("code_home")}",
                Field("name_home"),
                $"euroleagueR-team:{Field("code_away")}",
                Field("name_away"),
                homeScore,
                awayScore,
                new BasketballProviderGameProvenance(
                    ResultsUrl,
                    seasonCode,
                    fetchedAtUtc,
                    ParserVersion,
                    $"{seasonCode}:{endYear}"),
                CompetitionPhase: Field("phase_type_name"),
                CompetitionRound: Field("round_name"));
        }

        if (games.Count == 0)
        {
            warnings.Add($"euroleagueR did not expose any complete finished records for {seasonCode}.");
        }

        return games.Values
            .OrderBy(game => game.GameDateTimeUtc)
            .ThenBy(game => game.SourceGameId, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsTrue(string value) =>
        value.Equals("TRUE", StringComparison.OrdinalIgnoreCase) || value == "1";

    private static (int StartYear, int EndYear) ParseSeason(string season)
    {
        var parts = season.Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 ||
            !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var startYear) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var endYear) ||
            endYear != startYear + 1)
        {
            throw new ArgumentException($"Season '{season}' must be a full two-year label.", nameof(season));
        }

        return (startYear, endYear);
    }
}
