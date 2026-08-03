using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BasketElo.Domain.Backfill;
using HtmlAgilityPack;

namespace BasketElo.Infrastructure.Backfill;

/// <summary>
/// Parsers for the pre-2000 Yugoslav top-flight archives.  These sources do
/// not expose a common API: Pearl Basket is dated round-by-round HTML, while
/// the Wikipedia season pages expose Sports results as raw wikitext.
/// </summary>
internal static class SerbianHistoricalLegacyParser
{
    private static readonly Regex ScoreRegex = new(
        @"(?<home>\d{1,3})\s*[-\u0096\u2010\u2011\u2012\u2013\u2014]\s*(?<away>\d{1,3})",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex WikitextNameRegex = new(
        @"\|\s*name_(?<code>[A-Za-z0-9]+)\s*=\s*(?<name>.*?)(?=\s+\|\s*[A-Za-z_][A-Za-z0-9_]*\s*=|\r?$)",
        RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.CultureInvariant);

    private static readonly Regex WikitextMatchRegex = new(
        @"\|\s*match_(?<home>[A-Za-z0-9]+)_(?<away>[A-Za-z0-9]+)\s*=\s*(?<score>[^\r\n|]*)",
        RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.CultureInvariant);

    private static readonly Regex BorbaScoreRegex = new(
        @"(?<left>[^.!?\r\n]{2,100}?)(?:[\-\u0096\u2010\u2011\u2012\u2013\u2014])(?<right>[^.!?\r\n]{2,100}?)\s+(?<home>\d{2,3})\s*[:\-]\s*(?<away>\d{2,3})",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex BorbaScoreOnlyRegex = new(
        @"(?<home>\d{2,3})\s*[:\-]\s*(?<away>\d{2,3})",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex BorbaLinkRegex = new(
        @"href=""(?<url>https://pretraziva\.rs/show/borba/(?<date>\d{4}-\d{2}-\d{2})/(?<page>\d+))""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static IReadOnlyList<BasketballProviderGame> ParsePearlBasket(
        string html,
        string season,
        string sourceUrl,
        Func<string, string> canonicalizeTeam,
        Func<string, string> countryCode,
        string source,
        DateTime fetchedAtUtc)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        var games = new List<BasketballProviderGame>();
        var currentRound = "Round 1";
        DateTime? currentDate = null;
        var ordinal = 0;

        foreach (var node in document.DocumentNode.SelectNodes("//p") ?? Enumerable.Empty<HtmlNode>())
        {
            var className = node.GetAttributeValue("class", string.Empty);
            var text = Clean(node.InnerText);
            if (className.Contains("turno", StringComparison.OrdinalIgnoreCase))
            {
                var round = Regex.Match(text, @"^(?<round>\d+)\.\s*Round$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
                currentRound = round.Success ? $"Round {round.Groups["round"].Value}" : text;
                continue;
            }

            if (className.Contains("data", StringComparison.OrdinalIgnoreCase))
            {
                currentDate = ParseDate(text);
                continue;
            }

            if (!className.Contains("partita", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var match = Regex.Match(
                text,
                @"^(?<home>.+?)\s*[\-\u0096\u2010\u2011\u2012\u2013\u2014]\s*(?<away>.+?)\s+(?<score>\d{1,3}\s*[\-\u0096\u2010\u2011\u2012\u2013\u2014]\s*\d{1,3})(?:\s|$)",
                RegexOptions.CultureInvariant);
            if (!match.Success)
            {
                continue;
            }

            var score = ScoreRegex.Match(match.Groups["score"].Value);
            if (!score.Success || !short.TryParse(score.Groups["home"].Value, out var homeScore) ||
                !short.TryParse(score.Groups["away"].Value, out var awayScore))
            {
                continue;
            }

            var home = canonicalizeTeam(CleanTeam(match.Groups["home"].Value));
            var away = canonicalizeTeam(CleanTeam(match.Groups["away"].Value));
            if (string.IsNullOrWhiteSpace(home) || string.IsNullOrWhiteSpace(away) || home.Equals(away, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var date = currentDate ?? new DateTime(SeasonStartYear(season), 10, 1, 12, 0, 0, DateTimeKind.Utc);
            games.Add(new BasketballProviderGame(
                source,
                $"pearlbasket:{season}:{ordinal++:D4}:{Slug(home)}:{Slug(away)}",
                DateTime.SpecifyKind(date.Date.AddHours(12), DateTimeKind.Utc),
                "finished",
                $"serbia-club:{NormalizeKey(home).ToLowerInvariant()}",
                home,
                $"serbia-club:{NormalizeKey(away).ToLowerInvariant()}",
                away,
                homeScore,
                awayScore,
                new BasketballProviderGameProvenance(sourceUrl, season, fetchedAtUtc, "pearlbasket-yugoslav-v1", Hash(html)),
                CompetitionPhase: PearlBasketPhase(currentRound),
                CompetitionRound: currentRound,
                SourceHomeTeamCountryCode: countryCode(home),
                SourceAwayTeamCountryCode: countryCode(away)));
        }

        return games;
    }

    private static string PearlBasketPhase(string heading)
    {
        var key = NormalizeKey(heading);
        if (key.Contains("PLAYOUT", StringComparison.Ordinal))
        {
            return "Play-out";
        }

        if (key.Contains("CLASSIFICATION", StringComparison.Ordinal) || key is "58")
        {
            return "Classification";
        }

        if (key.Contains("PLAYOFF", StringComparison.Ordinal) ||
            key.Contains("FINALS", StringComparison.Ordinal) ||
            key is "FINAL" or "18FINALS" or "14FINALS" or "12FINALS")
        {
            return "Playoffs";
        }

        if (key.Contains("STAGEI", StringComparison.Ordinal))
        {
            return "Stage I";
        }

        return "Regular Season";
    }

    public static IReadOnlyList<BasketballProviderGame> ParseWikipediaRaw(
        string raw,
        string season,
        string sourceUrl,
        Func<string, string> canonicalizeTeam,
        Func<string, string> countryCode,
        string source,
        DateTime fetchedAtUtc)
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in WikitextNameRegex.Matches(raw))
        {
            names[match.Groups["code"].Value] = CleanWikiName(match.Groups["name"].Value);
        }

        var games = new List<BasketballProviderGame>();
        var ordinal = 0;
        foreach (Match match in WikitextMatchRegex.Matches(raw))
        {
            var score = ScoreRegex.Match(match.Groups["score"].Value);
            if (!score.Success || !short.TryParse(score.Groups["home"].Value, out var homeScore) ||
                !short.TryParse(score.Groups["away"].Value, out var awayScore))
            {
                continue;
            }

            var home = canonicalizeTeam(names.GetValueOrDefault(match.Groups["home"].Value, match.Groups["home"].Value));
            var away = canonicalizeTeam(names.GetValueOrDefault(match.Groups["away"].Value, match.Groups["away"].Value));
            if (string.IsNullOrWhiteSpace(home) || string.IsNullOrWhiteSpace(away) || home.Equals(away, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var date = InferMatrixDate(season, ordinal++);
            games.Add(new BasketballProviderGame(
                source,
                $"wikipedia:{season}:{ordinal:D4}:{Slug(home)}:{Slug(away)}",
                date,
                "finished",
                $"serbia-club:{NormalizeKey(home).ToLowerInvariant()}",
                home,
                $"serbia-club:{NormalizeKey(away).ToLowerInvariant()}",
                away,
                homeScore,
                awayScore,
                new BasketballProviderGameProvenance(sourceUrl, season, fetchedAtUtc, "wikipedia-yuba-matrix-v2", Hash(raw)),
                CompetitionPhase: "Regular Season",
                CompetitionRound: $"Source order {ordinal}",
                SourceHomeTeamCountryCode: countryCode(home),
                SourceAwayTeamCountryCode: countryCode(away)));
        }

        return games;
    }

    public static IReadOnlyList<BasketballProviderGame> ParseSerbianWikipediaRoundResults(
        string raw,
        string season,
        string sourceUrl,
        Func<string, string> canonicalizeTeam,
        Func<string, string> countryCode,
        string source,
        DateTime fetchedAtUtc)
    {
        // The Serbian page uses hand-written nested wiki tables rather than
        // the match_* templates used by the English-language season pages.
        // Parse the published regular-season table rows up to the playoff
        // heading, retaining the round number from each table header.
        var regularSeason = raw.Split("== Плеј-оф ==", 2, StringSplitOptions.None)[0];
        var games = new List<BasketballProviderGame>();
        var row = new List<string>();
        var currentRound = 0;
        var ordinal = 0;

        void FlushRow()
        {
            if (row.Count == 0)
            {
                return;
            }

            var text = string.Join(" ", row);
            var score = Regex.Match(text, @"(?<home>\d{2,3})\s*:\s*(?<away>\d{2,3})", RegexOptions.CultureInvariant);
            if (!score.Success || currentRound <= 0 ||
                !short.TryParse(score.Groups["home"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var homeScore) ||
                !short.TryParse(score.Groups["away"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var awayScore))
            {
                row.Clear();
                return;
            }

            var fixture = CleanWikiText(text[..score.Index]);
            var separator = Regex.Match(fixture, @"(?<home>.+?)\s*[-\u2010\u2011\u2012\u2013\u2014]\s*(?<away>.+)$", RegexOptions.CultureInvariant);
            if (!separator.Success)
            {
                row.Clear();
                return;
            }

            var home = canonicalizeTeam(CleanTeam(separator.Groups["home"].Value));
            var away = canonicalizeTeam(CleanTeam(separator.Groups["away"].Value));
            if (string.IsNullOrWhiteSpace(home) || string.IsNullOrWhiteSpace(away) || home.Equals(away, StringComparison.OrdinalIgnoreCase))
            {
                row.Clear();
                return;
            }

            var gameOrdinal = ordinal++;
            var date = InferMatrixDate(season, gameOrdinal);
            games.Add(new BasketballProviderGame(
                source,
                $"srwiki:{season}:{gameOrdinal:D4}:{Slug(home)}:{Slug(away)}",
                date,
                "finished",
                $"serbia-club:{NormalizeKey(home).ToLowerInvariant()}",
                home,
                $"serbia-club:{NormalizeKey(away).ToLowerInvariant()}",
                away,
                homeScore,
                awayScore,
                new BasketballProviderGameProvenance(sourceUrl, season, fetchedAtUtc, "serbian-wikipedia-rounds-v1", Hash(raw)),
                CompetitionPhase: "Regular Season",
                CompetitionRound: $"Round {currentRound}",
                SourceHomeTeamCountryCode: countryCode(home),
                SourceAwayTeamCountryCode: countryCode(away)));
            row.Clear();
        }

        foreach (var line in regularSeason.Split(['\r', '\n'], StringSplitOptions.None))
        {
            var sectionHeader = Regex.Match(line, @"^===\s*(?<round>\d+)\.\s*круг\s*===", RegexOptions.CultureInvariant);
            if (sectionHeader.Success && int.TryParse(sectionHeader.Groups["round"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sectionRound))
            {
                currentRound = sectionRound;
            }

            var roundHeader = Regex.Match(line, @"'''(?<round>\d+)\.\s*коло'''", RegexOptions.CultureInvariant);
            if (roundHeader.Success && int.TryParse(roundHeader.Groups["round"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedRound))
            {
                currentRound = parsedRound;
            }

            if (line.StartsWith("|-", StringComparison.Ordinal))
            {
                FlushRow();
                continue;
            }

            if (line.StartsWith("|}", StringComparison.Ordinal))
            {
                FlushRow();
                continue;
            }

            if (line.StartsWith('|'))
            {
                row.Add(line);
            }
        }

        FlushRow();
        return games;
    }

    public static IReadOnlyList<BasketballProviderGame> ParseBorbaText(
        string html,
        string season,
        string sourceUrl,
        DateTime publicationDateUtc,
        Func<string, string> canonicalizeTeam,
        Func<string, string> countryCode,
        string source,
        DateTime fetchedAtUtc)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        var text = document.GetElementbyId("text")?.InnerText ?? document.DocumentNode.InnerText;
        // Keep OCR line boundaries. Collapsing the whole article to one line
        // lets the score regex join a team name from one table row to a score
        // from the next row, producing false fixtures.
        text = WebUtility.HtmlDecode(text.Replace("\u0096", "-"));
        text = Regex.Replace(text, @"[ \t\f\v]+", " ");
        text = Regex.Replace(text, @"\r?\n\s*", "\n");
        var games = new Dictionary<string, BasketballProviderGame>(StringComparer.Ordinal);
        foreach (Match match in BorbaScoreRegex.Matches(text))
        {
            AddBorbaGame(
                match.Groups["left"].Value,
                match.Groups["right"].Value,
                match.Groups["home"].Value,
                match.Groups["away"].Value,
                games,
                season,
                sourceUrl,
                publicationDateUtc,
                canonicalizeTeam,
                countryCode,
                source,
                fetchedAtUtc,
                html);
        }

        // OCR frequently drops the dash between adjacent team names in the
        // compact round summaries, e.g. "Vojvodina Kolubara 87:68". Parse
        // those segments only when both names are recognized members of the
        // 1994-95 top-flight team set, which keeps foreign results out.
        if (season.Equals("1994-1995", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var paragraph in document.DocumentNode.SelectNodes("//div[@id='text']//p") ?? Enumerable.Empty<HtmlNode>())
            {
                var paragraphText = Regex.Replace(WebUtility.HtmlDecode(paragraph.InnerText.Replace("\u0096", "-")), @"\s+", " ").Trim();
                var previousEnd = 0;
                foreach (Match score in BorbaScoreOnlyRegex.Matches(paragraphText))
                {
                    var segment = paragraphText[previousEnd..score.Index];
                    var teams = FindKnownTeams(segment, canonicalizeTeam);
                    if (teams.Count >= 2)
                    {
                        AddBorbaGame(
                            teams[^2].Raw,
                            teams[^1].Raw,
                            score.Groups["home"].Value,
                            score.Groups["away"].Value,
                            games,
                            season,
                            sourceUrl,
                            publicationDateUtc,
                            canonicalizeTeam,
                            countryCode,
                            source,
                            fetchedAtUtc,
                            html,
                            teams[^2].Canonical,
                            teams[^1].Canonical);
                    }

                    previousEnd = score.Index + score.Length;
                }
            }
        }

        return games.Values.OrderBy(game => game.GameDateTimeUtc).ThenBy(game => game.SourceGameId, StringComparer.Ordinal).ToArray();
    }

    public static IReadOnlyList<BasketballProviderGame> ParseBorbaVerifiedResult(
        string html,
        string season,
        string sourceUrl,
        DateTime gameDateUtc,
        string homeTeam,
        string awayTeam,
        short homeScore,
        short awayScore,
        string competitionRound,
        Func<string, string> countryCode,
        string source,
        DateTime fetchedAtUtc)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        var text = WebUtility.HtmlDecode((document.GetElementbyId("text")?.InnerText ?? document.DocumentNode.InnerText).Replace("\u0096", "-"));
        var compact = Regex.Replace(text, @"\s+", string.Empty);
        var score = $"{homeScore}:{awayScore}";
        if (!compact.Contains(score, StringComparison.Ordinal) ||
            !ContainsBorbaTeam(compact, homeTeam) ||
            !ContainsBorbaTeam(compact, awayTeam))
        {
            return [];
        }

        var game = new BasketballProviderGame(
            source,
            $"borba:verified:{gameDateUtc:yyyyMMdd}:{NormalizeKey(homeTeam)}:{NormalizeKey(awayTeam)}:{homeScore}:{awayScore}".ToLowerInvariant(),
            gameDateUtc,
            "finished",
            $"serbia-club:{NormalizeKey(homeTeam).ToLowerInvariant()}",
            homeTeam,
            $"serbia-club:{NormalizeKey(awayTeam).ToLowerInvariant()}",
            awayTeam,
            homeScore,
            awayScore,
            new BasketballProviderGameProvenance(sourceUrl, season, fetchedAtUtc, "borba-verified-playoff-result-v1", Hash(html)),
            CompetitionPhase: "Playoffs",
            CompetitionRound: competitionRound,
            SourceHomeTeamCountryCode: countryCode(homeTeam),
            SourceAwayTeamCountryCode: countryCode(awayTeam));

        return [game];
    }

    private static void AddBorbaGame(
        string left,
        string right,
        string homeScoreText,
        string awayScoreText,
        IDictionary<string, BasketballProviderGame> games,
        string season,
        string sourceUrl,
        DateTime publicationDateUtc,
        Func<string, string> canonicalizeTeam,
        Func<string, string> countryCode,
        string source,
        DateTime fetchedAtUtc,
        string rawText,
        string? knownHome = null,
        string? knownAway = null)
    {
        if (!short.TryParse(homeScoreText, out var homeScore) || !short.TryParse(awayScoreText, out var awayScore))
        {
            return;
        }

        // OCR frequently turns standings columns into apparent scores
        // (for example, a 14-game column followed by a 70-point column).
        // A top-flight final in this period is safely within this range.
        if (homeScore < 40 || awayScore < 40 || homeScore > 160 || awayScore > 160)
        {
            return;
        }

        var home = knownHome ?? TryKnownTeam(left, canonicalizeTeam);
        var away = knownAway ?? TryKnownTeam(right, canonicalizeTeam);
        if (home is null || away is null || home.Equals(away, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var key = $"{publicationDateUtc:yyyy-MM-dd}:{NormalizeKey(home)}:{NormalizeKey(away)}:{homeScore}:{awayScore}";
        games[key] = new BasketballProviderGame(
            source,
            $"borba:{key.ToLowerInvariant()}",
            publicationDateUtc,
            "finished",
            $"serbia-club:{NormalizeKey(home).ToLowerInvariant()}",
            home,
            $"serbia-club:{NormalizeKey(away).ToLowerInvariant()}",
            away,
            homeScore,
            awayScore,
            new BasketballProviderGameProvenance(sourceUrl, season, fetchedAtUtc, "borba-yuba-ocr-v2", Hash(rawText)),
            CompetitionPhase: "Regular Season",
            CompetitionRound: "Newspaper result",
            SourceHomeTeamCountryCode: countryCode(home),
            SourceAwayTeamCountryCode: countryCode(away));
    }

    private static bool ContainsBorbaTeam(string compactText, string canonicalTeam)
    {
        var normalized = NormalizeKey(canonicalTeam);
        if (compactText.Contains(normalized, StringComparison.Ordinal))
        {
            return true;
        }

        return normalized switch
        {
            "RABOTNICKI" => compactText.Contains("РАБОТНИЧКИ", StringComparison.Ordinal),
            "CRVENAZVEZDA" => compactText.Contains("ЦРВЕНАЗВЕЗДА", StringComparison.Ordinal),
            _ => false
        };
    }

    private static IReadOnlyList<(string Raw, string Canonical)> FindKnownTeams(
        string value,
        Func<string, string> canonicalizeTeam)
    {
        var normalized = NormalizeKey(value);
        var matches = BorbaTeamCandidates
            .Select(candidate => (candidate, index: normalized.IndexOf(candidate, StringComparison.Ordinal), length: candidate.Length))
            .Where(item => item.index >= 0)
            .OrderBy(item => item.index)
            .ThenByDescending(item => item.length)
            .Select(item => (item.candidate, canonicalizeTeam(item.candidate)))
            .Where(item => !string.IsNullOrWhiteSpace(item.Item2))
            .ToArray();

        var selected = new List<(string Candidate, string Canonical, int Index, int Length)>();
        foreach (var match in matches)
        {
            var index = normalized.IndexOf(match.candidate, StringComparison.Ordinal);
            if (selected.Any(existing => index < existing.Index + existing.Length &&
                                         existing.Index < index + match.candidate.Length))
            {
                continue;
            }

            selected.Add((match.candidate, match.Item2, index, match.candidate.Length));
        }

        return selected
            .OrderBy(item => item.Index)
            .Select(item => (item.Candidate, item.Canonical))
            .ToArray();
    }

    public static IReadOnlyList<BorbaLink> ParseBorbaLinks(string html)
    {
        var links = new Dictionary<string, BorbaLink>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in BorbaLinkRegex.Matches(html))
        {
            if (!DateTime.TryParseExact(match.Groups["date"].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                continue;
            }

            var url = WebUtility.HtmlDecode(match.Groups["url"].Value);
            links[url] = new(url, DateTime.SpecifyKind(date.Date.AddHours(12), DateTimeKind.Utc));
        }

        return links.Values.OrderBy(link => link.PublicationDateUtc).ThenBy(link => link.Url, StringComparer.Ordinal).ToArray();
    }

    public static int ParseBorbaSearchTotal(string html)
    {
        var match = Regex.Match(
            Clean(html),
            @"Results\s+\d+\s*-\s*\d+\s+from\s+(?<total>\d+)\s+total",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success && int.TryParse(match.Groups["total"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var total)
            ? total
            : 0;
    }

    public static IReadOnlyList<BasketballProviderGame> ParsePartizanopedia(
        string html,
        string season,
        string sourceUrl,
        Func<string, string> canonicalizeTeam,
        Func<string, string> countryCode,
        string source,
        DateTime fetchedAtUtc,
        IReadOnlySet<string>? includedPhases = null)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        var games = new List<BasketballProviderGame>();
        var ordinal = 0;
        var allowedPhases = includedPhases ?? new HashSet<string>(["Regular Season"], StringComparer.OrdinalIgnoreCase);
        // The club archive also lists cup and European fixtures in tables
        // sharing the league table class.  The nearest section heading is
        // the reliable discriminator for the historical pages.
        foreach (var table in document.DocumentNode.SelectNodes(
                     "//table[contains(concat(' ', normalize-space(@class), ' '), ' utakmice90 ')]")
                 ?? Enumerable.Empty<HtmlNode>())
        {
            var heading = table.SelectSingleNode("preceding::h3[1]")?.InnerText ?? string.Empty;
            var phase = PartizanopediaPhase(heading);
            if (phase is null || !allowedPhases.Contains(phase))
            {
                continue;
            }

            foreach (var row in table.SelectNodes(".//tr[contains(@class, 'pobeda') or contains(@class, 'poraz')]") ?? Enumerable.Empty<HtmlNode>())
            {
                var cells = row.SelectNodes("./td")?.Select(cell => Clean(cell.InnerText)).ToArray() ?? [];
                var dateText = Regex.Replace(cells.ElementAtOrDefault(1)?.Replace(".", " ") ?? string.Empty, @"\s+", " ").Trim();
                if (cells.Length < 5 || !DateTime.TryParseExact(dateText, ["d M yyyy", "dd MM yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    continue;
                }

                var score = Regex.Match(cells[3], @"(?<home>\d{1,3})\s*[-:]\s*(?<away>\d{1,3})", RegexOptions.CultureInvariant);
                if (!score.Success || !short.TryParse(score.Groups["home"].Value, out var homeScore) || !short.TryParse(score.Groups["away"].Value, out var awayScore))
                {
                    continue;
                }

                var home = canonicalizeTeam(CleanTeam(cells[2]));
                var away = canonicalizeTeam(CleanTeam(cells[4]));
                var gameDate = DateTime.SpecifyKind(date.Date.AddHours(12), DateTimeKind.Utc);
                games.Add(new BasketballProviderGame(
                    source,
                    $"partizanopedia:{season}:{ordinal++:D4}:{Slug(home)}:{Slug(away)}",
                    gameDate,
                    "finished",
                    $"serbia-club:{NormalizeKey(home).ToLowerInvariant()}",
                    home,
                    $"serbia-club:{NormalizeKey(away).ToLowerInvariant()}",
                    away,
                    homeScore,
                    awayScore,
                    new BasketballProviderGameProvenance(sourceUrl, season, fetchedAtUtc, "partizanopedia-yuba-v2", Hash(html)),
                    CompetitionPhase: phase,
                    CompetitionRound: string.IsNullOrWhiteSpace(cells[0]) ? "Published schedule" : cells[0],
                    SourceHomeTeamCountryCode: countryCode(home),
                    SourceAwayTeamCountryCode: countryCode(away)));
            }
        }

        return games;
    }

    private static string? PartizanopediaPhase(string heading)
    {
        var key = NormalizeKey(heading);
        if (key.Contains("PRVENSTVO", StringComparison.Ordinal))
        {
            return "Regular Season";
        }

        if (key.Contains("PLEJOF", StringComparison.Ordinal) || key.Contains("PLAYOFF", StringComparison.Ordinal))
        {
            return "Playoffs";
        }

        if (key.Contains("KUP", StringComparison.Ordinal))
        {
            return "Cup";
        }

        return null;
    }

    private static string? TryKnownTeam(string value, Func<string, string> canonicalizeTeam)
    {
        var normalized = NormalizeKey(value);
        var candidates = BorbaTeamCandidates;
        /*
        {
            "BEOVUKBEMO", "BEOVUK", "BEOBANKA", "BIGENEKSMETALAC", "BIGENEKS", "BIGENEXMETALAC", "BIGENEX", "BFCCEOIN", "BFC", "BOBANIK", "BOROVICA",
            "BORACNEKTAR", "BORACCAK", "CRVENAZVEZDA", "FAGAR", "IBONNIKSIC", "IBON", "JUGOTESTNN", "JUGOTES",
            "KOLUBARA", "MLADOSTSRBOS", "MLADOST", "NAPNOVISAD", "NAPREDAK", "OKKKIKINDA", "OKKSABAC", "PEMONTPROLETER", "PROLETER",
            "PRIVREDNABANKANOVISAD", "RADNICKICIP", "RADNICKIKRAGUJEVAC", "RAJBANKA", "SREMTIFANI", "TEMKONIKŠIĆ", "VOJVODINAPANŠPED",
            "IVAKORMILO", "IVAOMEGA", "SLOBODAUZICE", "PRIVREDNABANKANOVISAD", "CRVENAZVEZDA", "BORACBANJALUKA", "RABOTNICKI", "RADNICKI", "BUDUCNOST",
            "PROFIKOLOR", "PARTIZAN", "OKKBEOGRAD", "SLOGA", "SPARTAK", "LOVCEN", "VOJVODINA", "ZORKA", "IVA",
            "BOSNA", "CIBONA", "ZADAR", "OLIMPIJA", "JUGOPLASTIKA", "SLOBODADITA", "UZICE", "INFOSRTM"
        */
        var match = candidates.FirstOrDefault(candidate => normalized.Contains(candidate, StringComparison.Ordinal));
        return match is null ? null : canonicalizeTeam(match);
    }

    private static readonly string[] BorbaTeamCandidates =
    [
        "BEOVUKBEMO", "BEOVUK", "BEOBANKA", "BIGENEKSMETALAC", "BIGENEKS", "BIGENEXMETALAC", "BIGENEX",
        "BFCCEOIN", "BFC", "BOBANIK", "BOROVICA", "BORACNEKTAR", "BORACCAK", "BORACBANJALUKA", "BORAC",
        "CRVENAZVEZDA", "FAGAR", "IBONNIKSIC", "IBON", "IVAKORMILO", "IVAOMEGA", "JUGOTESTNN", "JUGOTES",
        "KOLUBARA", "LOVCEN", "MLADOSTSRBOS", "MLADOST", "MORNAR", "NAPNOVISAD", "NAPREDAK", "OKKKIKINDA",
        "OKKSABAC", "OKKBEOGRAD", "PARTIZAN", "PEMONTPROLETER", "PROLETER", "RADNICKICIP", "RADNICKIKRAGUJEVAC",
        "RADNICKI", "RAJBANKA", "SREMTIFANI", "SPARTAK", "TEMKO", "VOJVODINAPANSPED", "VOJVODINA",
        "UZICE", "BUDUCNOST"
    ];

    private static DateTime InferMatrixDate(string season, int ordinal)
    {
        var startYear = SeasonStartYear(season);
        return new DateTime(startYear, 10, 1, 12, 0, 0, DateTimeKind.Utc).AddDays(ordinal);
    }

    private static DateTime? ParseDate(string value)
    {
        var normalized = Regex.Replace(value, @"^(?<day>\d{1,2})(?:/\d{1,2})?(?<rest>-\d{1,2}-\d{4})$", "${day}${rest}");
        var formats = new[] { "d-M-yyyy", "dd-M-yyyy", "d-MM-yyyy", "dd-MM-yyyy" };
        return DateTime.TryParseExact(normalized, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? DateTime.SpecifyKind(date, DateTimeKind.Utc)
            : null;
    }

    private static int SeasonStartYear(string season) =>
        int.TryParse(season[..4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var year) ? year : 2000;

    private static string CleanTeam(string value) =>
        Regex.Replace(WebUtility.HtmlDecode(value), @"\s+", " ").Trim(' ', '\u00a0', '.', ':');

    private static string Clean(string value) =>
        Regex.Replace(WebUtility.HtmlDecode(value.Replace("\u0096", "-")), @"\s+", " ").Trim();

    private static string CleanWikiName(string value)
    {
        var name = Regex.Replace(value, @"<!--.*?-->", string.Empty, RegexOptions.Singleline);
        name = Regex.Replace(name, @"\[\[[^|\]]+\|([^\]]+)\]\]", "$1");
        name = Regex.Replace(name, @"\[\[([^\]]+)\]\]", "$1");
        name = Regex.Replace(name, @"\{\{.*?\}\}", string.Empty);
        return Clean(name).Trim('*', '\'', '"');
    }

    private static string CleanWikiText(string value)
    {
        var text = Regex.Replace(value, @"<ref.*?</ref>", string.Empty, RegexOptions.Singleline | RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\[\[[^|\]]+\|([^\]]+)\]\]", "$1");
        text = Regex.Replace(text, @"\[\[([^\]]+)\]\]", "$1");
        text = Regex.Replace(text, @"\{\{.*?\}\}", string.Empty, RegexOptions.Singleline);
        text = Regex.Replace(text, @"<[^>]+>", " ");
        text = text.Replace("'''", string.Empty, StringComparison.Ordinal)
            .Replace("''", string.Empty, StringComparison.Ordinal)
            .Replace('|', ' ');
        return Clean(text);
    }

    private static string Slug(string value) => NormalizeKey(value).ToLowerInvariant();

    private static string NormalizeKey(string value) => string.Concat(TransliterateSerbian(value ?? string.Empty)
        .Normalize(NormalizationForm.FormD)
        .Where(character => char.IsLetterOrDigit(character) &&
            CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark))
        .ToUpperInvariant();

    private static string TransliterateSerbian(string value)
        => string.Concat(value.Select(character => character switch
        {
            'А' => "A", 'Б' => "B", 'В' => "V", 'Г' => "G", 'Д' => "D", 'Ђ' => "Dj", 'Е' => "E", 'Ж' => "Z", 'З' => "Z",
            'И' => "I", 'Ј' => "J", 'К' => "K", 'Л' => "L", 'Љ' => "Lj", 'М' => "M", 'Н' => "N", 'Њ' => "Nj", 'О' => "O",
            'П' => "P", 'Р' => "R", 'С' => "S", 'Т' => "T", 'Ћ' => "C", 'У' => "U", 'Ф' => "F", 'Х' => "H", 'Ц' => "C",
            'Ч' => "C", 'Џ' => "Dz", 'Ш' => "S", 'а' => "a", 'б' => "b", 'в' => "v", 'г' => "g", 'д' => "d", 'ђ' => "dj",
            'е' => "e", 'ж' => "z", 'з' => "z", 'и' => "i", 'ј' => "j", 'к' => "k", 'л' => "l", 'љ' => "lj", 'м' => "m",
            'н' => "n", 'њ' => "nj", 'о' => "o", 'п' => "p", 'р' => "r", 'с' => "s", 'т' => "t", 'ћ' => "c", 'у' => "u",
            'ф' => "f", 'х' => "h", 'ц' => "c", 'ч' => "c", 'џ' => "dz", 'ш' => "s", _ => character.ToString()
        }));

    public sealed record BorbaLink(string Url, DateTime PublicationDateUtc);

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..16];
}
