using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using BasketElo.Domain.Backfill;
using HtmlAgilityPack;
using Microsoft.Extensions.Options;

namespace BasketElo.Infrastructure.Backfill;

public sealed class BasketballReferenceBasketballDataProvider(
    HttpClient httpClient,
    IOptions<BasketballReferenceOptions> options,
    IBasketballReferenceRateLimiter rateLimiter) : IBasketballDataProvider
{
    public const string Source = "basketball-reference";
    public const string ParserVersion = "basketball-reference-schedule-v1";
    public const string EuroleagueParserVersion = "basketball-reference-euroleague-schedules-v1";

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
                : string.Equals(country, "Europe", StringComparison.OrdinalIgnoreCase) &&
                  string.Equals(leagueName, "Euroleague", StringComparison.OrdinalIgnoreCase)
                    ? new BasketballProviderLeague(Source, "Euroleague", "Euroleague", "EUR", "end_year")
                    : null;
        return Task.FromResult(league);
    }

    public async Task<(IReadOnlyCollection<BasketballProviderGame> Games, bool HasMorePages, IReadOnlyCollection<string> Warnings)> GetGamesAsync(
        BasketballProviderLeague league,
        string season,
        BackfillExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (string.Equals(league.SourceLeagueId, "Euroleague", StringComparison.OrdinalIgnoreCase))
        {
            return await GetEuroleagueGamesAsync(season, context, cancellationToken);
        }

        if (!string.Equals(league.Source, Source, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(league.SourceLeagueId, "NBA", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Basketball-Reference provider only supports United States: NBA.");
        }

        var seasonSource = GetSeasonSource(season);
        var warnings = new List<string>();
        var games = new List<BasketballProviderGame>();
        var pages = new[]
        {
            new SourcePage($"leagues/{seasonSource.SourceKey}_games.html", true, "regular season"),
            new SourcePage($"playoffs/{seasonSource.LeaguePrefix}_{seasonSource.EndYear}_games.html", false, "playoffs")
        };

        foreach (var page in pages)
        {
            var loadedPage = await LoadPageAsync(page, context, warnings, cancellationToken);
            if (loadedPage is null)
            {
                continue;
            }

            ParseSchedulePage(loadedPage, seasonSource.SourceKey, page.Label, games, warnings);
        }

        return (games, false, warnings);
    }

    private async Task<(IReadOnlyCollection<BasketballProviderGame> Games, bool HasMorePages, IReadOnlyCollection<string> Warnings)> GetEuroleagueGamesAsync(
        string season,
        BackfillExecutionContext context,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var canonicalSeason = SeasonLabelNormalizer.ToFullSeasonLabel(season);
        var pieces = canonicalSeason.Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (pieces.Length != 2 || !int.TryParse(pieces[0], out var startYear) || !int.TryParse(pieces[1], out var endYear))
        {
            throw new ArgumentException($"Euroleague season '{season}' must be a full two-year label.", nameof(season));
        }

        var standingsPage = await LoadPageAsync(
            new SourcePage($"euro/euroleague/{endYear}.html", true, "Euroleague season standings"),
            context,
            warnings,
            cancellationToken);
        if (standingsPage is null)
        {
            return ([], false, warnings);
        }

        var teams = ParseEuroleagueTeams(standingsPage.Html);
        if (teams.Count == 0)
        {
            warnings.Add($"No Euroleague team links were found on {standingsPage.SourceUrl} (HTML length {standingsPage.Html.Length}).");
            return ([], false, warnings);
        }

        var candidates = new List<EuroleagueGameCandidate>();
        foreach (var team in teams)
        {
            var page = await LoadPageAsync(
                new SourcePage($"euro/schedules/{team.Slug}/{endYear}.html", false, $"{team.Name} Euroleague schedule"),
                context,
                warnings,
                cancellationToken);
            if (page is not null)
            {
                ParseEuroleagueSchedulePage(page, team, candidates, warnings);
            }
        }

        var games = candidates
            .GroupBy(candidate => candidate.MatchKey, StringComparer.Ordinal)
            .Select(group => group.First())
            .Select(candidate =>
            {
                var identity = $"{canonicalSeason}|{candidate.MatchKey}";
                var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..24].ToLowerInvariant();
                return new BasketballProviderGame(
                    Source,
                    $"bref-euroleague-{hash}",
                    DateTime.SpecifyKind(candidate.Date.Date.AddHours(12), DateTimeKind.Utc),
                    "finished",
                    candidate.HomeTeamId,
                    candidate.HomeTeamName,
                    candidate.AwayTeamId,
                    candidate.AwayTeamName,
                    candidate.HomeScore,
                    candidate.AwayScore,
                    new BasketballProviderGameProvenance(
                        candidate.SourceUrl,
                        endYear.ToString(CultureInfo.InvariantCulture),
                        candidate.FetchedAtUtc,
                        EuroleagueParserVersion,
                        candidate.Revision),
                    CompetitionPhase: "Euroleague",
                    CompetitionRound: null);
            })
            .OrderBy(game => game.GameDateTimeUtc)
            .ThenBy(game => game.SourceGameId, StringComparer.Ordinal)
            .ToArray();

        warnings.Add($"Basketball-Reference parsed {games.Length} distinct Euroleague game(s) for {canonicalSeason} from {teams.Count} team schedule(s).");
        return (games, false, warnings);
    }

    private static IReadOnlyCollection<EuroleagueTeam> ParseEuroleagueTeams(string html)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        var teams = new Dictionary<string, EuroleagueTeam>(StringComparer.OrdinalIgnoreCase);
        foreach (var anchor in document.DocumentNode.SelectNodes("//a[@href]") ?? Enumerable.Empty<HtmlNode>())
        {
            var href = anchor.GetAttributeValue("href", string.Empty);
            if (!href.Contains("/euro/teams/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var match = System.Text.RegularExpressions.Regex.Match(
                href,
                @"/euro/teams/(?<slug>[^/]+)/",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var name = CleanText(anchor.InnerText);
            if (!match.Success || !href.Contains("_euroleague.html", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var slug = match.Groups["slug"].Value;
            teams[slug] = new EuroleagueTeam(slug, name, $"bref-team:{slug}");
        }

        if (teams.Count == 0)
        {
            foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(
                         html,
                         @"<a\b[^>]*href=['""](?<href>[^'""]*/euro/teams/(?<slug>[^/'""]+)/[^'""]*_euroleague\.html)['""][^>]*>(?<name>.*?)</a>",
                         System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline))
            {
                var name = CleanText(System.Text.RegularExpressions.Regex.Replace(match.Groups["name"].Value, "<[^>]+>", " "));
                var slug = match.Groups["slug"].Value;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    teams[slug] = new EuroleagueTeam(slug, name, $"bref-team:{slug}");
                }
            }
        }

        return teams.Values.OrderBy(team => team.Slug, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static void ParseEuroleagueSchedulePage(
        LoadedPage page,
        EuroleagueTeam team,
        ICollection<EuroleagueGameCandidate> candidates,
        ICollection<string> warnings)
    {
        var document = new HtmlDocument();
        document.LoadHtml(page.Html);
        var rows = document.DocumentNode.SelectNodes("//tr[.//*[@data-stat='date_game_full']]")?.ToList() ?? [];
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            var dateText = CleanText(row.SelectSingleNode(".//*[@data-stat='date_game_full']")?.InnerText);
            if (dateText.Equals("Date", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (!DateTime.TryParse(dateText, CultureInfo.GetCultureInfo("en-US"), DateTimeStyles.AllowWhiteSpaces, out var gameDate))
            {
                warnings.Add($"Skipped {team.Name} Euroleague row {rowIndex + 1}: invalid date '{dateText}'.");
                continue;
            }

            var location = CleanText(row.SelectSingleNode(".//*[@data-stat='game_location']")?.InnerText);
            var opponentNode = row.SelectSingleNode(".//*[@data-stat='opp_name_link']");
            var opponentAnchor = opponentNode?.SelectSingleNode(".//a[@href]");
            var opponentName = CleanText(opponentNode?.InnerText);
            var opponentHref = opponentAnchor?.GetAttributeValue("href", string.Empty) ?? string.Empty;
            var opponentMatch = System.Text.RegularExpressions.Regex.Match(opponentHref, @"/euro/teams/(?<slug>[^/]+)/");
            if (string.IsNullOrWhiteSpace(opponentName) || !opponentMatch.Success)
            {
                warnings.Add($"Skipped {team.Name} Euroleague row {rowIndex + 1}: opponent is missing.");
                continue;
            }

            var opponentSlug = opponentMatch.Groups["slug"].Value;
            var teamScore = Score(row.SelectSingleNode(".//*[@data-stat='pts']"));
            var opponentScore = Score(row.SelectSingleNode(".//*[@data-stat='opp_pts']"));
            if (!teamScore.HasValue || !opponentScore.HasValue)
            {
                warnings.Add($"Skipped {team.Name} Euroleague row {rowIndex + 1}: final score is missing.");
                continue;
            }

            var isAway = location.Equals("@", StringComparison.Ordinal);
            var home = isAway ? new EuroleagueTeam(opponentSlug, opponentName, $"bref-team:{opponentSlug}") : team;
            var away = isAway ? team : new EuroleagueTeam(opponentSlug, opponentName, $"bref-team:{opponentSlug}");
            var homeScore = isAway ? opponentScore.Value : teamScore.Value;
            var awayScore = isAway ? teamScore.Value : opponentScore.Value;
            candidates.Add(new EuroleagueGameCandidate(
                gameDate,
                home.Id,
                home.Name,
                away.Id,
                away.Name,
                homeScore,
                awayScore,
                $"{gameDate:yyyy-MM-dd}|{home.Id}|{away.Id}|{homeScore}|{awayScore}",
                page.SourceUrl,
                page.FetchedAtUtc,
                page.Revision));
        }
    }

    public static string ToSourceSeasonKey(string season) => GetSeasonSource(season).SourceKey;

    private async Task<LoadedPage?> LoadPageAsync(
        SourcePage page,
        BackfillExecutionContext context,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var archivePath = GetArchivePath(page.RelativePath);
        if (File.Exists(archivePath))
        {
            var html = await File.ReadAllTextAsync(archivePath, cancellationToken);
            return CreateLoadedPage(page.RelativePath, html, File.GetLastWriteTimeUtc(archivePath));
        }

        if (!options.Value.NetworkAccessEnabled)
        {
            if (page.Required)
            {
                throw new FileNotFoundException(
                    $"Authorized Basketball-Reference archive page is missing: {archivePath}. " +
                    "Network access is disabled by source policy.",
                    archivePath);
            }

            warnings.Add($"No authorized {page.Label} archive page was found at '{archivePath}'.");
            return null;
        }

        if (string.IsNullOrWhiteSpace(options.Value.PermissionReference))
        {
            throw new InvalidOperationException(
                "Basketball-Reference network access requires a non-empty PermissionReference.");
        }

        if (!context.CanUseRequest())
        {
            warnings.Add($"Request budget reached before the {page.Label} page could be fetched.");
            return null;
        }

        using var response = await BackfillHttpRetryPolicy.SendAsync(
            async retryCancellationToken =>
            {
                if (!context.CanUseRequest())
                {
                    throw new InvalidOperationException(
                        $"Request budget was exhausted while retrying the {page.Label} page.");
                }

                context.ConsumeRequest();
                await rateLimiter.WaitAsync(retryCancellationToken);
                using var request = new HttpRequestMessage(HttpMethod.Get, page.RelativePath);
                request.Headers.UserAgent.ParseAdd(options.Value.UserAgent);
                return await httpClient.SendAsync(request, retryCancellationToken);
            },
            options.Value.MaxTransientRetries,
            options.Value.RetryBaseDelayMilliseconds,
            cancellationToken);
        if (!page.Required && response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            warnings.Add($"No {page.Label} page exists for this season.");
            return null;
        }

        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        return CreateLoadedPage(page.RelativePath, content, DateTime.UtcNow);
    }

    private void ParseSchedulePage(
        LoadedPage page,
        string sourceSeasonKey,
        string pageLabel,
        List<BasketballProviderGame> games,
        List<string> warnings)
    {
        var document = new HtmlDocument();
        document.LoadHtml(page.Html);
        var rows = document.DocumentNode
            .SelectNodes("//table[@id='schedule']//tbody/tr[not(contains(@class, 'thead'))]")
            ?.ToList() ?? [];
        if (rows.Count == 0)
        {
            warnings.Add($"No schedule rows were found on the {pageLabel} page '{page.SourceUrl}'.");
            return;
        }

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            try
            {
                var dateNode = Cell(row, "date_game");
                var dateText = CleanText(dateNode?.InnerText);
                if (!DateTime.TryParse(
                        dateText,
                        CultureInfo.GetCultureInfo("en-US"),
                        DateTimeStyles.AllowWhiteSpaces,
                        out var gameDate))
                {
                    warnings.Add($"Skipped {pageLabel} row {rowIndex + 1}: invalid date '{dateText}'.");
                    continue;
                }

                var awayNode = Cell(row, "visitor_team_name");
                var homeNode = Cell(row, "home_team_name");
                var awayName = CleanText(awayNode?.InnerText);
                var homeName = CleanText(homeNode?.InnerText);
                if (string.IsNullOrWhiteSpace(awayName) || string.IsNullOrWhiteSpace(homeName))
                {
                    warnings.Add($"Skipped {pageLabel} row {rowIndex + 1}: home or away team is missing.");
                    continue;
                }

                var awayTeamId = TeamId(awayNode, awayName);
                var homeTeamId = TeamId(homeNode, homeName);
                var awayScore = Score(Cell(row, "visitor_pts"));
                var homeScore = Score(Cell(row, "home_pts"));
                var notes = CleanText(Cell(row, "game_remarks")?.InnerText);
                var status = Status(homeScore, awayScore, notes);
                var sourceGameId = SourceGameId(
                    row,
                    page.RelativePath,
                    gameDate,
                    awayTeamId,
                    homeTeamId,
                    rowIndex);

                if (homeScore.HasValue != awayScore.HasValue)
                {
                    warnings.Add($"{sourceGameId}: only one final score is present.");
                }

                if (notes.Contains("postpon", StringComparison.OrdinalIgnoreCase) ||
                    notes.Contains("cancel", StringComparison.OrdinalIgnoreCase))
                {
                    warnings.Add($"{sourceGameId}: {notes}.");
                }

                if (notes.Contains("neutral", StringComparison.OrdinalIgnoreCase))
                {
                    warnings.Add($"{sourceGameId}: neutral-site game requires review.");
                }

                games.Add(new BasketballProviderGame(
                    Source,
                    sourceGameId,
                    DateTime.SpecifyKind(gameDate.Date.AddHours(12), DateTimeKind.Utc),
                    status,
                    homeTeamId,
                    homeName,
                    awayTeamId,
                    awayName,
                    homeScore,
                    awayScore,
                    new BasketballProviderGameProvenance(
                        page.SourceUrl,
                        sourceSeasonKey,
                        page.FetchedAtUtc,
                        ParserVersion,
                        page.Revision)));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                warnings.Add($"Skipped {pageLabel} row {rowIndex + 1}: {exception.Message}");
            }
        }
    }

    private LoadedPage CreateLoadedPage(string relativePath, string html, DateTime fetchedAtUtc)
    {
        var sourceUrl = httpClient.BaseAddress is null
            ? new Uri(new Uri(options.Value.BaseUrl), relativePath).ToString()
            : new Uri(httpClient.BaseAddress, relativePath).ToString();
        var revision = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(html))).ToLowerInvariant();
        return new LoadedPage(relativePath, sourceUrl, html, fetchedAtUtc, revision);
    }

    private string GetArchivePath(string relativePath)
    {
        var root = options.Value.ArchiveRoot;
        var absoluteRoot = Path.IsPathRooted(root)
            ? root
            : Path.GetFullPath(root, Directory.GetCurrentDirectory());
        return Path.Combine(absoluteRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static SeasonSource GetSeasonSource(string season)
    {
        var canonical = SeasonLabelNormalizer.ToFullSeasonLabel(season);
        var pieces = canonical.Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (pieces.Length != 2 ||
            !int.TryParse(pieces[0], out var startYear) ||
            !int.TryParse(pieces[1], out var endYear) ||
            endYear != startYear + 1 ||
            startYear < 1946)
        {
            throw new ArgumentException($"NBA season '{season}' must be a full label at or after 1946-1947.", nameof(season));
        }

        var prefix = startYear <= 1948 ? "BAA" : "NBA";
        return new SeasonSource($"{prefix}_{endYear}", prefix, endYear);
    }

    private static HtmlNode? Cell(HtmlNode row, string dataStat) =>
        row.SelectSingleNode($".//*[@data-stat='{dataStat}']");

    private static string CleanText(string? value) =>
        HtmlEntity.DeEntitize(value ?? string.Empty).Trim();

    private static short? Score(HtmlNode? node) =>
        short.TryParse(CleanText(node?.InnerText), NumberStyles.Integer, CultureInfo.InvariantCulture, out var score)
            ? score
            : null;

    private static string TeamId(HtmlNode? node, string fallbackName)
    {
        var href = node?.SelectSingleNode(".//a")?.GetAttributeValue("href", string.Empty) ?? string.Empty;
        var segments = href.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var teamsIndex = Array.FindIndex(segments, x => x.Equals("teams", StringComparison.OrdinalIgnoreCase));
        if (teamsIndex >= 0 && teamsIndex + 1 < segments.Length)
        {
            return segments[teamsIndex + 1].ToUpperInvariant();
        }

        return new string(fallbackName
            .ToUpperInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());
    }

    private static string SourceGameId(
        HtmlNode row,
        string relativePath,
        DateTime gameDate,
        string awayTeamId,
        string homeTeamId,
        int rowIndex)
    {
        var boxScoreHref = Cell(row, "box_score_text")
            ?.SelectSingleNode(".//a")
            ?.GetAttributeValue("href", string.Empty);
        if (!string.IsNullOrWhiteSpace(boxScoreHref))
        {
            var boxScoreId = Path.GetFileNameWithoutExtension(boxScoreHref);
            if (!string.IsNullOrWhiteSpace(boxScoreId))
            {
                return boxScoreId;
            }
        }

        var identity = $"{relativePath}|{gameDate:yyyyMMdd}|{awayTeamId}|{homeTeamId}|{rowIndex}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..24].ToLowerInvariant();
    }

    private static string Status(short? homeScore, short? awayScore, string notes)
    {
        if (notes.Contains("cancel", StringComparison.OrdinalIgnoreCase))
        {
            return "canceled";
        }

        if (notes.Contains("postpon", StringComparison.OrdinalIgnoreCase))
        {
            return "postponed";
        }

        return homeScore.HasValue && awayScore.HasValue ? "finished" : "scheduled";
    }

    private sealed record SeasonSource(string SourceKey, string LeaguePrefix, int EndYear);
    private sealed record SourcePage(string RelativePath, bool Required, string Label);
    private sealed record LoadedPage(
        string RelativePath,
        string SourceUrl,
        string Html,
        DateTime FetchedAtUtc,
        string Revision);
    private sealed record EuroleagueTeam(string Slug, string Name, string Id);
    private sealed record EuroleagueGameCandidate(
        DateTime Date,
        string HomeTeamId,
        string HomeTeamName,
        string AwayTeamId,
        string AwayTeamName,
        short HomeScore,
        short AwayScore,
        string MatchKey,
        string SourceUrl,
        DateTime FetchedAtUtc,
        string Revision);
}
