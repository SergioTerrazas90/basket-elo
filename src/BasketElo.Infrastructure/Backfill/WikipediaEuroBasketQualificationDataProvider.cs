using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BasketElo.Domain.Backfill;
using HtmlAgilityPack;

namespace BasketElo.Infrastructure.Backfill;

/// <summary>
/// Imports EuroBasket qualification game boxes from Wikipedia for cycles whose
/// FIBA archive entry does not expose a complete usable game list.
/// </summary>
public sealed class WikipediaEuroBasketQualificationDataProvider(HttpClient httpClient) : IBasketballDataProvider
{
    public const string Source = "wikipedia";
    public const string ParserVersion = "wikipedia-eurobasket-qualification-html-v1";
    private static readonly IReadOnlyDictionary<int, (string Path, string Url)> Pages =
        new Dictionary<int, (string, string)>
        {
            [1991] = ("/wiki/FIBA_EuroBasket_1991_qualification", "https://en.wikipedia.org/wiki/FIBA_EuroBasket_1991_qualification"),
            [1993] = ("/wiki/FIBA_EuroBasket_1993_qualification", "https://en.wikipedia.org/wiki/FIBA_EuroBasket_1993_qualification")
        };

    public string SourceKey => Source;

    public Task<BasketballProviderLeague?> ResolveLeagueAsync(
        string country,
        string leagueName,
        BackfillExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (!country.Equals("Europe", StringComparison.OrdinalIgnoreCase) ||
            (!leagueName.Equals("EuroBasket Qualifiers", StringComparison.OrdinalIgnoreCase) &&
             !leagueName.Equals("EuroBasket 1991 Qualification", StringComparison.OrdinalIgnoreCase)))
        {
            return Task.FromResult<BasketballProviderLeague?>(null);
        }

        return Task.FromResult<BasketballProviderLeague?>(
            new BasketballProviderLeague(
                Source,
                "fiba-eurobasket-qualification",
                "EuroBasket Qualifiers",
                "EUR",
                "year"));
    }

    public async Task<(IReadOnlyCollection<BasketballProviderGame> Games, bool HasMorePages, IReadOnlyCollection<string> Warnings)> GetGamesAsync(
        BasketballProviderLeague league,
        string season,
        BackfillExecutionContext context,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var year = ParseStartYear(season);
        if (!Pages.TryGetValue(year, out var page))
        {
            warnings.Add($"Wikipedia source is not configured for the {season} EuroBasket qualification cycle.");
            return ([], false, warnings);
        }

        if (!context.CanUseRequest())
        {
            warnings.Add($"Wikipedia request budget reached before {page.Path} could be fetched.");
            return ([], false, warnings);
        }

        context.ConsumeRequest();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var response = await httpClient.GetAsync(page.Path, timeout.Token);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        var fetchedAtUtc = DateTime.UtcNow;
        var revision = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(html)))[..16];
        var games = ParseGames(html, year, page.Url, fetchedAtUtc, revision, warnings);

        if (games.Count > 0)
        {
            warnings.Add("Wikipedia provides the match dates but not tip-off times; imported times are 00:00 UTC.");
        }

        return (games, false, warnings);
    }

    private static List<BasketballProviderGame> ParseGames(
        string html,
        int year,
        string pageUrl,
        DateTime fetchedAtUtc,
        string revision,
        ICollection<string> warnings)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        var metadataNodes = document.DocumentNode
            .SelectNodes("//div[@data-mw and contains(@data-mw, 'basketballbox')]")?
            .ToList() ?? [];
        var games = new List<BasketballProviderGame>(metadataNodes.Count);
        var ordinal = 0;
        var missingDateCount = 0;

        foreach (var metadataNode in metadataNodes)
        {
            ordinal++;
            try
            {
                var data = metadataNode.GetAttributeValue("data-mw", string.Empty);
                using var json = JsonDocument.Parse(data);
                var parameters = json.RootElement
                    .GetProperty("parts")[0]
                    .GetProperty("template")
                    .GetProperty("params");

                var dateText = GetTemplateValue(parameters, "date");
                var teamAValue = GetTemplateValue(parameters, "teamA");
                var teamBValue = GetTemplateValue(parameters, "teamB");
                var homeCode = ExtractTeamCode(teamAValue);
                var awayCode = ExtractTeamCode(teamBValue);
                var homeScore = ParseScore(GetTemplateValue(parameters, "scoreA"));
                var awayScore = ParseScore(GetTemplateValue(parameters, "scoreB"));
                if (homeCode is null || awayCode is null || homeScore is null || awayScore is null)
                {
                    warnings.Add($"Wikipedia basketballbox {ordinal} could not be parsed and was skipped.");
                    continue;
                }

                if (!DateTime.TryParse(
                        dateText,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AllowWhiteSpaces,
                        out var gameDate))
                {
                    // The article omits the calendar date on part of the
                    // challenge round. Keep those score boxes rather than
                    // dropping games, using source order only for a stable
                    // date fallback that can be inspected later.
                    gameDate = new DateTime(1990, 1, 1).AddDays(ordinal);
                    missingDateCount++;
                }

                var renderedGame = metadataNode.SelectSingleNode("following-sibling::div[1]");
                var teamNames = ExtractTeamNames(renderedGame, homeCode, awayCode);
                var sourceGameId = BuildSourceGameId(year, ordinal, gameDate, homeCode, awayCode, homeScore.Value, awayScore.Value);
                var phase = FindHeading(metadataNode, "h2");
                var round = FindHeading(metadataNode, "h3");

                games.Add(new BasketballProviderGame(
                    Source,
                    sourceGameId,
                    DateTime.SpecifyKind(gameDate.Date, DateTimeKind.Utc),
                    "finished",
                    homeCode,
                    teamNames.Home,
                    awayCode,
                    teamNames.Away,
                    homeScore,
                    awayScore,
                    new BasketballProviderGameProvenance(
                        pageUrl,
                        year.ToString(CultureInfo.InvariantCulture),
                        fetchedAtUtc,
                        ParserVersion,
                        revision),
                    CompetitionPhase: phase,
                    CompetitionRound: round,
                    SourceHomeTeamCountryCode: homeCode,
                    SourceAwayTeamCountryCode: awayCode));
            }
            catch (JsonException exception)
            {
                warnings.Add($"Wikipedia basketballbox {ordinal} contained invalid embedded metadata and was skipped ({exception.Message}).");
            }
        }

        if (missingDateCount > 0)
        {
            warnings.Add($"Wikipedia omitted dates for {missingDateCount} match boxes; those games use a deterministic 1990 source-order date fallback.");
        }

        return games;
    }

    private static string GetTemplateValue(JsonElement parameters, string name)
        => parameters.TryGetProperty(name, out var value) && value.TryGetProperty("wt", out var wt)
            ? wt.GetString() ?? string.Empty
            : string.Empty;

    private static string? ExtractTeamCode(string value)
    {
        var match = Regex.Match(value, @"\{\{Bk\|\s*(?<code>[A-Za-z]{3})(?:\|[^}]*)?\}\}", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["code"].Value.ToUpperInvariant() : null;
    }

    private static short? ParseScore(string value)
    {
        var match = Regex.Match(value, @"\b(?<score>\d{1,3})\b");
        return match.Success && short.TryParse(match.Groups["score"].Value, out var score) ? score : null;
    }

    private static (string Home, string Away) ExtractTeamNames(HtmlNode? renderedGame, string homeCode, string awayCode)
    {
        var names = renderedGame?
            .SelectNodes(".//a[contains(@href, 'national_basketball_team')]")?
            .Select(x => HtmlEntity.DeEntitize(x.InnerText).Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList() ?? [];

        return (
            names.ElementAtOrDefault(0) ?? homeCode,
            names.ElementAtOrDefault(1) ?? awayCode);
    }

    private static string? FindHeading(HtmlNode node, string tagName)
    {
        var heading = node.SelectSingleNode($"preceding::{tagName}[1]");
        var text = heading is null ? null : HtmlEntity.DeEntitize(heading.InnerText).Trim();
        return string.IsNullOrWhiteSpace(text) ? null : Regex.Replace(text, @"\s+", " ");
    }

    private static string BuildSourceGameId(int year, int ordinal, DateTime date, string homeCode, string awayCode, short homeScore, short awayScore)
    {
        var value = $"{year}|{ordinal}|{date:yyyy-MM-dd}|{homeCode}|{awayCode}|{homeScore}|{awayScore}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..16].ToLowerInvariant();
        return $"wiki-eurobasket-{year}-{hash}";
    }

    private static int ParseStartYear(string season)
    {
        var match = Regex.Match(season, @"\b(19|20)\d{2}\b");
        return match.Success && int.TryParse(match.Value, out var year)
            ? year
            : throw new ArgumentException($"Wikipedia season '{season}' has no four-digit year.", nameof(season));
    }
}
