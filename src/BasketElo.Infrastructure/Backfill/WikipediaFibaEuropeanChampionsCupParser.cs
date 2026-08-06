using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BasketElo.Domain.Backfill;
using HtmlAgilityPack;

namespace BasketElo.Infrastructure.Backfill;

/// <summary>
/// Parses Wikipedia edition articles for the European Champions Cup and its
/// FIBA/Euroleague predecessor names. The articles publish two-legged score
/// tables and, in later seasons, round-robin score matrices rather than stable
/// game IDs, so this parser creates deterministic IDs from the edition,
/// table coordinate and resolved teams/scores.
/// </summary>
internal static class WikipediaFibaEuropeanChampionsCupParser
{
    public const string ParserVersion = "wikipedia-fiba-european-champions-cup-wikitext-v2-score-matrix-guard";

    private static readonly IReadOnlyDictionary<string, string> MonthNumbers =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["enero"] = "01", ["febrero"] = "02", ["marzo"] = "03", ["abril"] = "04",
            ["mayo"] = "05", ["junio"] = "06", ["julio"] = "07", ["agosto"] = "08",
            ["septiembre"] = "09", ["octubre"] = "10", ["noviembre"] = "11", ["diciembre"] = "12",
            ["january"] = "01", ["february"] = "02", ["march"] = "03", ["april"] = "04",
            ["may"] = "05", ["june"] = "06", ["july"] = "07", ["august"] = "08",
            ["september"] = "09", ["october"] = "10", ["november"] = "11", ["december"] = "12"
        };

    public static string PageTitle(int startYear)
    {
        var endYear = (startYear + 1) % 100;
        var suffix = $"{startYear}-{endYear:00}";
        return startYear switch
        {
            >= 1958 and <= 1990 => $"Copa de Europa de baloncesto {suffix}",
            >= 1991 and <= 1995 => $"Liga Europea de la FIBA {suffix}",
            >= 1996 and <= 1999 => $"Euroliga de la FIBA {suffix}",
            _ => throw new ArgumentOutOfRangeException(nameof(startYear), startYear, "Wikipedia coverage is configured for 1958-1999.")
        };
    }

    public static string EnglishPageTitle(int startYear)
    {
        var endYear = (startYear + 1) % 100;
        var suffix = startYear == 1999
            ? $"{startYear}\u20132000"
            : $"{startYear}\u2013{endYear:00}";
        return startYear switch
        {
            >= 1958 and <= 1990 => $"{suffix} FIBA European Champions Cup",
            >= 1991 and <= 1995 => $"{suffix} FIBA European League",
            >= 1996 and <= 1999 => $"{suffix} FIBA EuroLeague",
            >= 2000 and <= 2007 => $"{suffix} Euroleague",
            _ => throw new ArgumentOutOfRangeException(nameof(startYear), startYear, "Wikipedia coverage is configured for 1958-2007.")
        };
    }

    public static string SaportaEnglishPageTitle(int startYear)
    {
        var endYear = (startYear + 1) % 100;
        var suffix = startYear == 1999
            ? $"{startYear}\u20132000"
            : $"{startYear}\u2013{endYear:00}";
        var competition = startYear switch
        {
            <= 1990 => "FIBA European Cup Winners' Cup",
            <= 1995 => "FIBA European Cup",
            <= 1997 => "FIBA EuroCup",
            _ => "FIBA Saporta Cup"
        };
        return $"{suffix} {competition}";
    }

    public static string KoracEnglishPageTitle(int startYear)
    {
        var endYear = startYear + 1;
        return startYear switch
        {
            1971 => "1972 FIBA KoraÄ‡ Cup",
            2001 => "2001â€“02 FIBA KoraÄ‡ Cup",
            >= 1972 and <= 2000 => $"{startYear}â€“{endYear % 100:00} FIBA KoraÄ‡ Cup",
            _ => throw new ArgumentOutOfRangeException(nameof(startYear), startYear, "Wikipedia coverage is configured for 1971-2001.")
        };
    }

    public static string KoracWikipediaPageTitle(int startYear)
    {
        var endYear = startYear + 1;
        return startYear switch
        {
            1971 => "1972 FIBA Kora\u0107 Cup",
            1972 => "1973 FIBA Kora\u0107 Cup",
            2001 => "2001\u201302 FIBA Kora\u0107 Cup",
            >= 1973 and <= 2000 => $"{startYear}\u2013{endYear % 100:00} FIBA Kora\u0107 Cup",
            _ => throw new ArgumentOutOfRangeException(nameof(startYear), startYear, "Wikipedia coverage is configured for 1971-2001.")
        };
    }

    public static IReadOnlyCollection<BasketballProviderGame> ParseGames(
        string wikitext,
        string season,
        string pageUrl,
        DateTime fetchedAtUtc,
        string revision,
        ICollection<string> warnings,
        string source = FibaBasketballDataProvider.Source,
        string parserVersion = ParserVersion,
        string sourceGameIdPrefix = "wiki-fiba")
    {
        var startYear = ParseStartYear(season);
        var endYear = startYear + 1;
        var fallbackDate = ExtractInfoboxDate(wikitext, startYear, endYear)
            ?? new DateTime(startYear, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var accumulator = new GameAccumulator(season, pageUrl, fetchedAtUtc, revision, fallbackDate, warnings, source, parserVersion, sourceGameIdPrefix);

        ParseTwoLegTemplates(wikitext, startYear, endYear, accumulator);
        ParseLiteralTables(wikitext, startYear, endYear, accumulator);
        ParseMatrixTables(wikitext, startYear, endYear, accumulator);
        ParseSportsTableTemplates(wikitext, startYear, endYear, accumulator);
        ParseThreeLegTemplates(wikitext, startYear, endYear, accumulator);
        ParseTieBreakNotesReliable(wikitext, startYear, endYear, accumulator);
        ParseFinalTemplates(wikitext, startYear, endYear, accumulator);

        if (accumulator.InferredDateCount > 0)
        {
            warnings.Add($"Wikipedia did not publish exact dates for {accumulator.InferredDateCount} match legs; deterministic edition-order dates were used.");
        }

        warnings.Add($"Wikipedia parsed {accumulator.Games.Count} distinct game-level result(s) for {season}.");
        return accumulator.Games
            .OrderBy(game => game.GameDateTimeUtc)
            .ThenBy(game => game.SourceGameId, StringComparer.Ordinal)
            .ToArray();
    }

    public static IReadOnlyCollection<BasketballProviderGame> ParseHtmlMatrixGames(
        string html,
        string season,
        string pageUrl,
        DateTime fetchedAtUtc,
        string revision,
        ICollection<string> warnings,
        string source = FibaBasketballDataProvider.Source,
        string parserVersion = ParserVersion,
        string sourceGameIdPrefix = "wiki-fiba")
    {
        var startYear = ParseStartYear(season);
        var endYear = startYear + 1;
        var fallbackDate = ExtractInfoboxDate(html, startYear, endYear)
            ?? new DateTime(startYear, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var accumulator = new GameAccumulator(season, pageUrl, fetchedAtUtc, revision, fallbackDate, warnings, source, parserVersion, sourceGameIdPrefix);
        var document = new HtmlDocument();
        document.LoadHtml(html);

        var tableOrdinal = 0;
        foreach (var table in document.DocumentNode.SelectNodes("//table") ?? Enumerable.Empty<HtmlNode>())
        {
            tableOrdinal++;
            var rows = table.SelectNodes("./tr|.//tr")?.ToArray() ?? [];
            var headerIndex = Array.FindIndex(rows, row =>
            {
                var cells = GetHtmlCells(row).Select(cell => CleanHtmlText(cell)).ToArray();
                return cells.Any(cell => cell.Equals("Team", StringComparison.OrdinalIgnoreCase)) &&
                    cells.Any(cell => cell.Equals("Qualification", StringComparison.OrdinalIgnoreCase));
            });
            if (headerIndex < 0)
            {
                continue;
            }

            var headerCells = GetHtmlCells(rows[headerIndex]);
            var headerText = headerCells.Select(CleanHtmlText).ToArray();
            var teamColumn = Array.FindIndex(headerText, cell => cell.Equals("Team", StringComparison.OrdinalIgnoreCase));
            var qualificationColumn = Array.FindIndex(headerText, cell => cell.Equals("Qualification", StringComparison.OrdinalIgnoreCase));
            var matrixColumns = Enumerable.Range(qualificationColumn + 1, headerText.Length - qualificationColumn - 1)
                .Where(index => !string.IsNullOrWhiteSpace(headerText[index]))
                .ToArray();
            if (teamColumn < 0 || matrixColumns.Length < 3)
            {
                continue;
            }

            var dataRows = rows
                .Skip(headerIndex + 1)
                .Select(row =>
                {
                    var cells = GetHtmlCells(row);
                    var scoreCells = cells.Skip(Math.Max(0, cells.Count - matrixColumns.Length)).ToArray();
                    return new { Cells = cells, ScoreCells = scoreCells, Team = cells.Count > teamColumn ? ParseHtmlTeam(cells[teamColumn]) : null };
                })
                .Where(item => item.Team is not null &&
                    item.ScoreCells.Count(cell =>
                        TryParseScorePair(CleanHtmlText(cell), out _) || IsMatrixBye(CleanHtmlText(cell))) >= 2)
                .Take(matrixColumns.Length)
                .ToArray();
            if (dataRows.Length != matrixColumns.Length)
            {
                continue;
            }

            for (var rowIndex = 0; rowIndex < dataRows.Length; rowIndex++)
            {
                for (var columnIndex = rowIndex + 1; columnIndex < matrixColumns.Length; columnIndex++)
                {
                    var scoreCell = CleanHtmlText(dataRows[rowIndex].ScoreCells[columnIndex]);
                    if (!TryParseScorePair(scoreCell, out var score))
                    {
                        continue;
                    }

                    accumulator.Add(
                        dataRows[rowIndex].Team!,
                        dataRows[columnIndex].Team!,
                        score.Home,
                        score.Away,
                        accumulator.NextFallbackDate(),
                        "Wikipedia score matrix",
                        $"Table {tableOrdinal}",
                        $"html-matrix-{tableOrdinal}-{rowIndex + 1}-{columnIndex + 1}",
                        inferredDate: true);
                }
            }
        }

        warnings.Add($"Wikipedia HTML parsed {accumulator.Games.Count} distinct matrix game-level result(s) for {season}.");
        return accumulator.Games
            .OrderBy(game => game.GameDateTimeUtc)
            .ThenBy(game => game.SourceGameId, StringComparer.Ordinal)
            .ToArray();
    }

    public static IReadOnlyCollection<BasketballProviderGame> ParseTodor66Games(
        string html,
        string season,
        string pageUrl,
        DateTime fetchedAtUtc,
        string revision,
        ICollection<string> warnings,
        string source = FibaBasketballDataProvider.Source,
        string parserVersion = ParserVersion,
        string sourceGameIdPrefix = "wiki-fiba")
    {
        var startYear = ParseStartYear(season);
        var fallbackDate = new DateTime(startYear, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var accumulator = new GameAccumulator(season, pageUrl, fetchedAtUtc, revision, fallbackDate, warnings, source, parserVersion, sourceGameIdPrefix);
        var document = new HtmlDocument();
        document.LoadHtml(html);
        var tableOrdinal = 0;

        foreach (var table in document.DocumentNode.SelectNodes("//table") ?? Enumerable.Empty<HtmlNode>())
        {
            tableOrdinal++;
            int? monthHint = null;
            var rowOrdinal = 0;
            foreach (var row in table.SelectNodes(".//tr") ?? Enumerable.Empty<HtmlNode>())
            {
                rowOrdinal++;
                var cells = GetHtmlCells(row).Select(CleanHtmlText).ToArray();
                if (cells.Length < 5 || !TryParseTodorDates(cells[0], startYear, ref monthHint, out var dates))
                {
                    continue;
                }

                var scoreLikeIndexes = Enumerable.Range(1, cells.Length - 1)
                    .Where(index => IsTodorScore(cells[index]))
                    .ToArray();
                if (scoreLikeIndexes.Length == 0)
                {
                    continue;
                }

                var firstScoreIndex = scoreLikeIndexes[0];
                var hasTwoLegScores = scoreLikeIndexes.Length > 1 && scoreLikeIndexes[1] == firstScoreIndex + 1;
                var firstTeamIndex = IsCountryCode(cells.ElementAtOrDefault(firstScoreIndex - 1))
                    ? firstScoreIndex - 2
                    : firstScoreIndex - 1;
                if (firstTeamIndex < 1)
                {
                    continue;
                }

                var secondScoreIndex = hasTwoLegScores ? scoreLikeIndexes[1] : firstScoreIndex;
                var secondTeamIndex = hasTwoLegScores ? secondScoreIndex + 1 : firstScoreIndex + 1;
                if (secondTeamIndex >= cells.Length)
                {
                    continue;
                }

                var firstTeam = ParseTeam(cells[firstTeamIndex], IsCountryCode(cells.ElementAtOrDefault(firstTeamIndex + 1)) ? cells[firstTeamIndex + 1] : null);
                var secondTeam = ParseTeam(cells[secondTeamIndex], IsCountryCode(cells.ElementAtOrDefault(secondTeamIndex + 1)) ? cells[secondTeamIndex + 1] : null);
                if (firstTeam is null || secondTeam is null)
                {
                    continue;
                }

                var phase = "Published results";
                var round = "Todor66 score table";
                if (TryParseScorePair(cells[firstScoreIndex], out var firstScore))
                {
                    accumulator.Add(
                        firstTeam,
                        secondTeam,
                        firstScore.Home,
                        firstScore.Away,
                        dates[0],
                        phase,
                        round,
                        $"todor66-{tableOrdinal}-{rowOrdinal}-1",
                        inferredDate: false);
                }

                if (hasTwoLegScores && dates.Count > 1 && TryParseScorePair(cells[secondScoreIndex], out var secondScore))
                {
                    accumulator.Add(
                        secondTeam,
                        firstTeam,
                        secondScore.Away,
                        secondScore.Home,
                        dates[1],
                        phase,
                        round,
                        $"todor66-{tableOrdinal}-{rowOrdinal}-2",
             ëÝµ¶‰žËkºwµç@€€€€€€€€€µ½¹Ñ¡!¥¹Ð€ôµ½¹Ñ ì4(€€€€€€€€€€€™¥ÉÍÑ…ä€üüô‘…äì4(€€€€€€€ô4(4(€€€€€€€‘…Ñ•Ì€ôÁ…ÉÍ•¹¥ÍÑ¥¹Ð ¤¹Q½ÉÉ…ä ¤ì4(€€€€€€€É•ÑÕÉ¸‘…Ñ•Ì¹½Õ¹Ð€ø€Àì4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥ŒQ•…µI•˜üA…ÉÍ•!Ñµ±Q•…´¡!Ñµ±9½‘”•±°¤4(€€€ì4(€€€€€€€Ù…È…¹¡½È€ô•±°¹M•±•Ñ9½‘•Ì ˆ¸¼½…m¡É•˜…¹¹½Ð¡½¹Ñ…¥¹Ì¡¡É•˜°€¥±”èœ¤¥tˆ¤4(€€€€€€€€€€€€ü¹¥ÉÍÑ=É•™…Õ±Ð¡…¹‘¥‘…Ñ”€ôø€…ÍÑÉ¥¹œ¹%Í9Õ±±=É]¡¥Ñ•MÁ…”¡…¹‘¥‘…Ñ”¹%¹¹•ÉQ•áÐ¤¤ì4(€€€€€€€Ù…È¹…µ”€ô…¹¡½È¥Ì¹Õ±°€ü±•…¹!Ñµ±Q•áÐ¡•±°¤€è!Ñµ±¹Ñ¥Ñä¹•¹Ñ¥Ñ¥é”¡I••à¹I•Á±…”¡…¹¡½È¹%¹¹•ÉQ•áÐ° ‰qÌ¬ˆ°€ˆ€ˆ¤¤¹QÉ¥´ ¤ì4(€€€€€€€É•ÑÕÉ¸ÍÑÉ¥¹œ¹%Í9Õ±±=É]¡¥Ñ•MÁ…”¡¹…µ”¤€ü¹Õ±°€èA…ÉÍ•Q•…´ ‰mmí¹…µ•õutˆ°¹Õ±°¤ì4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥Œ%I•…‘=¹±å1¥ÍÐñ%I•…‘=¹±å1¥ÍÐñÍÑÉ¥¹œøøA…ÉÍ•Q…‰±•I½ÝÌ¡ÍÑÉ¥¹œ‰½‘ä¤4(€€€ì4(€€€€€€€Ù…ÈÉ½ÝÌ€ô¹•Ü1¥ÍÐñ%I•…‘=¹±å1¥ÍÐñÍÑÉ¥¹œøø ¤ì4(€€€€€€€™½É•… €¡Ù…ÈÉ½Ü¥¸I••à¹MÁ±¥Ð¡‰½‘ä° ˆ ý´¥yqð´¸¨ˆ¤¤4(€€€€€€€ì4(€€€€€€€€€€€Ù…È•±±Ì€ô¹•Ü1¥ÍÐñÍÑÉ¥¹œø ¤ì4(€€€€€€€€€€€™½É•… €¡Ù…ÈÉ…Ý1¥¹”¥¸É½Ü¹I•Á±…” ‰qÉq¸ˆ°€‰q¸ˆ°MÑÉ¥¹½µÁ…É¥Í½¸¹=É‘¥¹…°¤¹MÁ±¥Ð q¸œ¤¤4(€€€€€€€€€€€ì4(€€€€€€€€€€€€€€€Ù…È±¥¹”€ôÉ…Ý1¥¹”¹QÉ¥´ ¤ì4(€€€€€€€€€€€€€€€¥˜€¡±¥¹”¹1•¹Ñ €ôô€Àñð€¡±¥¹•lÁt€„ô€ðœ€˜˜±¥¹•lÁt€„ô€œ„œ¤¤4(€€€€€€€€€€€€€€€ì4(€€€€€€€€€€€€€€€€€€€½¹Ñ¥¹Õ”ì4(€€€€€€€€€€€€€€€ô4(4(€€€€€€€€€€€€€€€Ù…È‘•±¥µ¥Ñ•È€ô±¥¹•lÁt€ôô€œ„œ€ü€ˆ„„ˆ€è€‰ñðˆì4(€€€€€€€€€€€€€€€Ù…È±¥¹•	½‘ä€ô±¥¹•lÄ¸¹tì4(€€€€€€€€€€€€€€€Ù…ÈÙ…±Õ•Ì€ôMÁ±¥ÑQ½Á1•Ù•°¡±¥¹•	½‘ä°‘•±¥µ¥Ñ•È¤ì4(€€€€€€€€€€€€€€€¥˜€¡Ù…±Õ•Ì¹½Õ¹Ð€ôô€Ä€˜˜€…±¥¹•	½‘ä¹QÉ¥µMÑ…ÉÐ ¤¹MÑ…ÉÑÍ]¥Ñ  ‰ÍÑå±”ˆ°MÑÉ¥¹½µÁ…É¥Í½¸¹=É‘¥¹…±%¹½É•…Í”¤€˜˜4(€€€€€€€€€€€€€€€€€€€€…±¥¹•	½‘ä¹QÉ¥µMÑ…ÉÐ ¤¹MÑ…ÉÑÍ]¥Ñ  ‰…±¥¸ˆ°MÑÉ¥¹½µÁ…É¥Í½¸¹=É‘¥¹…±%¹½É•…Í”¤€˜˜4(€€€€€€€€€€€€€€€€€€€MÁ±¥ÑQ½Á1•Ù•°¡±¥¹•	½‘ä°€‰ðˆ¤¹½Õ¹Ð€ø€Ä¤4(€€€€€€€€€€€€€€€ì4(€€€€€€€€€€€€€€€€€€€Ù…±Õ•Ì€ôMÁ±¥ÑQ½Á1•Ù•°¡±¥¹•	½‘ä°€‰ðˆ¤ì4(€€€€€€€€€€€€€€€ô4(4(€€€€€€€€€€€€€€€™½É•… €¡Ù…ÈÙ…±Õ”¥¸Ù…±Õ•Ì¤4(€€€€€€€€€€€€€€€ì4(€€€€€€€€€€€€€€€€€€€•±±Ì¹‘¡MÑÉ¥Á•±±ÑÑÉ¥‰ÕÑ•Ì¡Ù…±Õ”¤¤ì4(€€€€€€€€€€€€€€€ô4(€€€€€€€€€€€ô4(4(€€€€€€€€€€€¥˜€¡•±±Ì¹½Õ¹Ð€ø€À¤4(€€€€€€€€€€€ì4(€€€€€€€€€€€€€€€É½ÝÌ¹‘¡•±±Ì¤ì4(€€€€€€€€€€€ô4(€€€€€€€ô4(4(€€€€€€€É•ÑÕÉ¸É½ÝÌì4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥ŒÍÑÉ¥¹œMÑÉ¥Á•±±ÑÑÉ¥‰ÕÑ•Ì¡ÍÑÉ¥¹œÙ…±Õ”¤4(€€€ì4(€€€€€€€Ù…ÈÍ•Á…É…Ñ½È€ô¥¹‘Q½Á1•Ù•±•±¥µ¥Ñ•È¡Ù…±Õ”¹QÉ¥´ ¤°€‰ðˆ¤ì4(€€€€€€€¥˜€¡Í•Á…É…Ñ½È€øô€À¤4(€€€€€€€ì4(€€€€€€€€€€€Ù…ÈÁÉ•™¥à€ôÙ…±Õ•l¸¹Í•Á…É…Ñ½Étì4(€€€€€€€€€€€¥˜€¡ÁÉ•™¥à¹½¹Ñ…¥¹Ì œôœ¤ñðÁÉ•™¥à¹½¹Ñ…¥¹Ì ‰ÍÑå±”ˆ°MÑÉ¥¹½µÁ…É¥Í½¸¹=É‘¥¹…±%¹½É•…Í”¤ñðÁÉ•™¥à¹½¹Ñ…¥¹Ì ‰…±¥¸ˆ°MÑÉ¥¹½µÁ…É¥Í½¸¹=É‘¥¹…±%¹½É•…Í”¤¤4(€€€€€€€€€€€ì4(€€€€€€€€€€€€€€€É•ÑÕÉ¸Ù…±Õ•l¡Í•Á…É…Ñ½È€¬€Ä¤¸¹t¹QÉ¥´ ¤ì4(€€€€€€€€€€€ô4(€€€€€€€ô4(4(€€€€€€€É•ÑÕÉ¸Ù…±Õ”¹QÉ¥´ ¤ì4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥ŒA…•½¹Ñ•áÐ¥¹‘½¹Ñ•áÐ¡ÍÑÉ¥¹œÑ•áÐ°¥¹Ð¥¹‘•à°¥¹ÐÍÑ…ÉÑe•…È°¥¹Ð•¹‘e•…È°…Ñ•Q¥µ”™…±±‰…­…Ñ”¤4(€€€ì4(€€€€€€€Ù…ÈÁÉ•™¥à€ôÑ•áÑl¸¹5…Ñ ¹5¥¸¡¥¹‘•à°Ñ•áÐ¹1•¹Ñ ¥tì4(€€€€€€€Ù…È¡•…‘¥¹Ì€ôI••à¹5…Ñ¡•Ì¡ÁÉ•™¥à° ˆ ý´¥x üñµ…É­ÌøõìÈ°Ñô¥qÌ¨ üñÑ•áÐø¸¨ü¥qÌ©q¬ñµ…É­Ìøˆ¤¹…ÍÐñ5…Ñ ø ¤¹Q½1¥ÍÐ ¤ì4(€€€€€€€Ù…ÈÁ¡…Í•!•…‘¥¹œ€ô¡•…‘¥¹Ì¹1…ÍÑ=É•™…Õ±Ð¡µ…Ñ €ôøµ…Ñ ¹É½ÕÁÍl‰µ…É­Ì‰t¹Y…±Õ”¹1•¹Ñ €ôô€È¤ì4(€€€€€€€Ù…ÈÉ½Õ¹‘!•…‘¥¹œ€ô¡•…‘¥¹Ì¹1…ÍÑ=É•™…Õ±Ð¡µ…Ñ €ôøµ…Ñ ¹É½ÕÁÍl‰µ…É­Ì‰t¹Y…±Õ”¹1•¹Ñ €øô€Ì¤€üüÁ¡…Í•!•…‘¥¹œì4(€€€€€€€Ù…ÈÁ¡…Í”€ôÁ¡…Í•!•…‘¥¹œ¥Ì¹Õ±°€ü€‰¥¹…°Á¡…Í”ˆ€è±•…¹]¥­¥Q•áÐ¡Á¡…Í•!•…‘¥¹œ¹É½ÕÁÍl‰Ñ•áÐ‰t¹Y…±Õ”¤ì4(€€€€€€€Ù…ÈÉ½Õ¹€ôÉ½Õ¹‘!•…‘¥¹œ¥Ì¹Õ±°€ü€‰AÕ‰±¥Í¡•É•ÍÕ±ÑÌˆ€è±•…¹]¥­¥Q•áÐ¡É½Õ¹‘!•…‘¥¹œ¹É½ÕÁÍl‰Ñ•áÐ‰t¹Y…±Õ”¤ì4(€€€€€€€Ù…È½¹Ñ•áÑMÑ…ÉÐ€ôÉ½Õ¹‘!•…‘¥¹œü¹%¹‘•à€üüÁ¡…Í•!•…‘¥¹œü¹%¹‘•à€üü5…Ñ ¹5…à À°¥¹‘•à€´€ÔÀÀ¤ì4(€€€€€€€Ù…È‘…Ñ•Ì€ôáÑÉ…Ñ…Ñ•Ì¡Ñ•áÑm½¹Ñ•áÑMÑ…ÉÐ¸¹5…Ñ ¹5¥¸¡¥¹‘•à°Ñ•áÐ¹1•¹Ñ ¥t°ÍÑ…ÉÑe•…È°•¹‘e•…È¤ì4(€€€€€€€É•ÑÕÉ¸¹•ÜA…•½¹Ñ•áÐ¡Á¡…Í”°É½Õ¹°‘…Ñ•Ì¤ì4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥Œ…Ñ•Q¥µ”üáÑÉ…Ñ%¹™½‰½á…Ñ”¡ÍÑÉ¥¹œÑ•áÐ°¥¹ÐÍÑ…ÉÑe•…È°¥¹Ð•¹‘e•…È¤4(€€€ì4(€€€€€€€Ù…Èµ…Ñ €ôI••à¹5…Ñ ¡Ñ•áÐ° ˆ ý¥´¥yqñqÌ©‘ÕÉ…Ñ¥½¹qÌ¨õqÌ¨ üñÙ…±Õ”ø¸¬¤ˆ¤ì4(€€€€€€€¥˜€ …µ…Ñ ¹MÕ•ÍÌ¤4(€€€€€€€ì4(€€€€€€€€€€€É•ÑÕÉ¸¹Õ±°ì4(€€€€€€€ô4(4(€€€€€€€Ù…È‘…Ñ”€ôáÑÉ…Ñ…Ñ•Ì¡µ…Ñ ¹É½ÕÁÍl‰Ù…±Õ”‰t¹Y…±Õ”°ÍÑ…ÉÑe•…È°•¹‘e•…È¤¹¥ÉÍÑ=É•™…Õ±Ð ¤ì4(€€€€€€€É•ÑÕÉ¸‘…Ñ”€ôô‘•™…Õ±Ð€ü¹Õ±°€è‘…Ñ”ì4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥Œ%I•…‘=¹±å1¥ÍÐñ…Ñ•Q¥µ”øáÑÉ…Ñ…Ñ•Ì¡ÍÑÉ¥¹œÑ•áÐ°¥¹ÐÍÑ…ÉÑe•…È°¥¹Ð•¹‘e•…È¤4(€€€ì4(€€€€€€€Ù…È±•…¹•€ô±•…¹]¥­¥Q•áÐ¡Ñ•áÐ¤ì4(€€€€€€€Ù…È‘…Ñ•Ì€ô¹•Ü1¥ÍÐñ…Ñ•Q¥µ”ø ¤ì4(€€€€€€€™½É•… €¡5…Ñ µ…Ñ ¥¸I••à¹5…Ñ¡•Ì¡±•…¹•° ˆ üð…q¤ üñ‘…äùq‘ìÄ°Éô¥qÌ¬ üè üé‘”¥qÌ¬¤ü üñµ½¹Ñ ùmµi„µë‡§·Ïét¬¤ üéqÌ¬ üè üé‘”¥qÌ¬¤ü üñå•…Èø üèÄåðÈÀ¥q‘ìÉô¤¤üˆ°I••á=ÁÑ¥½¹Ì¹%¹½É•…Í”¤¤4(€€€€€€€ì4(€€€€€€€€€€€¥˜€ …5½¹Ñ¡9Õµ‰•ÉÌ¹QÉå•ÑY…±Õ”¡µ…Ñ ¹É½ÕÁÍl‰µ½¹Ñ ‰t¹Y…±Õ”°½ÕÐÙ…Èµ½¹Ñ ¤ñð€…¥¹Ð¹QÉåA…ÉÍ”¡µ…Ñ ¹É½ÕÁÍl‰‘…ä‰t¹Y…±Õ”°½ÕÐÙ…È‘…ä¤¤4(€€€€€€€€€€€ì4(€€€€€€€€€€€€€€€½¹Ñ¥¹Õ”ì4(€€€€€€€€€€€ô4(4(€€€€€€€€€€€Ù…Èå•…È€ô¥¹Ð¹QÉåA…ÉÍ”¡µ…Ñ ¹É½ÕÁÍl‰å•…È‰t¹Y…±Õ”°½ÕÐÙ…È•áÁ±¥¥Ñe•…È¤4(€€€€€€€€€€€€€€€€ü•áÁ±¥¥Ñe•…È4(€€€€€€€€€€€€€€€€èµ½¹Ñ ¥Ì€ˆÀØˆ½È€ˆÀÜˆ½È€ˆÀàˆ€ü•¹‘e•…È€èÍÑ…ÉÑe•…Èì4(€€€€€€€€€€€¥˜€¡‘…ä€ðô…Ñ•Q¥µ”¹…åÍ%¹5½¹Ñ ¡å•…È°¥¹Ð¹A…ÉÍ”¡µ½¹Ñ °Õ±ÑÕÉ•%¹™¼¹%¹Ù…É¥…¹ÑÕ±ÑÕÉ”¤¤¤4(€€€€€€€€€€€ì4(€€€€€€€€€€€€€€€‘…Ñ•Ì¹‘¡¹•Ü…Ñ•Q¥µ”¡å•…È°¥¹Ð¹A…ÉÍ”¡µ½¹Ñ °Õ±ÑÕÉ•%¹™¼¹%¹Ù…É¥…¹ÑÕ±ÑÕÉ”¤°‘…ä°€À°€À°€À°…Ñ•Q¥µ•-¥¹¹UÑŒ¤¤ì4(€€€€€€€€€€€ô4(€€€€€€€ô4(4(€€€€€€€É•ÑÕÉ¸‘…Ñ•Ì¹¥ÍÑ¥¹Ð ¤¹Q½ÉÉ…ä ¤ì4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥ŒQ•…µI•˜üA…ÉÍ•Q•…´¡ÍÑÉ¥¹œÉ…Ü°ÍÑÉ¥¹œü½Õ¹ÑÉå½‘”¤4(€€€ì4(€€€€€€€Ù…È±¥¹¬€ôI••à¹5…Ñ ¡É…Ü° ‰qmql üñÑ…É•Ðùmyñqut¬¤ üéqð üñ‘¥ÍÁ±…äùmyqut¬¤¤ýquqtˆ¤ì4(€€€€€€€Ù…È¹…µ”€ô±•…¹]¥­¥Q•áÐ¡É…Ü¤¹QÉ¥´ œ€œ°€œ¨œ°€pœœ°€œ¸œ¤ì4(€€€€€€€¥˜€¡ÍÑÉ¥¹œ¹%Í9Õ±±=É]¡¥Ñ•MÁ…”¡¹…µ”¤ñð¹…µ”¥Ì€ˆ´ˆ½È€‰‰å”ˆñð¹…µ”¹½¹Ñ…¥¹Ì ‰íìˆ°MÑÉ¥¹½µÁ…É¥Í½¸¹=É‘¥¹…°¤ñð%ÍM½É•1¥­•Q•…µ9…µ”¡¹…µ”¤¤4(€€€€€€€ì4(€€€€€€€€€€€É•ÑÕÉ¸¹Õ±°ì4(€€€€€€€ô4(4(€€€€€€€Ù…È…¹½¹¥…°€ô±¥¹¬¹MÕ•ÍÌ€ü±¥¹¬¹É½ÕÁÍl‰Ñ…É•Ð‰t¹Y…±Õ”€è¹…µ”ì4(€€€€€€€Ù…È¥€ôM±Õ¥™ä¡…¹½¹¥…°¤ì4(€€€€€€€¥˜€¡ÍÑÉ¥¹œ¹%Í9Õ±±=É]¡¥Ñ•MÁ…”¡¥¤¤4(€€€€€€€ì4(€€€€€€€€€€€É•ÑÕÉ¸¹Õ±°ì4(€€€€€€€ô4(4(€€€€€€€Ù…È½Õ¹ÑÉä€ôÍÑÉ¥¹œ¹%Í9Õ±±=É]¡¥Ñ•MÁ…”¡½Õ¹ÑÉå½‘”¤€ü¹Õ±°€è±•…¹]¥­¥Q•áÐ¡½Õ¹ÑÉå½‘”¤¹Q½UÁÁ•É%¹Ù…É¥…¹Ð ¤ì4(€€€€€€€É•ÑÕÉ¸¹•ÜQ•…µI•˜ ‰Ý¥­¤µÑ•…´éí¥‘ôˆ°¹…µ”°½Õ¹ÑÉä¤ì4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥Œ‰½½°%ÍM½É•1¥­•Q•…µ9…µ”¡ÍÑÉ¥¹œÙ…±Õ”¤4(€€€€€€€€ôøI••à¹%Í5…Ñ  4(€€€€€€€€€€€Ù…±Õ”¹QÉ¥´ ¤°4(€€€€€€€€€€€ ‰yq‘ìÄ°ÍõqÌ©lµqÔÈÀÄÍqÔÈÀÄÓ
‹‹Šk
³‹Š
³O
‹‹Šk
³‹Š
³
uuqÌ©q‘ìÄ°Íôˆ°4(€€€€€€€€€€€I••á=ÁÑ¥½¹Ì¹Õ±ÑÕÉ•%¹Ù…É¥…¹Ð¤ì4(4(€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥ŒÍÑÉ¥¹œ±•…¹]¥­¥Q•áÐ¡ÍÑÉ¥¹œÙ…±Õ”¤4(€€€ì4(€€€€€€€Ù…È±•…¹•€ôI••à¹I•Á±…”¡Ù…±Õ”° ˆñÉ•™q‰mxùt¨ø¸¨üð½É•˜ùðñÉ•™q‰mxùt¨¼øˆ°ÍÑÉ¥¹œ¹µÁÑä°I••á=ÁÑ¥½¹Ì¹%¹½É•…Í”ðI••á=ÁÑ¥½¹Ì¹M¥¹±•±¥¹”¤ì4(€€€€€€€±•…¹•€ôI••à¹I•Á±…”¡±•…¹•° ‰qíqímyíõt©qõqôˆ°ÍÑÉ¥¹œ¹µÁÑä¤ì4(€€€€€€€±•…¹•€ôI••à¹I•Á±…”¡±•…¹•° ‰qmql üñÑ…É•Ðùmyñqut¬¤ üéqð üñ‘¥ÍÁ±…äùmyqut¬¤¤ýquqtˆ°µ…Ñ €ôøµ…Ñ ¹É½ÕÁÍl‰‘¥ÍÁ±…ä‰t¹MÕ•ÍÌ€üµ…Ñ ¹É½ÕÁÍl‰‘¥ÍÁ±…ä‰t¹Y…±Õ”€èµ…Ñ ¹É½ÕÁÍl‰Ñ…É•Ð‰t¹Y…±Õ”¤ì4(€€€€€€€±•…¹•€ôI••à¹I•Á±…”¡±•…¹•°€ˆñmxùt¬øˆ°€ˆ€ˆ¤ì4(€€€€€€€±•…¹•€ô±•…¹•¹I•Á±…” ˆœœœˆ°ÍÑÉ¥¹œ¹µÁÑä°MÑÉ¥¹½µÁ…É¥Í½¸¹=É‘¥¹…°¤¹I•Á±…” ˆœœˆ°ÍÑÉ¥¹œ¹µÁÑä°MÑÉ¥¹½µÁ…É¥Í½¸¹=É‘¥¹…°¤¹I•Á±…” ˆ™¹‰ÍÀìˆ°€ˆ€ˆ°MÑÉ¥¹½µÁ…É¥Í½¸¹=É‘¥¹…±%¹½É•…Í”¤ì4(€€€€€€€É•ÑÕÉ¸I••à¹I•Á±…”¡±•…¹•° ‰qÌ¬ˆ°€ˆ€ˆ¤¹QÉ¥´ ¤ì4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥ŒÍÑÉ¥¹œM±Õ¥™ä¡ÍÑÉ¥¹œÙ…±Õ”¤4(€€€ì4(€€€€€€€Ù…È¹½Éµ…±¥é•€ôÙ…±Õ”¹9½Éµ…±¥é”¡9½Éµ…±¥é…Ñ¥½¹½É´¹½Éµ¤ì4(€€€€€€€Ù…È‰Õ¥±‘•È€ô¹•ÜMÑÉ¥¹	Õ¥±‘•È¡¹½Éµ…±¥é•¹1•¹Ñ ¤ì4(€€€€€€€Ù…È‘…Í €ô™…±Í”ì4(€€€€€€€™½É•… €¡Ù…È¡…É…Ñ•È¥¸¹½Éµ…±¥é•¤4(€€€€€€€ì4(€€€€€€€€€€€¥˜€¡¡…ÉU¹¥½‘•%¹™¼¹•ÑU¹¥½‘•…Ñ•½Éä¡¡…É…Ñ•È¤€ôôMåÍÑ•´¹±½‰…±¥é…Ñ¥½¸¹U¹¥½‘•…Ñ•½Éä¹9½¹MÁ…¥¹5…É¬¤4(€€€€€€€€€€€ì4(€€€€€€€€€€€€€€€½¹Ñ¥¹Õ”ì4(€€€€€€€€€€€ô4(4(€€€€€€€€€€€¥˜€¡¡…È¹%Í1•ÑÑ•É=É¥¥Ð¡¡…É…Ñ•È¤¤4(€€€€€€€€€€€ì4(€€€€€€€€€€€€€€€‰Õ¥±‘•È¹ÁÁ•¹¡¡…È¹Q½1½Ý•É%¹Ù…É¥…¹Ð¡¡…É…Ñ•È¤¤ì4(€€€€€€€€€€€€€€€‘…Í €ô™…±Í”ì4(€€€€€€€€€€€ô4(€€€€€€€€€€€•±Í”¥˜€ …‘…Í €˜˜‰Õ¥±‘•È¹1•¹Ñ €ø€À¤4(€€€€€€€€€€€ì4(€€€€€€€€€€€€€€€‰Õ¥±‘•È¹ÁÁ•¹ œ´œ¤ì4(€€€€€€€€€€€€€€€‘…Í €ôÑÉÕ”ì4(€€€€€€€€€€€ô4(€€€€€€€ô4(4(€€€€€€€É•ÑÕÉ¸‰Õ¥±‘•È¹Q½MÑÉ¥¹œ ¤¹QÉ¥´ œ´œ¤ì4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥Œ‰½½°QÉåA…ÉÍ•M½É•A…¥È¡ÍÑÉ¥¹œÙ…±Õ”°½ÕÐ€¡Í¡½ÉÐ!½µ”°Í¡½ÉÐÝ…ä¤Í½É”¤4(€€€ì4(€€€€€€€Í½É”€ô‘•™…Õ±Ðì4(€€€€€€€Ù…È±•…¹•€ô±•…¹]¥­¥Q•áÐ¡Ù…±Õ”¤¹I•Á±…” ˆ¨ˆ°ÍÑÉ¥¹œ¹µÁÑä°MÑÉ¥¹½µÁ…É¥Í½¸¹=É‘¥¹…°¤ì4(€€€€€€€Ù…Èµ…Ñ €ôI••à¹5…Ñ ¡±•…¹•° ˆ üð…q¤ üñ¡½µ”ùq‘ìÄ°Íô¥qÌ©l·ŠOŠS‹Š
³Šs‹Š
³ŠuuqÌ¨ üñ…Ý…äùq‘ìÄ°Íô¤ ü…q¤ˆ¤ì4(€€€€€€€¥˜€ …µ…Ñ ¹MÕ•ÍÌñð€…Í¡½ÉÐ¹QÉåA…ÉÍ”¡µ…Ñ ¹É½ÕÁÍl‰¡½µ”‰t¹Y…±Õ”°½ÕÐÙ…È¡½µ”¤ñð€…Í¡½ÉÐ¹QÉåA…ÉÍ”¡µ…Ñ ¹É½ÕÁÍl‰…Ý…ä‰t¹Y…±Õ”°½ÕÐÙ…È…Ý…ä¤¤4(€€€€€€€ì4(€€€€€€€€€€€É•ÑÕÉ¸™…±Í”ì4(€€€€€€€ô4(4(€€€€€€€Í½É”€ô€¡¡½µ”°…Ý…ä¤ì4(€€€€€€€É•ÑÕÉ¸ÑÉÕ”ì4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥Œ‰½½°QÉåA…ÉÍ•M¥¹±•M½É”¡ÍÑÉ¥¹œÙ…±Õ”°½ÕÐÍ¡½ÉÐÍ½É”¤4(€€€ì4(€€€€€€€Í½É”€ô‘•™…Õ±Ðì4(€€€€€€€Ù…Èµ…Ñ €ôI••à¹5…Ñ ¡±•…¹]¥­¥Q•áÐ¡Ù…±Õ”¤° ˆ üð…q¥q‘ìÄ°Íô ü…q¤ˆ¤ì4(€€€€€€€É•ÑÕÉ¸µ…Ñ ¹MÕ•ÍÌ€˜˜Í¡½ÉÐ¹QÉåA…ÉÍ”¡µ…Ñ ¹Y…±Õ”°½ÕÐÍ½É”¤ì4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥Œ‰½½°QÉåA…ÉÍ•MÁ…¹¥Í¡…Ñ”¡ÍÑÉ¥¹œÙ…±Õ”°¥¹ÐÍÑ…ÉÑe•…È°¥¹Ð•¹‘e•…È°½ÕÐ…Ñ•Q¥µ”‘…Ñ”¤4(€€€ì4(€€€€€€€‘…Ñ”€ô‘•™…Õ±Ðì4(€€€€€€€Ù…ÈÁ…ÉÍ•€ôáÑÉ…Ñ…Ñ•Ì¡Ù…±Õ”°ÍÑ…ÉÑe•…È°•¹‘e•…È¤¹¥ÉÍÑ=É•™…Õ±Ð ¤ì4(€€€€€€€¥˜€¡Á…ÉÍ•€ôô‘•™…Õ±Ð¤4(€€€€€€€ì4(€€€€€€€€€€€É•ÑÕÉ¸™…±Í”ì4(€€€€€€€ô4(4(€€€€€€€‘…Ñ”€ôÁ…ÉÍ•ì4(€€€€€€€É•ÑÕÉ¸ÑÉÕ”ì4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥Œ1¥ÍÐñÍÑÉ¥¹œøMÁ±¥ÑQ½Á1•Ù•°¡ÍÑÉ¥¹œÙ…±Õ”°ÍÑÉ¥¹œ‘•±¥µ¥Ñ•È¤4(€€€ì4(€€€€€€€Ù…ÈÉ•ÍÕ±Ð€ô¹•Ü1¥ÍÐñÍÑÉ¥¹œø ¤ì4(€€€€€€€Ù…ÈÍÑ…ÉÐ€ô€Àì4(€€€€€€€Ý¡¥±”€¡ÍÑ…ÉÐ€ðôÙ…±Õ”¹1•¹Ñ ¤4(€€€€€€€ì4(€€€€€€€€€€€Ù…ÈÉ•±…Ñ¥Ù”€ô¥¹‘Q½Á1•Ù•±•±¥µ¥Ñ•È¡Ù…±Õ•mÍÑ…ÉÐ¸¹t°‘•±¥µ¥Ñ•È¤ì4(€€€€€€€€€€€¥˜€¡É•±…Ñ¥Ù”€ð€À¤4(€€€€€€€€€€€ì4(€€€€€€€€€€€€€€€É•ÍÕ±Ð¹‘¡Ù…±Õ•mÍÑ…ÉÐ¸¹t¹QÉ¥´ ¤¤ì4(€€€€€€€€€€€€€€€‰É•…¬ì4(€€€€€€€€€€€ô4(4(€€€€€€€€€€€É•ÍÕ±Ð¹‘¡Ù…±Õ”¹MÕ‰ÍÑÉ¥¹œ¡ÍÑ…ÉÐ°É•±…Ñ¥Ù”¤¹QÉ¥´ ¤¤ì4(€€€€€€€€€€€ÍÑ…ÉÐ€¬ôÉ•±…Ñ¥Ù”€¬‘•±¥µ¥Ñ•È¹1•¹Ñ ì4(€€€€€€€ô4(4(€€€€€€€É•ÑÕÉ¸É•ÍÕ±Ðì4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥Œ¥¹Ð¥¹‘Q½Á1•Ù•±•±¥µ¥Ñ•È¡ÍÑÉ¥¹œÙ…±Õ”°ÍÑÉ¥¹œ‘•±¥µ¥Ñ•È¤4(€€€ì4(€€€€€€€Ù…È‰É…•Ì€ô€Àì4(€€€€€€€Ù…È±¥¹­Ì€ô€Àì4(€€€€€€€™½È€¡Ù…È¥¹‘•à€ô€Àì¥¹‘•à€ðôÙ…±Õ”¹1•¹Ñ €´‘•±¥µ¥Ñ•È¹1•¹Ñ ì¥¹‘•à¬¬¤4(€€€€€€€ì4(€€€€€€€€€€€¥˜€¡Ù…±Õ”¹ÍMÁ…¸¡¥¹‘•à¤¹MÑ…ÉÑÍ]¥Ñ  ‰íìˆ¤¤ì‰É…•Ì¬¬ì¥¹‘•à¬¬ì½¹Ñ¥¹Õ”ìô4(€€€€€€€€€€€¥˜€¡Ù…±Õ”¹ÍMÁ…¸¡¥¹‘•à¤¹MÑ…ÉÑÍ]¥Ñ  ‰õôˆ¤¤ì‰É…•Ì€ô5…Ñ ¹5…à À°‰É…•Ì€´€Ä¤ì¥¹‘•à¬¬ì½¹Ñ¥¹Õ”ìô4(€€€€€€€€€€€¥˜€¡Ù…±Õ”¹ÍMÁ…¸¡¥¹‘•à¤¹MÑ…ÉÑÍ]¥Ñ  ‰mlˆ¤¤ì±¥¹­Ì¬¬ì¥¹‘•à¬¬ì½¹Ñ¥¹Õ”ìô4(€€€€€€€€€€€¥˜€¡Ù…±Õ”¹ÍMÁ…¸¡¥¹‘•à¤¹MÑ…ÉÑÍ]¥Ñ  ‰utˆ¤¤ì±¥¹­Ì€ô5…Ñ ¹5…à À°±¥¹­Ì€´€Ä¤ì¥¹‘•à¬¬ì½¹Ñ¥¹Õ”ìô4(€€€€€€€€€€€¥˜€¡‰É…•Ì€ôô€À€˜˜±¥¹­Ì€ôô€À€˜˜Ù…±Õ”¹ÍMÁ…¸¡¥¹‘•à°‘•±¥µ¥Ñ•È¹1•¹Ñ ¤¹M•ÅÕ•¹•ÅÕ…°¡‘•±¥µ¥Ñ•È¤¤É•ÑÕÉ¸¥¹‘•àì4(€€€€€€€ô4(4(€€€€€€€É•ÑÕÉ¸€´Äì4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥Œ%I•…‘=¹±å½±±•Ñ¥½¸ñQ•µÁ±…Ñ•5…Ñ øáÑÉ…ÑQ•µÁ±…Ñ•Ì¡ÍÑÉ¥¹œÑ•áÐ°ÍÑÉ¥¹œÑ•µÁ±…Ñ•9…µ”¤4(€€€ì4(€€€€€€€Ù…Èµ…Ñ¡•Ì€ô¹•Ü1¥ÍÐñQ•µÁ±…Ñ•5…Ñ ø ¤ì4(€€€€€€€Ù…È¹••‘±”€ô€‰íìˆ€¬Ñ•µÁ±…Ñ•9…µ”ì4(€€€€€€€Ù…ÈÍ•…É¡MÑ…ÉÐ€ô€Àì4(€€€€€€€Ý¡¥±”€¡Í•…É¡MÑ…ÉÐ€ðÑ•áÐ¹1•¹Ñ ¤4(€€€€€€€ì4(€€€€€€€€€€€Ù…ÈÍÑ…ÉÐ€ôÑ•áÐ¹%¹‘•á=˜¡¹••‘±”°Í•…É¡MÑ…ÉÐ°MÑÉ¥¹½µÁ…É¥Í½¸¹=É‘¥¹…±%¹½É•…Í”¤ì4(€€€€€€€€€€€¥˜€¡ÍÑ…ÉÐ€ð€À¤4(€€€€€€€€€€€ì4(€€€€€€€€€€€€€€€‰É•…¬ì4(€€€€€€€€€€€ô4(4(€€€€€€€€€€€Ù…È‘•ÁÑ €ô€Àì4(€€€€€€€€€€€Ù…È•¹€ô€´Äì4(€€€€€€€€€€€™½È€¡Ù…È¥¹‘•à€ôÍÑ…ÉÐì¥¹‘•à€ðÑ•áÐ¹1•¹Ñ €´€Äì¥¹‘•à¬¬¤4(€€€€€€€€€€€ì4(€€€€€€€€€€€€€€€¥˜€¡Ñ•áÐ¹ÍMÁ…¸¡¥¹‘•à°€È¤¹M•ÅÕ•¹•ÅÕ…° ‰íìˆ¤¤4(€€€€€€€€€€€€€€€ì4(€€€€€€€€€€€€€€€€€€€‘•ÁÑ ¬¬ì4(€€€€€€€€€€€€€€€€€€€¥¹‘•à¬¬ì4(€€€€€€€€€€€€€€€ô4(€€€€€€€€€€€€€€€•±Í”¥˜€¡Ñ•áÐ¹ÍMÁ…¸¡¥¹‘•à°€È¤¹M•ÅÕ•¹•ÅÕ…° ‰õôˆ¤¤4(€€€€€€€€€€€€€€€ì4(€€€€€€€€€€€€€€€€€€€‘•ÁÑ ´´ì4(€€€€€€€€€€€€€€€€€€€¥¹‘•à¬¬ì4(€€€€€€€€€€€€€€€€€€€¥˜€¡‘•ÁÑ €ôô€À¤4(€€€€€€€€€€€€€€€€€€€ì4(€€€€€€€€€€€€€€€€€€€€€€€•¹€ô¥¹‘•à€´€Äì4(€€€€€€€€€€€€€€€€€€€€€€€‰É•…¬ì4(€€€€€€€€€€€€€€€€€€€ô4(€€€€€€€€€€€€€€€ô4(€€€€€€€€€€€ô4(4(€€€€€€€€€€€¥˜€¡•¹€ð€À¤4(€€€€€€€€€€€ì4(€€€€€€€€€€€€€€€‰É•…¬ì4(€€€€€€€€€€€ô4(4(€€€€€€€€€€€µ…Ñ¡•Ì¹‘¡¹•ÜQ•µÁ±…Ñ•5…Ñ ¡ÍÑ…ÉÐ°Ñ•áÑl¡ÍÑ…ÉÐ€¬¹••‘±”¹1•¹Ñ ¤¸¹•¹‘t¤¤ì4(€€€€€€€€€€€Í•…É¡MÑ…ÉÐ€ô•¹€¬€Äì4(€€€€€€€ô4(4(€€€€€€€É•ÑÕÉ¸µ…Ñ¡•Ìì4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”ÍÑ…Ñ¥Œ¥¹ÐA…ÉÍ•MÑ…ÉÑe•…È¡ÍÑÉ¥¹œÍ•…Í½¸¤4(€€€ì4(€€€€€€€Ù…Èµ…Ñ €ôI••à¹5…Ñ ¡Í•…Í½¸° ‰qˆ ÄåðÈÀ¥q‘ìÉõqˆˆ¤ì4(€€€€€€€É•ÑÕÉ¸µ…Ñ ¹MÕ•ÍÌ€ü¥¹Ð¹A…ÉÍ”¡µ…Ñ ¹Y…±Õ”°Õ±ÑÕÉ•%¹™¼¹%¹Ù…É¥…¹ÑÕ±ÑÕÉ”¤€èÑ¡É½Ü¹•ÜÉÕµ•¹Ñá•ÁÑ¥½¸ ‰M•…Í½¸€íÍ•…Í½¹ôœ¡…Ì¹¼™½ÕÈµ‘¥¥Ðå•…È¸ˆ°¹…µ•½˜¡Í•…Í½¸¤¤ì4(€€€ô4(4(€€€ÁÉ¥Ù…Ñ”Í•…±•É•½ÉA…•½¹Ñ•áÐ¡ÍÑÉ¥¹œA¡…Í”°ÍÑÉ¥¹œI½Õ¹°%I•…‘=¹±å1¥ÍÐñ…Ñ•Q¥µ”ø…Ñ•Ì¤ì4(€€€ÁÉ¥Ù…Ñ”Í•…±•É•½ÉQ•µÁ±…Ñ•5…Ñ ¡¥¹Ð%¹‘•à°ÍÑÉ¥¹œ	½‘ä¤ì4(€€€ÁÉ¥Ù…Ñ”Í•…±•É•½ÉQ•…µI•˜¡ÍÑÉ¥¹œ%°ÍÑÉ¥¹œ9…µ”°ÍÑÉ¥¹œü½Õ¹ÑÉå½‘”¤ì4(4(€€€ÁÉ¥Ù…Ñ”Í•…±•±…ÍÌ…µ•ÕµÕ±…Ñ½È 4(€€€€€€€ÍÑÉ¥¹œÍ•…Í½¸°4(€€€€€€€ÍÑÉ¥¹œÁ…•UÉ°°4(€€€€€€€…Ñ•Q¥µ”™•Ñ¡•‘ÑUÑŒ°4(€€€€€€€ÍÑÉ¥¹œÉ•Ù¥Í¥½¸°4(€€€€€€€…Ñ•Q¥µ”™…±±‰…­…Ñ”°4(€€€€€€€%½±±•Ñ¥½¸ñÍÑÉ¥¹œøÝ…É¹¥¹Ì°4(€€€€€€€ÍÑÉ¥¹œÍ½ÕÉ”°4(€€€€€€€ÍÑÉ¥¹œÁ…ÉÍ•ÉY•ÉÍ¥½¸°4(€€€€€€€ÍÑÉ¥¹œÍ½ÕÉ•…µ•%‘AÉ•™¥à¤4(€€€ì4(€€€€€€€ÁÉ¥Ù…Ñ”É•…‘½¹±ä!…Í¡M•ÐñÍÑÉ¥¹œøÍ•µ…¹Ñ¥-•åÌ€ô¹•Ü¡MÑÉ¥¹½µÁ…É•È¹=É‘¥¹…°¤ì4(€€€€€€€ÁÉ¥Ù…Ñ”¥¹Ð™…±±‰…­=É‘¥¹…°ì4(4(€€€€€€€ÁÕ‰±¥Œ1¥ÍÐñ	…Í­•Ñ‰…±±AÉ½Ù¥‘•É…µ”ø…µ•Ìì•Ðìô€ômtì4(€€€€€€€ÁÕ‰±¥Œ¥¹Ð%¹™•ÉÉ•‘…Ñ•½Õ¹Ðì•ÐìÁÉ¥Ù…Ñ”Í•Ðìô4(€€€€€€€ÁÕ‰±¥Œ…Ñ•Q¥µ”…±±‰…­…Ñ”ì•Ðìô€ô™…±±‰…­…Ñ”ì4(4(€€€€€€€ÁÕ‰±¥Œ…Ñ•Q¥µ”9•áÑ…±±‰…­…Ñ” ¤4(€€€€€€€€€€€€ôø…Ñ•Q¥µ”¹MÁ•¥™å-¥¹¡…±±‰…­…Ñ”¹…Ñ”¹‘‘…åÌ¡™…±±‰…­=É‘¥¹…°¬¬¤°…Ñ•Q¥µ•-¥¹¹UÑŒ¤ì4(4(€€€€€€€ÁÕ‰±¥ŒÙ½¥‘¡Q•…µI•˜¡½µ”°Q•…µI•˜…Ý…ä°Í¡½ÉÐ¡½µ•M½É”°Í¡½ÉÐ…Ý…åM½É”°…Ñ•Q¥µ”‘…Ñ”°ÍÑÉ¥¹œÁ¡…Í”°ÍÑÉ¥¹œÉ½Õ¹°ÍÑÉ¥¹œ½½É‘¥¹…Ñ”°‰½½°¥¹™•ÉÉ•‘…Ñ”¤4(€€€€€€€ì4(€€€€€€€€€€€¥˜€¡¡½µ”¹%¹ÅÕ…±Ì¡…Ý…ä¹%°MÑÉ¥¹½µÁ…É¥Í½¸¹=É‘¥¹…±%¹½É•…Í”¤¤4(€€€€€€€€€€€ì4(€€€€€€€€€€€€€€€Ý…É¹¥¹Ì¹‘ ‰M­¥ÁÁ•í½½É‘¥¹…Ñ•ôè‰½Ñ Í¥‘•ÌÉ•Í½±Ù•Ñ¼í¡½µ”¹9…µ•ô¸ˆ¤ì4(€€€€€€€€€€€€€€€É•ÑÕÉ¸ì4(€€€€€€€€€€€ô4(4(€€€€€€€€€€€Ù…ÈÍ•µ…¹Ñ¥-•ä€ô€‰í‘…Ñ”éåååäµ54µ‘‘õñí¡½µ”¹%‘õñí…Ý…ä¹%‘õñí¡½µ•M½É•õñí…Ý…åM½É•õñíÁ¡…Í•õñíÉ½Õ¹‘ôˆì4(€€€€€€€€€€€¥˜€ …Í•µ…¹Ñ¥-•åÌ¹‘¡Í•µ…¹Ñ¥-•ä¤¤4(€€€€€€€€€€€ì4(€€€€€€€€€€€€€€€É•ÑÕÉ¸ì4(€€€€€€€€€€€ô4(4(€€€€€€€€€€€Ù…È¡…Í €ô½¹Ù•ÉÐ¹Q½!•áMÑÉ¥¹œ¡M!ÈÔØ¹!…Í¡…Ñ„¡¹½‘¥¹œ¹UQà¹•Ñ	åÑ•Ì ‰íÍ•…Í½¹õñí½½É‘¥¹…Ñ•õñíÍ•µ…¹Ñ¥-•åôˆ¤¤¥l¸¸ÈÁt¹Q½1½Ý•É%¹Ù…É¥…¹Ð ¤ì4(€€€€€€€€€€€Ù…È•á±ÕÍ¥½¹I•…Í½¸€ô¡½µ•M½É”€¬…Ý…åM½É”€ôô€È€˜˜€¡¡½µ•M½É”€ôô€Àñð…Ý…åM½É”€ôô€À¤4(€€€€€€€€€€€€€€€€ü€‰M½ÕÉ”É•Á½ÉÑÌ…¸…‘µ¥¹¥ÍÑÉ…Ñ¥Ù”€È´ÀÉ•ÍÕ±Ðì•á±Õ‘•™É½´1<¸ˆ4(€€€€€€€€€€€€€€€€è¹Õ±°ì4(€€€€€€€€€€€…µ•Ì¹‘¡¹•Ü	…Í­•Ñ‰…±±AÉ½Ù¥‘•É…µ” 4(€€€€€€€€€€€€€€€Í½ÕÉ”°4(€€€€€€€€€€€€€€€€‰íÍ½ÕÉ•…µ•%‘AÉ•™¥áôµí¡…Í¡ôˆ°4(€€€€€€€€€€€€€€€…Ñ•Q¥µ”¹MÁ•¥™å-¥¹¡‘…Ñ”¹…Ñ”°…Ñ•Q¥µ•-¥¹¹UÑŒ¤°4(€€€€€€€€€€€€€€€€‰™¥¹¥Í¡•ˆ°4(€€€€€€€€€€€€€€€¡½µ”¹%°4(€€€€€€€€€€€€€€€¡½µ”¹9…µ”°4(€€€€€€€€€€€€€€€…Ý…ä¹%°4(€€€€€€€€€€€€€€€…Ý…ä¹9…µ”°4(€€€€€€€€€€€€€€€¡½µ•M½É”°4(€€€€€€€€€€€€€€€…Ý…åM½É”°4(€€€€€€€€€€€€€€€€€€€¹•Ü	…Í­•Ñ‰…±±AÉ½Ù¥‘•É…µ•AÉ½Ù•¹…¹”¡Á…•UÉ°°Í•…Í½¸°™•Ñ¡•‘ÑUÑŒ°Á…ÉÍ•ÉY•ÉÍ¥½¸°É•Ù¥Í¥½¸¤°4(€€€€€€€€€€€€€€€•á±ÕÍ¥½¹I•…Í½¸°4(€€€€€€€€€€€€€€€Á¡…Í”°4(€€€€€€€€€€€€€€€É½Õ¹°4(€€€€€€€€€€€€€€€¡½µ”¹½Õ¹ÑÉå½‘”°4(€€€€€€€€€€€€€€€…Ý…ä¹½Õ¹ÑÉå½‘”¤¤ì4(4(€€€€€€€€€€€¥˜€¡¥¹™•ÉÉ•‘…Ñ”¤4(€€€€€€€€€€€ì4(€€€€€€€€€€€€€€€%¹™•ÉÉ•‘…Ñ•½Õ¹Ð¬¬ì4(€€€€€€€€€€€ô4(€€€€€€€ô4(€€€ô4)ô4(