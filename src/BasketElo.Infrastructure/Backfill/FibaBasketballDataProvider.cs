Exit code: 0
Wall time: 0.4 seconds
Total output lines: 1127
Output:
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BasketElo.Domain.Backfill;
using HtmlAgilityPack;

namespace BasketElo.Infrastructure.Backfill;

/// <summary>
/// Reads the official FIBA history pages. The archive page is server-rendered and
/// exposes stable game links, dates, phase labels and final scores in its HTML.
/// </summary>
public sealed class FibaBasketballDataProvider(HttpClient httpClient) : IBasketballDataProvider
{
    public const string Source = "fiba";
    public const string ParserVersion = "fiba-history-html-v1";

    public static string? CountryCodeFromTeamId(string? sourceTeamId)
    {
        if (string.IsNullOrWhiteSpace(sourceTeamId))
        {
            return null;
        }

        var normalized = sourceTeamId.Trim().ToUpperInvariant();
        return normalized.Length is > 0 and <= 3 && normalized.All(char.IsLetter)
            ? normalized
            : null;
    }

    private static readonly IReadOnlyDictionary<string, (string Family, string Name, string? CountryCode)> Catalog =
        new Dictionary<string, (string, string, string?)>(StringComparer.OrdinalIgnoreCase)
        {
            ["Europe:FIBA EuroBasket"] = ("208-fiba-eurobasket", "FIBA EuroBasket", "EUR"),
            ["Europe:FIBA European Champions Cup"] = ("112-fiba-mens-european-club-competitions-tier-1", "FIBA European Champions Cup / EuroLeague predecessor", "EUR"),
            ["Europe:FIBA Saporta Cup"] = ("212-fiba-mens-european-club-competitions-tier-2", "FIBA Saporta Cup / European Cup Winners' Cup predecessor", "EUR"),
            ["Europe:FIBA European Tier 2"] = ("212-fiba-mens-european-club-competitions-tier-2|post-saporta", "FIBA European Tier 2 / FIBA Europe League / EuroCup lineage", "EUR"),
            ["Europe:FIBA Korac Cup"] = ("164-eurocup-challenge", "FIBA Korac Cup / European Cup Radivoj Korac", "EUR"),
            // Keep the SuproLeague alias distinct from the Champions Cup alias even
            // though FIBA publishes both editions in the same history family.
            ["Europe:FIBA SuproLeague"] = ("112-fiba-mens-european-club-competitions-tier-1|suproleague", "FIBA SuproLeague", "EUR"),
            ["Europe:FIBA EuroBasket Pre-Qualifiers"] = ("204-fiba-eurobasket-pre-qualifiers", "FIBA EuroBasket Pre-Qualifiers", "EUR"),
            ["Europe:FIBA EuroBasket Qualifiers"] = ("205-fiba-eurobasket-qualifiers", "FIBA EuroBasket Qualifiers", "EUR"),
            ["Europe:EuroBasket Qualifiers"] = ("205-fiba-eurobasket-qualifiers", "FIBA EuroBasket Qualifiers", "EUR"),
            ["Europe:FIBA EuroBasket Division B"] = ("206-fiba-eurobasket-division-b", "FIBA EuroBasket Division B", "EUR"),
            ["Africa:FIBA AfroBasket"] = ("179-fiba-afrobasket", "FIBA AfroBasket", "AFR"),
            ["Africa:FIBA AfroBasket Qualifiers"] = ("178-fiba-afrobasket-qualifiers", "FIBA AfroBasket Qualifiers", "AFR"),
            ["Africa:FIBA AfroBasket Pre-Qualifiers"] = ("178-fiba-afrobasket-qualifiers|pre-qualifiers", "FIBA AfroBasket Pre-Qualifiers", "AFR"),
            ["Asia:FIBA Asia Cup"] = ("195-fiba-asia-cup", "FIBA Asia Cup", "ASI"),
            ["Americas:FIBA AmeriCup Qualifiers"] = ("183-fiba-americup-qualifiers", "FIBA AmeriCup Qualifiers", "AME"),
            ["Americas:FIBA AmeriCup Pre-Qualifiers"] = ("182-fiba-americup-pre-qualifiers", "FIBA AmeriCup Pre-Qualifiers", "AME"),
            ["Americas:FIBA Americas Championship"] = ("184-fiba-americup", "FIBA Americas Championship", "AME"),
            ["Americas:Centrobasket Championship"] = ("122-centrobasket-championship", "Centrobasket Championship", "AME"),
            ["Americas:COCABA Championship"] = ("113-cbc-championship|cocaba", "COCABA Championship", "AME"),
            ["Americas:South American Championship"] = ("327-south-american-championship", "South American Championship", "AME"),
            ["Americas:Caribbean Basketball Championship"] = ("113-cbc-championship|caribbean", "Caribbean Basketball Championship", "AME"),
            ["Asia:FIBA Asia Cup Qualifiers"] = ("192-fiba-asia-cup-qualifiers", "FIBA Asia Cup Qualifiers", "ASI"),
            ["Oceania:FIBA Oceania Championship"] = ("216-fiba-oceania-championship", "FIBA Oceania Championship", "OCE"),
            ["World:FIBA Basketball World Cup"] = ("201-fiba-basketball-world-cup", "FIBA Basketball World Cup", "WOR"),
            ["World:FIBA Basketball World Cup Qualifiers"] = ("200-fiba-basketball-world-cup-qualifiers", "FIBA Basketball World Cup Qualifiers", "WOR"),
            ["World:FIBA Olympic Qualifying Tournament"] = ("219-fiba-olympic-qualifying-tournament", "FIBA Olympic Qualifying Tournament", "WOR"),
            ["World:FIBA Olympic Pre-Qualifying Tournament"] = ("218-fiba-olympic-pre-qualifying-tournament", "FIBA Olympic Pre-Qualifying Tournament", "WOR"),
            ["World:Olympic Qualifying Tournament"] = ("324-olympic-qualifying-tournament", "Olympic Qualifying Tournament", "WOR"),
            ["World:FIBA Men's Olympic Basketball Tournament"] = ("320-mens-olympic-basketball-tournament", "FIBA Men's Olympic Basketball Tournament", "WOR")
        };

    public string SourceKey => Source;

    public Task<BasketballProviderLeague?> ResolveLeagueAsync(
        string country,
        string leagueName,
        BackfillExecutionContext context,
        CancellationToken cancellationToken)
    {
        Catalog.TryGetValue($"{country}:{leagueName}", out var entry);
        return Task.FromResult<BasketballProviderLeague?>(
            entry.Family is null
                ? null
                : new BasketballProviderLeague(Source, entry.Family, entry.Name, entry.CountryCode, "year"));
    }

    public async Task<(IReadOnlyCollection<BasketballProviderGame> Games, bool HasMorePages, IReadOnlyCollection<string> Warnings)> GetGamesAsync(
        BasketballProviderLeague league,
        string season,
        BackfillExecutionContext context,
        CancellationToken cancellationToken)
    {
        var year = ParseStartYear(season);
        var warnings = new List<string>();
        var (historyFamily, variant) = ParseFamily(league.SourceLeagueId);
        var historyPath = $"/en/history/{historyFamily}";
        var archiveYear = IsEuropeanClubCompetition(historyFamily) ? year + 1 : year;
        var editionPaths = KnownEditionPaths(historyFamily, variant, year);
        if (editionPaths is null)
        {
            var history = await GetPageAsync(historyPath, context, cancellationToken);
            editionPaths = FindEditionPaths(history.Content, historyFamily, archiveYear);
        }

        if (editionPaths.Count == 0)
        {
            warnings.Add($"FIBA edition {year} was not found in {historyPath}.");
            return ([], false, warnings);
        }

        var games = new List<BasketballProviderGame>();
        foreach (var editionPath in editionPaths)
        {
            if (!context.CanUseRequest())
            {
                warnings.Add($"FIBA request budget reached before edition {editionPath} could be fetched.");
                break;
            }

            var gamesPath = $"{editionPath}/games";
            try
            {
                var gamesPage = await GetPageAsync(gamesPath, context, cancellationToken);
                var parsedGames = ParseGames(gamesPage.Content, gamesPage.FetchedAtUtc, gamesPage.Revision, gamesPath, archiveYear, warnings);
                if (IsEuroBasket2005Event(editionPath))
                {
                    parsedGames = parsedGames
                        .Where(IsEuroBasket2005QualificationGame)
                        .ToArray();
                }

                games.AddRange(parsedGames);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                warnings.Add($"FIBA edition page timed out: {gamesPath}.");
            }
            catch (HttpRequestException exception)
            {
                warnings.Add($"FIBA edition page could not be fetched: {gamesPath} ({exception.StatusCode?.ToString() ?? exception.Message}).");
            }
        }

        // Older Champions Cup editions can expose unresolved cards even when
        // the archive has a complete serialized game payload. Use Wikipedia or
        // Todor66 only for genuinely sparse editions; a rich embedded payload
        // is the authoritative source for the game-level records.
        var hasUnresolvedTeamCards = warnings.Any(warning =>
            warning.Contains("unresolved TBD/TBC", StringComparison.OrdinalIgnoreCase));
        if (IsEuropeanClubCompetition(historyFamily) &&
            !IsEuropeanPostSaportaTierTwo(historyFamily, variant) &&
            (games.Count < 50 || IsEuropeanSaportaCup(historyFamily, variant) && hasUnresolvedTeamCards))
        {
            var wikipediaLanguages = IsEuropeanSaportaCup(historyFamily, variant) || IsEuropeanKoracCup(historyFamily)
                ? new[] { "en" }
                : year >= 1996 ? new[] { "en", "es" } : new[] { "es" };
            var wikipediaGames = new List<BasketballProviderGame>();
            foreach (var language in wikipediaLanguages)
            {
                var languageGames = await GetWikipediaGamesAsync(
                    season,
                    language,
                    IsEuropeanSaportaCup(historyFamily, variant),
                    IsEuropeanKoracCup(historyFamily),
                    context,
                    cancellationToken,
                    warnings);
                if (languageGames.Count > wikipediaGames.Count)
                {
                    wikipediaGames = languageGames.ToList();
                }
            }

            if (year <= 1990 && !IsEuropeanSaportaCup(historyFamily, variant) && !IsEuropeanKoracCup(historyFamily))
            {
                var todorGames = await GetTodor66GamesAsync(
                    season,
                    context,
                    cancellationToken,
                    warnings);
                if (todorGames.Count > wikipediaGames.Count)
                {
                    wikipediaGames = todorGames.ToList();
                }
            }

            if (wikipediaGames.Count > games.Count)
            {
                warnings.Add($"FIBA archive was sparse or incomplete ({games.Count} games); used the richest external score table ({wikipediaGames.Count} games).");
                games = wikipediaGames;
            }
            else if (wikipediaGames.Count > 0 && hasUnresolvedTeamCards && year >= 1996)
            {
                var officialKnockoutGames = games.Where(game => !IsGroupStage(game)).ToList();
                games = wikipediaGames.Concat(officialKnockoutGames)
                    .GroupBy(game => game.SourceGameId, StringComparer.Ordinal)
                    .Select(group => group.First())
                    .ToList();
                warnings.Add("FIBA archive had unresolved group-stage cards; combined the external score table with official knockout records.");
            }
        }

        return (games, false, warnings);
    }

    private async Task<IReadOnlyCollection<BasketballProviderGame>> GetTodor66GamesAsync(
        string season,
        BackfillExecutionContext context,
        CancellationToken cancellationToken,
        ICollection<string> warnings)
    {
        var startYear = ParseStartYear(season);
        var archiveYear = startYear + 1;
        var title = $"Men Basketball European Champions Cup {archiveYear}";
        if (!context.CanUseRequest())
        {
            warnings.Add($"Todor66 request budget reached before {title} could be fetched.");
            return [];
        }

        context.ConsumeRequest();
        var pageUrl = $"http://todor66.com/basketball/Eurocups/Men_CC_{archiveYear}.html";
        try
        {
            using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            requestTimeout.CancelAfter(TimeSpan.FromSeconds(30));
            using var response = await httpClient.GetAsync(pageUrl, requestTimeout.Token);
            response.EnsureSuccessStatusCode();
            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            var revision = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(html)))[..16];
            return WikipediaFibaEuropeanChampionsCupParser.ParseTodor66Games(html, season, pageUrl, DateTime.UtcNow, revision, warnings);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            warnings.Add($"Todor66 edition page timed out: {title}.");
        }
        catch (HttpRequestException exception)
        {
            warnings.Add($"Todor66 edition page could not be fetched: {title} ({exception.StatusCode?.ToString() ?? exception.Message}).");
        }
        return [];
    }

    private async Task<IReadOnlyCollection<BasketballProviderGame>> GetWikipediaGamesAsync(
        string season,
        string language,
        bool saportaCup,
        bool koracCup,
        BackfillExecutionContext context,
        CancellationToken cancellationToken,
        ICollection<string> warnings)
    {
        var startYear = ParseStartYear(season);
        var title = language.Equals("en", StringComparison.OrdinalIgnoreCase)
            ? koracCup
                ? WikipediaFibaEuropeanChampionsCupParser.KoracWikipediaPageTitle(startYear)
                : saportaCup
                ? WikipediaFibaEuropeanChampionsCupParser.SaportaEnglishPageTitle(startYear)
                : WikipediaFibaEuropeanChampionsCupParser.EnglishPageTitle(startYear)
            : WikipediaFibaEuropeanChampionsCupParser.PageTitle(startYear);
        if (!context.CanUseRequest())
        {
            warnings.Add($"Wikipedia request budget reached before {title} could be fetched.");
            return [];
        }

        context.ConsumeRequest();
        var pagePath = $"/w/index.php?title={Uri.EscapeDataString(title)}&action=raw";
        try
        {
            using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            requestTimeout.CancelAfter(TimeSpan.FromSeconds(30));
            var host = language.Equals("en", StringComparison.OrdinalIgnoreCase) ? "en.wikipedia.org" : "es.wikipedia.org";
            using var response = await httpClient.GetAsync("https://" + host + pagePath, requestTimeout.Token);
            response.EnsureSuccessStatusCode();
            var wikitext = await response.Content.ReadAsStringAsync(cancellationToken);
            if (wikitext.Contains("#REDIRECT", StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add($"Wikipedia page was not found: {title}.");
                return [];
            }

            var pageUrl = $"https://{host}/wiki/{Uri.EscapeDataString(title).Replace("%20", "_", StringComparison.Ordinal)}";
            var revision = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(wikitext)))[..16];
            return WikipediaFibaEuropeanChampionsCupParser.ParseGames(wikitext, season, pageUrl, DateTime.UtcNow, revision, warnings);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            warnings.Add($"Wikipedia edition page timed out: {title}.");
        }
        catch (HttpRequestException exception)
        {
            warnings.Add($"Wikipedia edition page could not be fetched: {title} ({exception.StatusCode?.ToString() ?? exception.Message}).");
        }
        return [];
    }

    private static (string Family, string? Variant) ParseFamily(string sourceLeagueId)
    {
        var parts = sourceLeagueId.Split('|', 2, StringSplitOptions.TrimEntries);
        return (parts[0], parts.Length == 2 ? parts[1] : null);
    }

    private static bool IsEuropeanClubCompetition(string family)
        => family.Equals("112-fiba-mens-european-club-competitions-tier-1", StringComparison.OrdinalIgnoreCase) ||
           family.Equals("212-fiba-mens-european-club-competitions-tier-2", StringComparison.OrdinalIgnoreCase) ||
           family.Equals("164-eurocup-challenge", StringComparison.OrdinalIgnoreCase);

    private static bool IsEuropeanSaportaCup(string family, string? variant)
        => family.Equals("212-fiba-mens-european-club-competitions-tier-2", StringComparison.OrdinalIgnoreCase) &&
           string.IsNullOrWhiteSpace(variant);

    private static bool IsEuropeanPostSaportaTierTwo(string family, string? variant)
        => family.Equals("212-fiba-mens-european-club-competitions-tier-2", StringComparison.OrdinalIgnoreCase) &&
           string.Equals(variant, "post-saporta", StringComparison.OrdinalIgnoreCase);

    private static bool IsEuropeanKoracCup(string family)
        => family.Equals("164-eurocup-challenge", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyCollection<string>? KnownEditionPaths(string family, string? variant, int year)
    {
        if (family.Equals("212-fiba-mens-european-club-competitions-tier-2", StringComparison.OrdinalIgnoreCase))
        {
            // The archive's 2002 row also contains the post-Saporta FIBA Europe
            // Champions Cup. The Siena-winning edition is the second 2002 row.
            return year switch
            {
                2001 => ["/en/history/212-fiba-mens-european-club-competitions-tier-2/2175"],
                _ => null
            };
        }

        if (family.Equals("204-fiba-eurobasket-pre-qualifiers", StringComparison.OrdinalIgnoreCase))
        {
            return year switch
            {
                2025 => ["/en/history/204-fiba-eurobasket-pre-qualifiers/208437"],
                2021 => ["/en/history/204-fiba-eurobasket-pre-qualifiers/10909"],
                2003 => ["/en/history/204-fiba-eurobasket-pre-qualifiers/277"],
                2001 =>
                [
                    "/en/history/204-fiba-eurobasket-pre-qualifiers/227",
                    "/en/history/204-fiba-eurobasket-pre-qualifiers/276"
                ],
                1999 =>
                [
                    "/en/history/204-fiba-eurobasket-pre-qualifiers/1783",
                    "/en/history/204-fiba-eurobasket-pre-qualifiers/226"
                ],
                1997 =>
                [
                    "/en/history/204-fiba-eurobasket-pre-qualifiers/1501",
                    "/en/history/204-fiba-eurobasket-pre-qualifiers/1782"
                ],
                1995 => ["/en/history/204-fiba-eurobasket-pre-qualifiers/1285"],
                _ => null
            };
        }

        if (family.Equals("205-fiba-eurobasket-qualifiers", StringComparison.OrdinalIgnoreCase))
        {
            // FIBA labels the individual rounds by the year in which that round
            // was played.  The application season is the EuroBasket tournament
            // year, so some tournament seasons intentionally span two archive
            // editions (for example, 2015 has the 2013 first round and 2014
            // second round).
            return year switch
            {
                2015 =>
                [
                    "/en/history/205-fiba-eurobasket-qualifiers/7399",
                    "/en/history/205-fiba-eurobasket-qualifiers/7258"
                ],
                2013 => ["/en/history/205-fiba-eurobasket-qualifiers/6773"],
                2011 => ["/en/history/205-fiba-eurobasket-qualifiers/5937"],
                2009 => ["/en…3072 tokens truncated…       foreach (Match match in Regex.Matches(html, historyLinkPattern, RegexOptions.IgnoreCase))
        {
            var rowStart = html.LastIndexOf("<tr", match.Index, StringComparison.OrdinalIgnoreCase);
            var rowEnd = html.IndexOf("</tr>", match.Index, StringComparison.OrdinalIgnoreCase);
            if (rowStart >= 0 && rowEnd > rowStart &&
                Regex.IsMatch(html.Substring(rowStart, rowEnd - rowStart), $@"\b{year}\b"))
            {
                AddPath($"/en/history/{family}/{match.Groups["id"].Value}");
            }
        }

        if (paths.Count > 0)
        {
            return paths;
        }

        foreach (Match rowMatch in Regex.Matches(html, @"<tr\b[^>]*>(?<row>.*?)</tr>", RegexOptions.Singleline | RegexOptions.IgnoreCase))
        {
            if (!Regex.IsMatch(rowMatch.Groups["row"].Value, $@"\b{year}\b"))
            {
                continue;
            }

            var historyMatch = Regex.Match(
                rowMatch.Groups["row"].Value,
                $@"(?:https://www\.fiba\.basketball)?/en/history/{Regex.Escape(family)}/(?<id>\d+)",
                RegexOptions.IgnoreCase);
            if (historyMatch.Success)
            {
                AddPath($"/en/history/{family}/{historyMatch.Groups["id"].Value}");
            }
        }

        if (rows.Count > 0)
        {
            return paths;
        }

        // Keep a small regex fallback for archive pages whose history table is rendered
        // through a different semantic wrapper than the current site markup.
        var linkPattern = $@"(?:https://www\.fiba\.basketball)?/en/history/{Regex.Escape(family)}/(?<id>\d+)";
        foreach (Match match in Regex.Matches(html, linkPattern, RegexOptions.IgnoreCase))
        {
            var start = Math.Max(0, match.Index - 400);
            var length = Math.Min(800, html.Length - start);
            if (!Regex.IsMatch(html.Substring(start, length), $@"\b{year}\b"))
            {
                continue;
            }

            AddPath($"/en/history/{family}/{match.Groups["id"].Value}");
        }

        return paths;
    }

    private IReadOnlyCollection<BasketballProviderGame> ParseGames(
        string html,
        DateTime fetchedAtUtc,
        string revision,
        string sourcePath,
        int year,
        ICollection<string> warnings)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        var cards = document.DocumentNode.SelectNodes("//div[@data-testid='ui-game-card']")?.ToList() ?? [];
        if (cards.Count == 0)
        {
            return ParseEmbeddedGames(html, fetchedAtUtc, revision, sourcePath, year, warnings);
        }

        var games = new List<BasketballProviderGame>(cards.Count);
        var fallbackDate = FindCompetitionStartDate(html, year);
        if (fallbackDate is null || fallbackDate.Value.Year < 1900)
        {
            fallbackDate = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        }
        var missingStableLinkCount = 0;
        var missingTeamIdentityCount = 0;
        var unresolvedTeamIdentityCount = 0;

        foreach (var card in cards)
        {
            var anchor = card.SelectSingleNode(".//a[@href and contains(@href, '/games/')]");
            var gamePath = NormalizePath(anchor?.GetAttributeValue("href", string.Empty) ?? string.Empty);
            var slug = gamePath.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
            if (string.IsNullOrWhiteSpace(slug))
            {
                missingStableLinkCount += 1;
                continue;
            }

            var slugParts = slug.Split('-', StringSplitOptions.RemoveEmptyEntries);
            if (slugParts.Length == 0 || !slugParts[0].All(char.IsDigit))
            {
                warnings.Add($"Skipped FIBA game link with an unexpected slug: {slug}.");
                continue;
            }

            var cardTeams = ParseCardTeams(card);
            if (slugParts.Length < 3 && cardTeams is null)
            {
                missingTeamIdentityCount += 1;
                continue;
            }

            var homeCode = slugParts.Length >= 3 ? slugParts[1].ToUpperInvariant() : cardTeams![0].Code;
            var awayCode = slugParts.Length >= 3 ? slugParts[2].ToUpperInvariant() : cardTeams![1].Code;
            if (IsUnresolvedTeamCode(homeCode) || IsUnresolvedTeamCode(awayCode))
            {
                unresolvedTeamIdentityCount += 1;
                continue;
            }

            var gameDate = FindCardDate(card);
            if (gameDate is null)
            {
                warnings.Add($"FIBA game {slugParts[0]} had no game date; used the edition start date {fallbackDate:yyyy-MM-dd}.");
                gameDate = fallbackDate.Value;
            }
            else if (gameDate.Value.Year < 1900)
            {
                warnings.Add($"FIBA game {slugParts[0]} exposed an invalid pre-1900 date; used the edition start date {fallbackDate:yyyy-MM-dd}.");
                gameDate = fallbackDate.Value;
            }

            var scores = cardTeams is not null && slugParts.Length < 3
                ? new short?[] { cardTeams[0].Score, cardTeams[1].Score }
                : ParseScores(card).ToArray();
            var phaseLabel = FindPhaseLabel(card);
            var phaseParts = phaseLabel?.Split('Â·', 2, StringSplitOptions.TrimEntries);
            var status = FindStatus(card, scores);

            games.Add(new BasketballProviderGame(
                Source,
                slugParts[0],
                gameDate.Value,
                status,
                homeCode,
                homeCode,
                awayCode,
                awayCode,
                scores.ElementAtOrDefault(0),
                scores.ElementAtOrDefault(1),
                new BasketballProviderGameProvenance(
                    BuildAbsoluteUrl(gamePath),
                    $"{year}:{sourcePath}",
                    fetchedAtUtc,
                    ParserVersion,
                    revision),
                null,
                phaseParts?.ElementAtOrDefault(0),
                phaseParts?.ElementAtOrDefault(1),
                CountryCodeFromTeamId(homeCode),
                CountryCodeFromTeamId(awayCode)));
        }

        if (missingStableLinkCount > 0)
        {
            warnings.Add($"FIBA edition exposed {missingStableLinkCount} game cards without stable game links; those records were not synthesized.");
        }

        if (missingTeamIdentityCount > 0)
        {
            warnings.Add($"FIBA edition exposed {missingTeamIdentityCount} game cards without both team identities; those records were not synthesized.");
        }

        if (unresolvedTeamIdentityCount > 0)
        {
            warnings.Add($"FIBA edition exposed {unresolvedTeamIdentityCount} game cards with unresolved TBD/TBC team identities; those records were skipped.");
        }

        // FIBA's current history pages render only the first selected round as
        // game cards, but often embed every round in the page's serialized data.
        // This is especially important for AfroBasket 2017, where the visible
        // cards are Zone 1 while the embedded payload also contains Zones 2-7,
        // playoffs and additional qualifiers. Prefer the embedded record when
        // both forms contain the same game because it carries stable historic
        // team IDs and round metadata.
        var embeddedGames = ParseEmbeddedGames(
            html,
            fetchedAtUtc,
            revision,
            sourcePath,
            year,
            warnings,
            warnIfEmpty: false);
        if (embeddedGames.Count > 0)
        {
            var gamesById = games.ToDictionary(game => game.SourceGameId, StringComparer.Ordinal);
            foreach (var embeddedGame in embeddedGames)
            {
                gamesById[embeddedGame.SourceGameId] = embeddedGame;
            }

            games = gamesById.Values.ToList();
        }

        if (games.Count == 0)
        {
            warnings.Add("FIBA page exposed no resolvable game-level records; no games were synthesized.");
        }

        return games;
    }

    private static IReadOnlyList<(string Code, short? Score)>? ParseCardTeams(HtmlNode card)
    {
        var teamNodes = card.SelectNodes(".//div[contains(@class, 'wa01avm')]")?.ToList() ?? [];
        var teams = new List<(string Code, short? Score)>();
        foreach (var teamNode in teamNodes)
        {
            var code = teamNode
                .SelectSingleNode(".//div[contains(@class, 'wa01avq')]") is { } codeNode
                ? Normalize(codeNode.InnerText).ToUpperInvariant()
                : string.Empty;
            var scoreText = teamNode
                .SelectSingleNode(".//div[contains(@class, 'wa01avo')]") is { } scoreNode
                ? Normalize(scoreNode.InnerText)
                : string.Empty;

            if (string.IsNullOrWhiteSpace(code))
            {
                var tokens = Normalize(teamNode.InnerText)
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length == 0)
                {
                    continue;
                }

                var repeatedCode = Regex.Match(
                    string.Concat(tokens),
                    "^(?<code>[A-Za-z0-9]+)\\k<code>(?<score>\\d+)$",
                    RegexOptions.IgnoreCase);
                code = repeatedCode.Success
                    ? repeatedCode.Groups["code"].Value.ToUpperInvariant()
                    : tokens[0].ToUpperInvariant();
                scoreText = repeatedCode.Success ? repeatedCode.Groups["score"].Value : tokens[^1];
            }

            var score = short.TryParse(scoreText, out var parsedScore) ? (short?)parsedScore : null;
            teams.Add((code, score));
        }

        return teams.Count >= 2 ? teams.Take(2).ToArray() : null;
    }

    private static bool IsUnresolvedTeamCode(string code)
        => code.Equals("TBD", StringComparison.OrdinalIgnoreCase) ||
           code.Equals("TBC", StringComparison.OrdinalIgnoreCase);

    private IReadOnlyCollection<BasketballProviderGame> ParseEmbeddedGames(
        string html,
        DateTime fetchedAtUtc,
        string revision,
        string sourcePath,
        int year,
        ICollection<string> warnings,
        bool warnIfEmpty = true)
    {
        var normalizedHtml = html.Replace("\\\"", "\"");
        var games = new List<BasketballProviderGame>();

        var gameMatches = Regex.Matches(normalizedHtml, "\"gameId\":(?<id>\\d+)", RegexOptions.IgnoreCase);
        for (var index = 0; index < gameMatches.Count; index++)
        {
            var gameMatch = gameMatches[index];
            var recordEnd = index + 1 < gameMatches.Count
                ? gameMatches[index + 1].Index
                : normalizedHtml.Length;
            var record = normalizedHtml[gameMatch.Index..recordEnd];
            var home = ParseEmbeddedTeam(record, "teamA");
            var away = ParseEmbeddedTeam(record, "teamB");
            var scores = Regex.Match(record, "\"teamAScore\":(?<home>-?\\d+|null).*?\"teamBScore\":(?<away>-?\\d+|null)", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            var dateMatch = Regex.Match(record, "\"gameDateTimeUTC\":\"(?<date>[^\"]+)\"", RegexOptions.IgnoreCase);
            var round = Regex.Match(record, "\"round\":\\{.*?\"roundCode\":\"(?<code>[^\"]*)\".*?\"roundName\":\"(?<name>[^\"]*)\"", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (home is null || away is null || !scores.Success || !dateMatch.Success ||
                !DateTime.TryParse(dateMatch.Groups["date"].Value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date) ||
                date.Year < 1900)
            {
                continue;
            }

            games.Add(new BasketballProviderGame(
                Source,
                gameMatch.Groups["id"].Value,
                date.ToUniversalTime(),
                scores.Groups["home"].Value == "null" ? "scheduled" : "final",
                home.SourceId,
                home.Name,
                away.SourceId,
                away.Name,
                ParseEmbeddedScore(scores.Groups["home"].Value),
                ParseEmbeddedScore(scores.Groups["away"].Value),
                new BasketballProviderGameProvenance(
                    BuildAbsoluteUrl(BuildEmbeddedGamePath(
                        sourcePath,
                        gameMatch.Groups["id"].Value,
                        home.SourceId,
                        away.SourceId)),
                    $"{year}:{sourcePath}",
                    fetchedAtUtc,
                    ParserVersion,
                    revision),
                null,
                round.Success ? round.Groups["name"].Value : null,
                round.Success ? round.Groups["code"].Value : null,
                CountryCodeFromTeamId(home.SourceId),
                CountryCodeFromTeamId(away.SourceId)));
        }

        if (games.Count == 0 && warnIfEmpty)
        {
            warnings.Add("FIBA page contained no parseable game cards or embedded game records.");
        }

        return games;
    }

    private static EmbeddedTeam? ParseEmbeddedTeam(string record, string propertyName)
    {
        var nextProperty = propertyName.Equals("teamA", StringComparison.OrdinalIgnoreCase)
            ? "teamB"
            : "teamAScore";
        var match = Regex.Match(
            record,
            $"\\\"{propertyName}\\\":\\{{(?<body>.*?)(?=\\\"{nextProperty}\\\":(?:\\{{)?)",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return null;
        }

        var body = match.Groups["body"].Value;
        var id = Regex.Match(body, "\\\"teamId\\\":(?<id>\\d+)", RegexOptions.IgnoreCase).Groups["id"].Value;
        var code = Regex.Match(body, "\\\"code\\\":(?:\\\"(?<code>[^\\\"]*)\\\"|null)", RegexOptions.IgnoreCase).Groups["code"].Value.Trim();
        var nameMatch = Regex.Match(body, "\\\"shortName\\\":\\\"(?<name>[^\\\"]*)\\\"", RegexOptions.IgnoreCase);
        if (!nameMatch.Success)
        {
            nameMatch = Regex.Match(body, "\\\"officialName\\\":\\\"(?<name>[^\\\"]*)\\\"", RegexOptions.IgnoreCase);
        }

        var sourceId = string.IsNullOrWhiteSpace(code)
            ? string.IsNullOrWhiteSpace(id) ? string.Empty : $"FIBA:{id}"
            : code;
        var name = nameMatch.Success ? nameMatch.Groups["name"].Value.Trim() : sourceId;
        if (string.IsNullOrWhiteSpace(sourceId))
        {
            return null;
        }

        return new EmbeddedTeam(sourceId, string.IsNullOrWhiteSpace(name) ? sourceId : name);
    }

    private static string BuildEmbeddedGamePath(string sourcePath, string gameId, string homeCode, string awayCode)
    {
        var editionPath = sourcePath.EndsWith("/games", StringComparison.OrdinalIgnoreCase)
            ? sourcePath[..^6]
            : sourcePath.TrimEnd('/');
        return $"{editionPath}/games/{gameId}-{homeCode.ToUpperInvariant()}-{awayCode.ToUpperInvariant()}";
    }

    private static short? ParseEmbeddedScore(string value)
        => short.TryParse(value, out var score) ? score : null;

    private sealed record EmbeddedTeam(string SourceId, string Name);

    private static DateTime? FindCardDate(HtmlNode card)
    {
        for (var node = card.ParentNode; node is not null; node = node.ParentNode)
        {
            for (var sibling = node.PreviousSibling; sibling is not null; sibling = sibling.PreviousSibling)
            {
                if (sibling.NodeType != HtmlNodeType.Element)
                {
                    continue;
                }

                var match = Regex.Match(Normalize(sibling.InnerText), @"\b\d{1,2} [A-Za-z]+ \d{4}\b");
                if (match.Success && DateTime.TryParseExact(
                    match.Value,
                    "d MMMM yyyy",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var date) && date.Year >= 1900)
                {
                    return DateTime.SpecifyKind(date, DateTimeKind.Utc);
                }
            }
        }

        return null;
    }

    private static DateTime? FindCompetitionStartDate(string html, int year)
    {
        var normalizedHtml = html.Replace("\\\"", "\"");
        var match = Regex.Match(normalizedHtml, "eventDateStart\\\"\\s*:\\s*\\\"(?<date>\\d{4}-\\d{2}-\\d{2})", RegexOptions.IgnoreCase);
        if (match.Success && DateTime.TryParseExact(
            match.Groups["date"].Value,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var startDate) && startDate.Year >= 1900)
        {
            return DateTime.SpecifyKind(startDate, DateTimeKind.Utc);
        }

        return new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    }

    private static IReadOnlyCollection<short?> ParseScores(HtmlNode card)
    {
        var matches = Regex.Matches(Normalize(card.InnerText), @"(?<!\d)\d{1,3}(?!\d)");
        return matches
            .Cast<Match>()
            .TakeLast(2)
            .Select(match => short.TryParse(match.Value, out var score) ? (short?)score : null)
            .ToList();
    }

    private static string? FindPhaseLabel(HtmlNode card)
    {
        var match = Regex.Match(
            Normalize(card.InnerText),
            @"(?<phase>[^\u00b7]+)\u00b7(?<round>.*?)(?=(?:Final|Scheduled|Postponed|Cancelled))",
            RegexOptions.IgnoreCase);
        if (match.Success)
        {
            return $"{match.Groups["phase"].Value.Trim()}\u00b7{match.Groups["round"].Value.Trim()}";
        }

        var phase = card.SelectNodes(".//div")?
            .Select(x => Normalize(x.InnerText))
            .FirstOrDefault(x => x.Length <= 80 &&
                (x.EndsWith("Round", StringComparison.OrdinalIgnoreCase) ||
                 x.EndsWith("Finals", StringComparison.OrdinalIgnoreCase)));
        return phase is null ? null : $"{phase}\u00b7";
    }

    private static string FindStatus(HtmlNode card, IReadOnlyCollection<short?> scores)
    {
        var text = Normalize(card.InnerText);
        var status = new[] { "Final", "Scheduled", "Postponed", "Cancelled" }
            .FirstOrDefault(candidate => text.Contains(candidate, StringComparison.OrdinalIgnoreCase));
        return (status ?? (scores.All(x => x.HasValue) ? "Final" : "Scheduled")).ToLowerInvariant();
    }

    private string BuildAbsoluteUrl(string path)
        => httpClient.BaseAddress is null ? path : new Uri(httpClient.BaseAddress, path).ToString();

    private static string NormalizePath(string value)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var absolute))
        {
            return absolute.AbsolutePath;
        }

        return value.Split('?', 2)[0];
    }

    private static string Normalize(string value)
        => HtmlEntity.DeEntitize(Regex.Replace(value, @"\s+", " ")).Trim();

    private static int ParseStartYear(string season)
    {
        var match = Regex.Match(season, @"\b(19|20)\d{2}\b");
        return match.Success && int.TryParse(match.Value, out var year) ? year : throw new ArgumentException($"FIBA season '{season}' has no four-digit year.", nameof(season));
    }
}

