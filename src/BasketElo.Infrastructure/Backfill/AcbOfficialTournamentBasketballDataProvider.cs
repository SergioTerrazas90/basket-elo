using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BasketElo.Domain.Backfill;
using HtmlAgilityPack;

namespace BasketElo.Infrastructure.Backfill;

/// <summary>
/// Reads the historical final phases exposed by the official ACB site.  The
/// competition page exposes the final, while each finalist's team page exposes
/// the complete historical tournament match list; crawling the bracket teams
/// makes it possible to recover every game in the edition.
/// </summary>
public sealed class AcbOfficialTournamentBasketballDataProvider(HttpClient httpClient) : IBasketballDataProvider
{
    public const string Source = "acb-official-tournaments";
    public const string ParserVersion = "acb-official-tournaments-v1";
    private const string BaseUrl = "https://acb.com";

    public string SourceKey => Source;

    public Task<BasketballProviderLeague?> ResolveLeagueAsync(
        string country,
        string leagueName,
        BackfillExecutionContext context,
        CancellationToken cancellationToken)
    {
        BasketballProviderLeague? league = null;
        if (string.Equals(country, "Spain", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(leagueName, "Spanish Cup", StringComparison.OrdinalIgnoreCase))
            {
                league = new(Source, "COPA_DEL_REY", "Copa del Rey", "ES", "start_year");
            }
            else if (string.Equals(leagueName, "Copa del Generalísimo", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(leagueName, "Copa del Generalisimo", StringComparison.OrdinalIgnoreCase))
            {
                league = new(Source, "COPA_DEL_GENERALISIMO", "Copa del Generalísimo", "ES", "end_year");
            }
            else if (string.Equals(leagueName, "Supercopa ACB", StringComparison.OrdinalIgnoreCase))
            {
                league = new(Source, "SUPERCOPA_ENDESA", "Supercopa ACB", "ES", "start_year");
            }
        }

        return Task.FromResult(league);
    }

    public async Task<(IReadOnlyCollection<BasketballProviderGame> Games, bool HasMorePages, IReadOnlyCollection<string> Warnings)> GetGamesAsync(
        BasketballProviderLeague league,
        string season,
        BackfillExecutionContext context,
        CancellationToken cancellationToken)
    {
        var kind = league.SourceLeagueId.ToUpperInvariant() switch
        {
            "COPA_DEL_REY" => TournamentKind.CopaDelRey,
            "COPA_DEL_GENERALISIMO" => TournamentKind.CopaDelGeneralisimo,
            "SUPERCOPA_ENDESA" => TournamentKind.Supercopa,
            _ => throw new InvalidOperationException($"Unsupported ACB tournament '{league.SourceLeagueId}'.")
        };
        var (startYear, endYear) = ParseSeason(season);
        if (!IsPlayed(kind, startYear))
        {
            return ([], false, [$"No {kind} edition was played in {season}."]);
        }

        if (kind == TournamentKind.CopaDelGeneralisimo)
        {
            return await GetGeneralissimoGamesAsync(season, endYear, context, cancellationToken);
        }

        var editionId = kind == TournamentKind.CopaDelGeneralisimo ? endYear - 1935 : startYear - 1935;
        var competitionId = kind is TournamentKind.CopaDelRey or TournamentKind.CopaDelGeneralisimo ? 2 : 3;
        var path = competitionId == 2 ? "copa-del-rey" : "supercopa";
        var indexUrl = $"{BaseUrl}/es/{path}/partidos?temporada={editionId}&competicion={competitionId}";
        var warnings = new List<string>();
        var games = new Dictionary<string, BasketballProviderGame>(StringComparer.OrdinalIgnoreCase);
        var verifiedGameIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var teams = new Queue<string>();
        var visitedTeams = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var indexHtml = await FetchAsync(indexUrl, context, cancellationToken);
        if (indexHtml is null)
        {
            return ([], false, [$"Official ACB tournament page was not found: {indexUrl}"]);
        }

        // The competition route is client-rendered and sends its match list as
        // RSC data. Its team directory is server-rendered, so use those club
        // links as seeds and let the historical pages reveal the bracket.
        foreach (var teamUrl in ParseTeamUrls(indexHtml))
        {
            teams.Enqueue(teamUrl);
        }

        while (teams.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var teamUrl = teams.Dequeue();
            if (!visitedTeams.Add(teamUrl))
            {
                continue;
            }

            if (!context.CanUseRequest())
            {
                warnings.Add($"Request budget reached after visiting {visitedTeams.Count} tournament team pages.");
                break;
            }

            var historicalUrl = $"{BaseUrl}{teamUrl}?categoria=played_matches&editionId={editionId}&filtro=temporada";
            var html = await FetchAsync(historicalUrl, context, cancellationToken);
            if (html is null)
            {
                continue;
            }

            foreach (var teamLink in ParseTeamUrls(html))
            {
                if (!visitedTeams.Contains(teamLink))
                {
                    teams.Enqueue(teamLink);
                }
            }

            var parsedCards = ParseMatchCards(html).ToList();
            foreach (var card in parsedCards)
            {
                if (!TryParseMatch(card, season, startYear, endYear, kind, historicalUrl, out var game))
                {
                    continue;
                }

                if (!verifiedGameIds.Add(game!.SourceGameId))
                {
                    continue;
                }

                var liveUrl = game.Provenance?.SourceUrl;
                if (string.IsNullOrWhiteSpace(liveUrl) || !context.CanUseRequest())
                {
                    continue;
                }

                var liveHtml = await FetchAsync(liveUrl, context, cancellationToken);
                if (IsExactSeason(liveHtml, startYear, endYear))
                {
                    games[game.SourceGameId] = game;
                }
            }
        }

        return (games.Values.OrderBy(x => x.GameDateTimeUtc).ThenBy(x => x.SourceGameId).ToArray(), false, warnings);
    }

    private async Task<(IReadOnlyCollection<BasketballProviderGame> Games, bool HasMorePages, IReadOnlyCollection<string> Warnings)> GetGeneralissimoGamesAsync(
        string season, int editionYear, BackfillExecutionContext context, CancellationToken cancellationToken)
    {
        var pageUrl = $"https://es.wikipedia.org/wiki/Copa_del_General%C3%ADsimo_de_baloncesto_{editionYear}";
        var html = await FetchAsync(pageUrl, context, cancellationToken);
        if (html is null) return ([], false, [$"Wikipedia cup season page was not found: {pageUrl}"]);

        var document = new HtmlDocument();
        document.LoadHtml(html);
        var text = Regex.Replace(CleanText(document.DocumentNode.InnerText), @"\s+", " ");
        var finalDate = ExtractFinalDate(text, editionYear);
        var warnings = new List<string> { $"Historical bracket dates are inferred from the published season page: {pageUrl}." };
        var games = new List<BasketballProviderGame>();
        var bracketTables = document.DocumentNode.SelectNodes("//table[contains(@class,'wikitable')]")?
            .Where(table => CleanText(table.SelectSingleNode(".//tr[1]")?.InnerText ?? string.Empty).Contains("Equipo 1", StringComparison.OrdinalIgnoreCase))
            .ToList() ?? [];
        warnings.Add($"Detected {bracketTables.Count} historical bracket table(s).");

        for (var tableIndex = 0; tableIndex < bracketTables.Count; tableIndex++)
        {
            var roundDate = finalDate.AddDays(-7 * (bracketTables.Count - tableIndex));
            var rows = bracketTables[tableIndex].SelectNodes(".//tr")?.Skip(1) ?? [];
            var rowIndex = 0;
            foreach (var row in rows)
            {
                var cells = row.SelectNodes("./th|./td")?.ToList() ?? [];
                if (cells.Count < 4) continue;
                var teams = cells.Select(ExtractTeam).Where(team => team is not null).Select(team => team!.Value).ToList();
                var parsedScores = cells.Select(cell => ParseScore(CleanText(cell.InnerText))).Where(score => score is not null).Select(score => score!.Value).ToList();
                var scores = parsedScores.Count > 2 ? parsedScores.TakeLast(2).ToList() : parsedScores;
                var home = teams.Count > 0 ? teams[0] : ((string Id, string Name)?)null;
                var away = teams.Count > 1 ? teams[1] : ((string Id, string Name)?)null;
                var firstLeg = scores.Count > 0 ? scores[0] : ((short Home, short Away)?)null;
                var secondLeg = scores.Count > 1 ? scores[1] : ((short Home, short Away)?)null;
                if (home is null || away is null) continue;
                if (firstLeg is not null) games.Add(CreateHistoricalCupGame(season, $"{editionYear}-r{tableIndex + 1}-{rowIndex + 1}-1", roundDate, home.Value, away.Value, firstLeg.Value, pageUrl));
                if (secondLeg is not null) games.Add(CreateHistoricalCupGame(season, $"{editionYear}-r{tableIndex + 1}-{rowIndex + 1}-2", roundDate.AddDays(7), home.Value, away.Value, secondLeg.Value, pageUrl));
                rowIndex++;
            }
        }

        var finalCells = document.DocumentNode.SelectNodes("//table")?
            .SelectMany(table => table.SelectNodes(".//tr") ?? Enumerable.Empty<HtmlNode>())
            .Select(row => row.SelectNodes("./th|./td")?.ToList() ?? [])
            .FirstOrDefault(cells => cells.Count >= 4 && Regex.IsMatch(CleanText(string.Join(" ", cells.Select(cell => cell.InnerText))), @"\b\d{1,2}\s+de\s+[^\d]+\s+de\s+\d{4}\b", RegexOptions.IgnoreCase) && cells.Select(cell => ParseScore(CleanText(cell.InnerText))).Any(score => score is not null));
        if (finalCells is not null)
        {
            var finalTeams = finalCells.Select(ExtractTeam).Where(team => team is not null).Select(team => team!.Value).ToList();
            var finalScores = finalCells.Select(cell => ParseScore(CleanText(cell.InnerText))).Where(score => score is not null).Select(score => score!.Value).ToList();
            var home = finalTeams.Count > 0 ? finalTeams[0] : ((string Id, string Name)?)null;
            var away = finalTeams.Count > 1 ? finalTeams[1] : ((string Id, string Name)?)null;
            var score = finalScores.Count > 0 ? ((short Home, short Away)?)finalScores[^1] : null;
            if (home is not null && away is not null && score is not null)
                games.Add(CreateHistoricalCupGame(season, $"{editionYear}-final", finalDate, home.Value, away.Value, score.Value, pageUrl));
        }

        warnings.Add($"Parsed {games.Count} historical cup game(s) from the published bracket.");
        return (games.OrderBy(x => x.GameDateTimeUtc).ThenBy(x => x.SourceGameId).ToArray(), false, warnings);
    }

    private static BasketballProviderGame CreateHistoricalCupGame(string season, string sourceGameId, DateTime date,
        (string Id, string Name) home, (string Id, string Name) away, (short Home, short Away) score, string sourceUrl) => new(
        Source, sourceGameId, DateTime.SpecifyKind(date, DateTimeKind.Utc), "finished", home.Id, home.Name, away.Id, away.Name,
        score.Home, score.Away, new BasketballProviderGameProvenance(sourceUrl, season, DateTime.UtcNow, ParserVersion, sourceGameId),
        CompetitionPhase: "Final phase");

    private static (string Id, string Name)? ExtractTeam(HtmlNode cell)
    {
        var cellText = CleanText(cell.InnerText);
        if (ParseScore(cellText) is not null || Regex.IsMatch(cellText, @"\b\d{1,2}\s+de\s+[^\d]+\s+de\s+\d{4}\b", RegexOptions.IgnoreCase)) return null;
        var anchor = cell.SelectSingleNode(".//a");
        var name = CleanText(anchor?.InnerText ?? cell.InnerText);
        if (string.IsNullOrWhiteSpace(name)) return null;
        var id = anchor?.GetAttributeValue("href", string.Empty).Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()
            ?? Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        return (id, name);
    }

    private static (short Home, short Away)? ParseScore(string text)
    {
        var normalizedMatch = Regex.Match(text, @"(?<!\d)(\d{1,3})\s*[-\u2013\u2014]\s*(\d{1,3})(?!\d)");
        if (normalizedMatch.Success && short.TryParse(normalizedMatch.Groups[1].Value, out var normalizedHome) && short.TryParse(normalizedMatch.Groups[2].Value, out var normalizedAway))
            return (normalizedHome, normalizedAway);
        var match = Regex.Match(text, @"(?<!\d)(\d{1,3})\s*[–-]\s*(\d{1,3})(?!\d)");
        return match.Success && short.TryParse(match.Groups[1].Value, out var home) && short.TryParse(match.Groups[2].Value, out var away)
            ? (home, away) : null;
    }

    private static DateTime ExtractFinalDate(string text, int editionYear)
    {
        var match = Regex.Match(text, @"(?:final se disputó|final tuvo lugar).*?el\s+(\d{1,2})\s+de\s+([a-záéíóú]+)\s+de\s+(\d{4})", RegexOptions.IgnoreCase);
        if (match.Success && DateTime.TryParseExact($"{match.Groups[1].Value} {match.Groups[2].Value} {match.Groups[3].Value}", "d MMMM yyyy", new CultureInfo("es-ES"), DateTimeStyles.None, out var date)) return date;
        return new DateTime(editionYear, 6, 30);
    }

    private async Task<string?> FetchAsync(string url, BackfillExecutionContext context, CancellationToken cancellationToken)
    {
        if (!context.CanUseRequest())
        {
            return null;
        }

        context.ConsumeRequest();
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("BasketElo historical-ingest/1.0");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    internal static IReadOnlyCollection<string> ParseTeamUrls(string html)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        return document.DocumentNode
            .SelectNodes("//a[contains(@href,'/copa-del-rey/equipos/') or contains(@href,'/supercopa/equipos/')]")?
            .Select(node => NormalizeTeamUrl(node.GetAttributeValue("href", string.Empty)))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];
    }

    private static IEnumerable<HtmlNode> ParseMatchCards(string html)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        return ((IEnumerable<HtmlNode>?)document.DocumentNode.SelectNodes("//div[contains(@class,'matchCard')]") ?? Array.Empty<HtmlNode>())
            .Where(card => card.SelectSingleNode(".//a[contains(@href,'/partidos/')]") is not null);
    }

    public static bool TryParseMatch(
        HtmlNode card,
        string season,
        int startYear,
        int endYear,
        TournamentKind kind,
        string sourceUrl,
        out BasketballProviderGame? game)
    {
        game = null;
        var dateText = CleanText(card.SelectSingleNode(".//*[contains(@class,'dateText')]")?.InnerText ?? string.Empty);
        if (!TryParseDate(dateText, startYear, endYear, out var date))
        {
            return false;
        }

        var teams = card.SelectNodes(".//a[contains(@href,'/equipos/')]")?
            .Take(2)
            .Select(anchor =>
            {
                var href = anchor.GetAttributeValue("href", string.Empty).Split('?', 2)[0];
                var imageName = anchor.SelectSingleNode(".//img[@alt]")?.GetAttributeValue("alt", string.Empty) ?? string.Empty;
                var name = CleanText(imageName).Replace(" logo", string.Empty, StringComparison.OrdinalIgnoreCase);
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = CleanText(anchor.SelectSingleNode(".//*[contains(@class,'teamName')]")?.InnerText ?? href.Split('/').Last());
                }

                return (Url: href, Id: href.Split('/').Last(), Name: name);
            })
            .ToArray();
        var scores = card.SelectNodes(".//p[contains(@class,'scoreNumber')]")?
            .Take(2)
            .Select(node =>
            {
                var match = Regex.Match(CleanText(node.InnerText), "\\d+");
                return short.TryParse(match.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? (short?)value : null;
            })
            .ToArray();
        var liveUrl = card.SelectSingleNode(".//a[contains(@href,'/partidos/')]")?.GetAttributeValue("href", string.Empty);
        var matchSlug = liveUrl?.Split('/', StringSplitOptions.RemoveEmptyEntries).Reverse().Skip(1).FirstOrDefault();
        var sourceGameId = matchSlug?.Split('-').LastOrDefault();
        if (string.IsNullOrWhiteSpace(matchSlug) || matchSlug.StartsWith("-vs--", StringComparison.Ordinal))
        {
            return false;
        }
        if (teams is null || teams.Length < 2 || scores is null || scores.Length < 2 ||
            scores.Any(x => !x.HasValue) || string.IsNullOrWhiteSpace(sourceGameId))
        {
            return false;
        }

        var revision = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(card.OuterHtml))).ToLowerInvariant();
        game = new BasketballProviderGame(
            Source,
            sourceGameId,
            DateTime.SpecifyKind(date, DateTimeKind.Utc),
            "finished",
            teams[0].Id,
            teams[0].Name,
            teams[1].Id,
            teams[1].Name,
            scores[0],
            scores[1],
            new BasketballProviderGameProvenance(liveUrl ?? sourceUrl, season, DateTime.UtcNow, ParserVersion, revision),
            CompetitionPhase: kind == TournamentKind.CopaDelRey ? "Final phase" : "Tournament");
        return true;
    }

    private static bool TryParseDate(string text, int startYear, int endYear, out DateTime date)
    {
        date = default;
        var parts = CleanText(text).ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3 || !int.TryParse(parts[^2], out var day))
        {
            return false;
        }

        var month = parts[^1] switch
        {
            "ENERO" => 1,
            "FEBRERO" => 2,
            "MARZO" => 3,
            "ABRIL" => 4,
            "MAYO" => 5,
            "JUNIO" => 6,
            "JULIO" => 7,
            "AGOSTO" => 8,
            "SEPTIEMBRE" => 9,
            "OCTUBRE" => 10,
            "NOVIEMBRE" => 11,
            "DICIEMBRE" => 12,
            _ => 0
        };
        if (month == 0)
        {
            return false;
        }

        var year = month >= 9 ? startYear : endYear;
        return DateTime.TryParseExact($"{day:00}-{month:00}-{year}", "dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    private static (int StartYear, int EndYear) ParseSeason(string season)
    {
        var canonical = SeasonLabelNormalizer.ToFullSeasonLabel(season).Split('-');
        if (canonical.Length != 2 || !int.TryParse(canonical[0], out var start) || !int.TryParse(canonical[1], out var end) || end != start + 1)
        {
            throw new ArgumentException($"Invalid ACB tournament season '{season}'.", nameof(season));
        }

        return (start, end);
    }

    private static bool IsPlayed(TournamentKind kind, int startYear) => kind switch
    {
        TournamentKind.CopaDelRey => startYear is >= 1983 and <= 2007,
        TournamentKind.CopaDelGeneralisimo => startYear is >= 1939 and <= 1975,
        _ => startYear is >= 1984 and <= 1987 or >= 2004 and <= 2007
    };

    private static string CleanText(string value) => HtmlEntity.DeEntitize(value).Replace('\u00A0', ' ').Trim();

    private static bool IsExactSeason(string? liveHtml, int startYear, int endYear)
    {
        if (string.IsNullOrWhiteSpace(liveHtml))
        {
            return false;
        }

        var document = new HtmlDocument();
        document.LoadHtml(liveHtml);
        var dateText = document.DocumentNode.SelectSingleNode("//meta[@name='customTagDate']")?.GetAttributeValue("content", string.Empty);
        var yearMatch = Regex.Match(dateText ?? string.Empty, "\\b(19|20)\\d{2}\\b");
        return yearMatch.Success && int.TryParse(yearMatch.Value, out var year) && (year == startYear || year == endYear);
    }

    private static string NormalizeTeamUrl(string href)
    {
        var clean = href.Split('?', 2)[0];
        var parts = clean.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 4 ? $"/{parts[0]}/{parts[1]}/{parts[2]}/{parts[3]}" : clean;
    }

    public enum TournamentKind
    {
        CopaDelRey,
        CopaDelGeneralisimo,
        Supercopa
    }
}
