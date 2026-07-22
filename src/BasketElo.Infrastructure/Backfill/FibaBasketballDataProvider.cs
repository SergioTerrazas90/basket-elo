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
        var editionPaths = KnownEditionPaths(historyFamily, variant, year);
        if (editionPaths is null)
        {
            var history = await GetPageAsync(historyPath, context, cancellationToken);
            editionPaths = FindEditionPaths(history.Content, historyFamily, year);
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
                games.AddRange(ParseGames(gamesPage.Content, gamesPage.FetchedAtUtc, gamesPage.Revision, gamesPath, year, warnings));
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

        return (games, false, warnings);
    }

    private static (string Family, string? Variant) ParseFamily(string sourceLeagueId)
    {
        var parts = sourceLeagueId.Split('|', 2, StringSplitOptions.TrimEntries);
        return (parts[0], parts.Length == 2 ? parts[1] : null);
    }

    private static IReadOnlyCollection<string>? KnownEditionPaths(string family, string? variant, int year)
    {
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
                2009 => ["/en/history/205-fiba-eurobasket-qualifiers/5132"],
                2007 => ["/en/history/205-fiba-eurobasket-qualifiers/4127"],
                2003 => ["/en/history/205-fiba-eurobasket-qualifiers/2878"],
                2001 => ["/en/history/205-fiba-eurobasket-qualifiers/228"],
                1999 => ["/en/history/205-fiba-eurobasket-qualifiers/1784"],
                1997 => ["/en/history/205-fiba-eurobasket-qualifiers/1502"],
                1995 =>
                [
                    "/en/history/205-fiba-eurobasket-qualifiers/1286",
                    "/en/history/205-fiba-eurobasket-qualifiers/1284"
                ],
                // 2005 and 1993 have no usable edition in the current FIBA
                // archive. Returning an empty collection records the gap in
                // the backfill instead of silently importing a wrong cycle.
                2005 or 1993 => [],
                _ => null
            };
        }

        if (family.Equals("206-fiba-eurobasket-division-b", StringComparison.OrdinalIgnoreCase))
        {
            return year switch
            {
                2007 => ["/en/history/206-fiba-eurobasket-division-b/4128"],
                2009 => ["/en/history/206-fiba-eurobasket-division-b/5133"],
                2011 => ["/en/history/206-fiba-eurobasket-division-b/5938"],
                _ => null
            };
        }

        if (family.Equals("219-fiba-olympic-qualifying-tournament", StringComparison.OrdinalIgnoreCase) && year == 2024)
        {
            return
            [
                "/en/events/fiba-olympic-qualifying-tournament-2024-valencia-spain",
                "/en/events/fiba-olympic-qualifying-tournament-2024-riga-latvia",
                "/en/events/fiba-olympic-qualifying-tournament-2024-piraeus-greece",
                "/en/events/fiba-olympic-qualifying-tournament-2024-san-juan-puerto-rico"
            ];
        }

        if (family.Equals("178-fiba-afrobasket-qualifiers", StringComparison.OrdinalIgnoreCase))
        {
            if (variant?.Equals("pre-qualifiers", StringComparison.OrdinalIgnoreCase) == true)
            {
                return year switch
                {
                    2021 => ["/en/history/178-fiba-afrobasket-qualifiers/208167"],
                    _ => null
                };
            }

            return year switch
            {
                2020 => ["/en/history/178-fiba-afrobasket-qualifiers/208167"],
                2021 => ["/en/history/178-fiba-afrobasket-qualifiers/208166"],
                2024 => ["/en/history/178-fiba-afrobasket-qualifiers/208806"],
                2025 => ["/en/events/fiba-afrobasket-2025-qualifiers"],
                _ => null
            };
        }

        if (family.Equals("192-fiba-asia-cup-qualifiers", StringComparison.OrdinalIgnoreCase))
        {
            return year switch
            {
                2019 => ["/en/history/192-fiba-asia-cup-qualifiers/208018"],
                2021 => ["/en/history/192-fiba-asia-cup-qualifiers/208126"],
                2023 => ["/en/history/192-fiba-asia-cup-qualifiers/208462"],
                2025 => ["/en/events/fiba-asiacup-2025-qualifiers"],
                _ => null
            };
        }

        if (family.Equals("182-fiba-americup-pre-qualifiers", StringComparison.OrdinalIgnoreCase))
        {
            return year switch
            {
                2018 =>
                [
                    "/en/history/182-fiba-americup-pre-qualifiers/208040",
                    "/en/history/182-fiba-americup-pre-qualifiers/208039",
                    "/en/history/182-fiba-americup-pre-qualifiers/208038"
                ],
                2019 => ["/en/history/182-fiba-americup-pre-qualifiers/208060"],
                2023 =>
                [
                    "/en/history/182-fiba-americup-pre-qualifiers/208517",
                    "/en/history/182-fiba-americup-pre-qualifiers/208516",
                    "/en/history/182-fiba-americup-pre-qualifiers/208515",
                    "/en/history/182-fiba-americup-pre-qualifiers/208518"
                ],
                2026 =>
                [
                    "/en/events/fiba-americup-2029-caribbean-pre-qualifiers",
                    "/en/events/fiba-americup-2029-central-american-pre-qualifiers"
                ],
                _ => null
            };
        }

        if (family.Equals("113-cbc-championship", StringComparison.OrdinalIgnoreCase))
        {
            if (variant?.Equals("cocaba", StringComparison.OrdinalIgnoreCase) == true)
            {
                return year switch
                {
                    2003 => ["/en/history/113-cbc-championship/3046"],
                    2004 => ["/en/history/113-cbc-championship/3204"],
                    2006 => ["/en/history/113-cbc-championship/4184"],
                    2007 => ["/en/history/113-cbc-championship/4828"],
                    2009 => ["/en/history/113-cbc-championship/5349"],
                    2011 => ["/en/history/113-cbc-championship/6604"],
                    2013 => ["/en/history/113-cbc-championship/7162"],
                    2015 => ["/en/history/113-cbc-championship/9363"],
                    _ => null
                };
            }

            if (variant?.Equals("caribbean", StringComparison.OrdinalIgnoreCase) == true)
            {
                return year switch
                {
                    2004 => ["/en/history/113-cbc-championship/3205"],
                    2006 => ["/en/history/113-cbc-championship/4219"],
                    2007 => ["/en/history/113-cbc-championship/4702"],
                    2009 => ["/en/history/113-cbc-championship/5347"],
                    2011 => ["/en/history/113-cbc-championship/6554"],
                    2014 => ["/en/history/113-cbc-championship/7761"],
                    2015 => ["/en/history/113-cbc-championship/9302"],
                    _ => null
                };
            }
        }

        return null;
    }

    private async Task<(string Content, DateTime FetchedAtUtc, string Revision)> GetPageAsync(
        string path,
        BackfillExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (!context.CanUseRequest())
        {
            throw new InvalidOperationException("FIBA backfill request budget reached before the archive page could be fetched.");
        }

        context.ConsumeRequest();
        using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        requestTimeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var response = await httpClient.GetAsync(path, requestTimeout.Token);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        return (content, DateTime.UtcNow, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)))[..16]);
    }

    private static IReadOnlyCollection<string> FindEditionPaths(string html, string family, int year)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        var prefix = $"/en/history/{family}/";
        var paths = new List<string>();

        void AddPath(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            var normalized = NormalizePath(value);
            if (normalized.StartsWith("/en/events/", StringComparison.OrdinalIgnoreCase))
            {
                if (!paths.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                {
                    paths.Add(normalized);
                }

                return;
            }

            if (!normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var editionId = normalized[prefix.Length..].Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (editionId is null || !editionId.All(char.IsDigit))
            {
                return;
            }

            var historyPath = $"/en/history/{family}/{editionId}";
            if (!paths.Contains(historyPath, StringComparer.OrdinalIgnoreCase))
            {
                paths.Add(historyPath);
            }
        }

        // The current FIBA archive contains malformed/nested table markup. HtmlAgilityPack
        // can consequently make a 1978 row inherit the later /1993 link. Parse literal
        // table rows first so a legacy link is only considered when its own row is for the
        // requested year; this is important because AfroBasket has two distinct 1993 rows.
        foreach (Match rowMatch in Regex.Matches(html, @"<tr\b[^>]*>(?<row>.*?)</tr>", RegexOptions.Singleline | RegexOptions.IgnoreCase))
        {
            var rowHtml = rowMatch.Groups["row"].Value;
            if (!Regex.IsMatch(Normalize(Regex.Replace(rowHtml, "<[^>]+>", " ")), $@"\b{year}\b"))
            {
                continue;
            }

            foreach (Match anchorMatch in Regex.Matches(rowHtml, "href\\s*=\\s*[\"'](?<href>[^\"']+)", RegexOptions.IgnoreCase))
            {
                AddPath(anchorMatch.Groups["href"].Value);
            }
        }

        if (paths.Count > 0)
        {
            return paths;
        }

        var rows = document.DocumentNode.SelectNodes("//tr")?.ToList() ?? [];
        foreach (var row in rows)
        {
            var yearMatch = Regex.Match(Normalize(row.InnerText), $@"\b{year}\b");
            if (!yearMatch.Success)
            {
                continue;
            }

            foreach (var anchor in row.SelectNodes(".//a[@href]") ?? Enumerable.Empty<HtmlNode>())
            {
                AddPath(anchor.GetAttributeValue("href", string.Empty));
            }
        }

        if (paths.Count > 0)
        {
            return paths;
        }

        var eventPattern = @"(?:https://www\.fiba\.basketball)?/en/events/[^"" ]+";
        foreach (Match match in Regex.Matches(html, eventPattern, RegexOptions.IgnoreCase))
        {
            var eventPath = NormalizePath(match.Value);
            var rowStart = html.LastIndexOf("<tr", match.Index, StringComparison.OrdinalIgnoreCase);
            var rowEnd = html.IndexOf("</tr>", match.Index, StringComparison.OrdinalIgnoreCase);
            var isMatchingRow = rowStart >= 0 && rowEnd > rowStart &&
                Regex.IsMatch(html.Substring(rowStart, rowEnd - rowStart), $@"\b{year}\b");
            if (eventPath.Contains(year.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase) || isMatchingRow)
            {
                AddPath(eventPath);
            }
        }

        if (paths.Count > 0)
        {
            return paths;
        }

        var historyLinkPattern = $@"(?:https://www\.fiba\.basketball)?/en/history/{Regex.Escape(family)}/(?<id>\d+)";
        foreach (Match match in Regex.Matches(html, historyLinkPattern, RegexOptions.IgnoreCase))
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

        foreach (var card in cards)
        {
            var anchor = card.SelectSingleNode(".//a[@href and contains(@href, '/games/')]");
            var gamePath = NormalizePath(anchor?.GetAttributeValue("href", string.Empty) ?? string.Empty);
            var slug = gamePath.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
            if (string.IsNullOrWhiteSpace(slug))
            {
                warnings.Add("Skipped a FIBA game card without a stable game link.");
                continue;
            }

            var slugParts = slug.Split('-', StringSplitOptions.RemoveEmptyEntries);
            if (slugParts.Length < 3 || !slugParts[0].All(char.IsDigit))
            {
                warnings.Add($"Skipped FIBA game link with an unexpected slug: {slug}.");
                continue;
            }

            var gameDate = FindCardDate(card);
            if (gameDate is null)
            {
                if (fallbackDate is null)
                {
                    warnings.Add($"Skipped FIBA game {slugParts[0]} because its date heading was not found.");
                    continue;
                }

                warnings.Add($"FIBA game {slugParts[0]} had no game date; used the edition start date {fallbackDate:yyyy-MM-dd}.");
                gameDate = fallbackDate;
            }

            var homeCode = slugParts[1].ToUpperInvariant();
            var awayCode = slugParts[2].ToUpperInvariant();
            var scores = ParseScores(card);
            var phaseLabel = FindPhaseLabel(card);
            var phaseParts = phaseLabel?.Split('·', 2, StringSplitOptions.TrimEntries);
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

        // FIBA's current history pages render only the first selected round as
        // game cards, but often embed every round in the page's serialized data.
        // This is especially important for AfroBasket 2017, where the visible
        // cards are Zone 1 while the embedded payload also contains Zones 2-7,
        // playoffs and additional qualifiers. Keep card data when both forms
        // contain the same game, and add the embedded games that are not visible.
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
                gamesById.TryAdd(embeddedGame.SourceGameId, embeddedGame);
            }

            games = gamesById.Values.ToList();
        }

        if (games.Count == 0)
        {
            warnings.Add("FIBA page contained no parseable game cards.");
        }

        return games;
    }

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
            var home = Regex.Match(record, "\"teamA\":\\{.*?\"code\":\"(?<code>[^\"]+)\".*?\"officialName\":\"(?<name>[^\"]*)\"", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            var away = Regex.Match(record, "\"teamB\":\\{.*?\"code\":\"(?<code>[^\"]+)\".*?\"officialName\":\"(?<name>[^\"]*)\"", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            var scores = Regex.Match(record, "\"teamAScore\":(?<home>-?\\d+|null).*?\"teamBScore\":(?<away>-?\\d+|null)", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            var dateMatch = Regex.Match(record, "\"gameDateTimeUTC\":\"(?<date>[^\"]+)\"", RegexOptions.IgnoreCase);
            var round = Regex.Match(record, "\"round\":\\{.*?\"roundCode\":\"(?<code>[^\"]*)\".*?\"roundName\":\"(?<name>[^\"]*)\"", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (!home.Success || !away.Success || !scores.Success || !dateMatch.Success ||
                !DateTime.TryParse(dateMatch.Groups["date"].Value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
            {
                continue;
            }

            games.Add(new BasketballProviderGame(
                Source,
                gameMatch.Groups["id"].Value,
                date.ToUniversalTime(),
                scores.Groups["home"].Value == "null" ? "scheduled" : "final",
                home.Groups["code"].Value,
                home.Groups["name"].Value,
                away.Groups["code"].Value,
                away.Groups["name"].Value,
                ParseEmbeddedScore(scores.Groups["home"].Value),
                ParseEmbeddedScore(scores.Groups["away"].Value),
                new BasketballProviderGameProvenance(
                    BuildAbsoluteUrl(BuildEmbeddedGamePath(
                        sourcePath,
                        gameMatch.Groups["id"].Value,
                        home.Groups["code"].Value,
                        away.Groups["code"].Value)),
                    $"{year}:{sourcePath}",
                    fetchedAtUtc,
                    ParserVersion,
                    revision),
                null,
                round.Success ? round.Groups["name"].Value : null,
                round.Success ? round.Groups["code"].Value : null,
                CountryCodeFromTeamId(home.Groups["code"].Value),
                CountryCodeFromTeamId(away.Groups["code"].Value)));
        }

        if (games.Count == 0 && warnIfEmpty)
        {
            warnings.Add("FIBA page contained no parseable game cards or embedded game records.");
        }

        return games;
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
                    out var date))
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
            out var startDate))
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
