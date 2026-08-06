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
        string sourceGameIdPrefix = "wiki-fiba",
        bool preserveRoundRobinMatrixHomeAway = false,
        bool parseUlebFinalFormats = false)
    {
        var startYear = ParseStartYear(season);
        var endYear = startYear + 1;
        var fallbackDate = ExtractInfoboxDate(wikitext, startYear, endYear)
            ?? new DateTime(startYear, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var accumulator = new GameAccumulator(season, pageUrl, fetchedAtUtc, revision, fallbackDate, warnings, source, parserVersion, sourceGameIdPrefix);

        ParseTwoLegTemplates(wikitext, startYear, endYear, accumulator);
        ParseLiteralTables(wikitext, startYear, endYear, accumulator);
        ParseMatrixTables(wikitext, startYear, endYear, accumulator, preserveRoundRobinMatrixHomeAway);
        if (parseUlebFinalFormats)
        {
            ParseUlebFinalTables(wikitext, startYear, endYear, accumulator);
            ParseUlebBracketTemplates(wikitext, startYear, endYear, accumulator);
        }
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
        string sourceGameIdPrefix = "wiki-fiba",
        bool preserveRoundRobinMatrixHomeAway = false)
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
                for (var columnIndex = preserveRoundRobinMatrixHomeAway ? 0 : rowIndex + 1; columnIndex < matrixColumns.Length; columnIndex++)
                {
                    if (!preserveRoundRobinMatrixHomeAway && columnIndex <= rowIndex)
                    {
                        continue;
                    }

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
                        inferredDate: false);
                }
            }
        }

        warnings.Add($"Todor66 parsed {accumulator.Games.Count} distinct game-level result(s) for {season}.");
        return accumulator.Games
            .OrderBy(game => game.GameDateTimeUtc)
            .ThenBy(game => game.SourceGameId, StringComparer.Ordinal)
            .ToArray();
    }

    private static void ParseTwoLegTemplates(
        string wikitext,
        int startYear,
        int endYear,
        GameAccumulator accumulator)
    {
        var ordinal = 0;
        foreach (var match in ExtractTemplates(wikitext, "TwoLegResult"))
        {
            ordinal++;
            var values = SplitTopLevel(match.Body.TrimStart('|'), "|");
            var teamIndexes = values
                .Select((value, index) => new { Value = value, Index = index })
                .Where(item => !item.Value.Contains('=') && item.Value.Contains("[[", StringComparison.Ordinal))
                .Select(item => item.Index)
                .Take(2)
                .ToArray();
            if (teamIndexes.Length < 2)
            {
                continue;
            }

            var firstTeam = ParseTeam(values[teamIndexes[0]], values.ElementAtOrDefault(teamIndexes[0] + 1));
            var secondTeam = ParseTeam(values[teamIndexes[1]], values.ElementAtOrDefault(teamIndexes[1] + 1));
            var legScores = values
                .Skip(teamIndexes[1] + 1)
                .Where(value => !value.Contains('=') && TryParseScorePair(value, out _))
                .Take(2)
                .Select(value => { TryParseScorePair(value, out var score); return score; })
                .ToArray();
            if (firstTeam is null || secondTeam is null || legScores.Length == 0)
            {
                continue;
            }

            var context = FindContext(wikitext, match.Index, startYear, endYear, accumulator.FallbackDate);
            var firstDate = context.Dates.Count > 0 ? context.Dates[0] : accumulator.NextFallbackDate();
            accumulator.Add(firstTeam, secondTeam, legScores[0].Home, legScores[0].Away, firstDate, context.Phase, context.Round, $"twoleg-{ordinal}-1", context.Dates.Count == 0);
            if (legScores.Length > 1)
            {
                var secondDate = context.Dates.Count > 1 ? context.Dates[1] : accumulator.NextFallbackDate();
                accumulator.Add(secondTeam, firstTeam, legScores[1].Away, legScores[1].Home, secondDate, context.Phase, context.Round, $"twoleg-{ordinal}-2", context.Dates.Count < 2);
            }
        }
    }

    private static void ParseThreeLegTemplates(
        string wikitext,
        int startYear,
        int endYear,
        GameAccumulator accumulator)
    {
        var ordinal = 0;
        foreach (var match in ExtractTemplates(wikitext, "ThreeLegResult"))
        {
            ordinal++;
            var values = SplitTopLevel(match.Body.TrimStart('|'), "|");
            var teamIndexes = values
                .Select((value, index) => new { Value = value, Index = index })
                .Where(item => !item.Value.Contains('=') && item.Value.Contains("[[", StringComparison.Ordinal))
                .Select(item => item.Index)
                .Take(2)
                .ToArray();
            if (teamIndexes.Length < 2)
            {
                continue;
            }

            var firstTeam = ParseTeam(values[teamIndexes[0]], values.ElementAtOrDefault(teamIndexes[0] + 1));
            var secondTeam = ParseTeam(values[teamIndexes[1]], values.ElementAtOrDefault(teamIndexes[1] + 1));
            var scores = values
                .Skip(teamIndexes[1] + 1)
                .Where(value => !value.Contains('=') && TryParseScorePair(value, out _))
                .Take(3)
                .Select(value => { TryParseScorePair(value, out var score); return score; })
                .ToArray();
            if (firstTeam is null || secondTeam is null || scores.Length == 0)
            {
                continue;
            }

            var context = FindContext(wikitext, match.Index, startYear, endYear, accumulator.FallbackDate);
            for (var scoreIndex = 0; scoreIndex < scores.Length; scoreIndex++)
            {
                var date = context.Dates.ElementAtOrDefault(scoreIndex);
                var inferredDate = date == default;
                date = inferredDate ? accumulator.NextFallbackDate() : date;
                var score = scores[scoreIndex];
                var home = scoreIndex % 2 == 0 ? firstTeam : secondTeam;
                var away = scoreIndex % 2 == 0 ? secondTeam : firstTeam;
                var homeScore = scoreIndex % 2 == 0 ? score.Home : score.Away;
                var awayScore = scoreIndex % 2 == 0 ? score.Away : score.Home;
                accumulator.Add(home, away, homeScore, awayScore, date, context.Phase, context.Round, $"threeleg-{ordinal}-{scoreIndex + 1}", inferredDate);
            }
        }
    }

    private static void ParseTieBreakNotes(
        string wikitext,
        int startYear,
        int endYear,
        GameAccumulator accumulator)
    {
        var ordinal = 0;
        foreach (Match match in Regex.Matches(
            wikitext,
            @"(?is)(?:partido\s+de\s+desempate|tercer\s+partido).*?(?<home>\[\[[^\]]+\]\])\s*-\s*(?<away>\[\[[^\]]+\]\])\s+(?<score>\d{1,3}\s*[-Ã¢â‚¬â€œÃ¢â‚¬â€]\s*\d{1,3})",
            RegexOptions.IgnoreCase))
        {
            if (!TryParseScorePair(match.Groups["score"].Value, out var score))
            {
                continue;
            }

            var home = ParseTeam(match.Groups["home"].Value, null);
            var away = ParseTeam(match.Groups["away"].Value, null);
            if (home is null || away is null)
            {
                continue;
            }

            ordinal++;
            var context = FindContext(wikitext, match.Index, startYear, endYear, accumulator.FallbackDate);
            var date = ExtractDates(match.Value, startYear, endYear).FirstOrDefault();
            var inferredDate = date == default;
            date = inferredDate ? accumulator.NextFallbackDate() : date;
            accumulator.Add(home, away, score.Home, score.Away, date, context.Phase, context.Round, $"tiebreak-{ordinal}", inferredDate);
        }
    }

    private static void ParseTieBreakNotesReliable(
        string wikitext,
        int startYear,
        int endYear,
        GameAccumulator accumulator)
    {
        var ordinal = 0;
        const string scorePattern = @"\d{1,3}\s*[^0-9\s]\s*\d{1,3}";
        var pattern = $@"(?is)\bun\s+partido\s+de\s+desempate.*?(?<home>\[\[[^\]]+\]\])\s*-\s*(?<away>\[\[[^\]]+\]\])\s+(?<score>{scorePattern})";
        foreach (Match match in Regex.Matches(wikitext, pattern, RegexOptions.IgnoreCase))
        {
            if (!TryParseScorePair(match.Groups["score"].Value, out var score))
            {
                continue;
            }

            var home = ParseTeam(match.Groups["home"].Value, null);
            var away = ParseTeam(match.Groups["away"].Value, null);
            if (home is null || away is null)
            {
                continue;
            }

            ordinal++;
            var context = FindContext(wikitext, match.Index, startYear, endYear, accumulator.FallbackDate);
            var date = ExtractDates(match.Value, startYear, endYear).FirstOrDefault();
            var inferredDate = date == default;
            date = inferredDate ? accumulator.NextFallbackDate() : date;
            accumulator.Add(home, away, score.Home, score.Away, date, context.Phase, context.Round, $"tiebreak-reliable-{ordinal}", inferredDate);
        }
    }

    private static void ParseLiteralTables(
        string wikitext,
        int startYear,
        int endYear,
        GameAccumulator accumulator)
    {
        var tableOrdinal = 0;
        foreach (Match tableMatch in Regex.Matches(wikitext, @"(?ms)^\{\|(?<body>.*?)^\|\}"))
        {
            tableOrdinal++;
            var context = FindContext(wikitext, tableMatch.Index, startYear, endYear, accumulator.FallbackDate);
            var rowOrdinal = 0;
            foreach (var cells in ParseTableRows(tableMatch.Groups["body"].Value))
            {
                rowOrdinal++;
                // Historical Wikipedia articles use: team, aggregate, team,
                // first leg, second leg. Standings and roster tables do not fit.
                if (cells.Count < 5)
                {
                    continue;
                }

                // Group-stage score matrices also have five or more cells, but
                // their third cell is the first score rather than the second
                // team. Treating that score as a team creates identities such as
                // "77-88" and then emits two fabricated two-leg games.
                if (IsScoreLikeTeamName(cells[0]) || IsScoreLikeTeamName(cells[2]))
                {
                    continue;
                }

                var firstTeam = ParseTeam(cells[0], null);
                var secondTeam = ParseTeam(cells[2], null);
                if (firstTeam is null || secondTeam is null ||
                    !TryParseScorePair(cells[3], out var firstLeg) ||
                    !TryParseScorePair(cells[4], out var secondLeg))
                {
                    continue;
                }

                var firstDate = context.Dates.Count > 0 ? context.Dates[0] : accumulator.NextFallbackDate();
                var secondDate = context.Dates.Count > 1 ? context.Dates[1] : accumulator.NextFallbackDate();
                accumulator.Add(firstTeam, secondTeam, firstLeg.Home, firstLeg.Away, firstDate, context.Phase, context.Round, $"table-{tableOrdinal}-{rowOrdinal}-1", context.Dates.Count == 0);
                accumulator.Add(secondTeam, firstTeam, secondLeg.Away, secondLeg.Home, secondDate, context.Phase, context.Round, $"table-{tableOrdinal}-{rowOrdinal}-2", context.Dates.Count < 2);
            }
        }
    }

    private static void ParseFinalTemplates(
        string wikitext,
        int startYear,
        int endYear,
        GameAccumulator accumulator)
    {
        var ordinal = 0;
        foreach (var match in ExtractTemplates(wikitext, "Partido de baloncesto"))
        {
            ordinal++;
            var values = SplitTopLevel(match.Body.TrimStart('|'), "|")
                .Select(value =>
                {
                    var separator = FindTopLevelDelimiter(value, "=");
                    return separator < 0
                        ? (Name: string.Empty, Value: value)
                        : (Name: value[..separator].Trim(), Value: value[(separator + 1)..].Trim());
                })
                .Where(pair => pair.Name.Length > 0)
                .ToDictionary(pair => pair.Name, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

            if (!values.TryGetValue("team1", out var team1Raw) || !values.TryGetValue("team2", out var team2Raw) ||
                !values.TryGetValue("score1", out var score1Raw) || !values.TryGetValue("score2", out var score2Raw) ||
                !TryParseSingleScore(score1Raw, out var score1) || !TryParseSingleScore(score2Raw, out var score2))
            {
                continue;
            }

            var team1 = ParseTeam(team1Raw, null);
            var team2 = ParseTeam(team2Raw, null);
            if (team1 is null || team2 is null)
            {
                continue;
            }

            var date = values.TryGetValue("date", out var dateRaw) && TryParseSpanishDate(dateRaw, startYear, endYear, out var parsedDate)
                ? parsedDate
                : accumulator.NextFallbackDate();
            accumulator.Add(team1, team2, score1, score2, date, "Final", "Final", $"final-{ordinal}", !values.ContainsKey("date"));
        }
    }

    private static void ParseMatrixTables(
        string wikitext,
        int startYear,
        int endYear,
        GameAccumulator accumulator,
        bool preserveRoundRobinMatrixHomeAway)
    {
        var tableOrdinal = 0;
        foreach (Match tableMatch in Regex.Matches(wikitext, @"(?ms)^\{\|(?<body>.*?)^\|\}"))
        {
            tableOrdinal++;
            var rows = ParseTableRows(tableMatch.Groups["body"].Value);
            var headerIndex = rows
                .Select((row, index) => new { Row = row, Index = index })
                .FirstOrDefault(item =>
                    (item.Row.Any(cell => CleanWikiText(cell).Equals("Team", StringComparison.OrdinalIgnoreCase)) &&
                        item.Row.Any(cell => CleanWikiText(cell).Equals("Qualification", StringComparison.OrdinalIgnoreCase))) ||
                    IsCompactScoreMatrixHeader(item.Row))
                ?.Index;
            if (headerIndex is null)
            {
                continue;
            }

            var header = rows[headerIndex.Value]
                .Where(cell => !CleanWikiText(cell).StartsWith("width=", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var standardMatrix = header.Any(cell => CleanWikiText(cell).Equals("Team", StringComparison.OrdinalIgnoreCase)) &&
                header.Any(cell => CleanWikiText(cell).Equals("Qualification", StringComparison.OrdinalIgnoreCase));
            var teamColumn = standardMatrix
                ? Array.FindIndex(header, cell => CleanWikiText(cell).Equals("Team", StringComparison.OrdinalIgnoreCase))
                : 0;
            var qualificationColumn = standardMatrix
                ? Array.FindIndex(header, cell => CleanWikiText(cell).Equals("Qualification", StringComparison.OrdinalIgnoreCase))
                : 0;
            var matrixColumns = header
                .Select((cell, index) => new { Cell = cell, Index = index })
                .Where(item => item.Index > (standardMatrix ? qualificationColumn : teamColumn) && !string.IsNullOrWhiteSpace(CleanWikiText(item.Cell)))
                .ToArray();
            if (matrixColumns.Length < 3)
            {
                continue;
            }

            var dataRows = rows
                .Skip(headerIndex.Value + 1)
                .Select(row => new
                {
                    Row = row,
                    Team = row.Count > teamColumn ? ParseTeam(row[teamColumn], null) : null
                })
                .Where(item => item.Team is not null &&
                    matrixColumns.Count(column => column.Index < item.Row.Count &&
                        (TryParseScorePair(item.Row[column.Index], out _) || IsMatrixBye(item.Row[column.Index]))) >= 2)
                .Take(matrixColumns.Length)
                .ToArray();
            if (dataRows.Length != matrixColumns.Length)
            {
                continue;
            }

            var context = FindContext(wikitext, tableMatch.Index, startYear, endYear, accumulator.FallbackDate);
            for (var rowIndex = 0; rowIndex < dataRows.Length; rowIndex++)
            {
                for (var columnIndex = preserveRoundRobinMatrixHomeAway ? 0 : rowIndex + 1; columnIndex < matrixColumns.Length; columnIndex++)
                {
                    if (!preserveRoundRobinMatrixHomeAway && columnIndex <= rowIndex)
                    {
                        continue;
                    }

                    var scoreCell = dataRows[rowIndex].Row[matrixColumns[columnIndex].Index];
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
                        context.Phase,
                        context.Round,
                        $"matrix-{tableOrdinal}-{rowIndex + 1}-{columnIndex + 1}",
                        inferredDate: true);
                }
            }
        }
    }

    private static bool IsCompactScoreMatrixHeader(IReadOnlyList<string> row)
    {
        var cells = row
            .Where(cell => !CleanWikiText(cell).StartsWith("width=", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return cells.Length >= 4 &&
            string.IsNullOrWhiteSpace(CleanWikiText(cells[0])) &&
            cells.Skip(1).All(cell => !string.IsNullOrWhiteSpace(CleanWikiText(cell)));
    }

    private static void ParseUlebFinalTables(
        string wikitext,
        int startYear,
        int endYear,
        GameAccumulator accumulator)
    {
        var tableOrdinal = 0;
        foreach (Match tableMatch in Regex.Matches(wikitext, @"(?ms)^\{\|(?<body>.*?)^\|\}"))
        {
            tableOrdinal++;
            var context = FindContext(wikitext, tableMatch.Index, startYear, endYear, accumulator.FallbackDate);
            if (!context.Phase.Contains("final", StringComparison.OrdinalIgnoreCase) &&
                !context.Round.Contains("final", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var rowOrdinal = 0;
            foreach (var cells in ParseTableRows(tableMatch.Groups["body"].Value))
            {
                rowOrdinal++;
                if (cells.Count < 5 || !TryParseScorePair(cells[^1], out var score))
                {
                    continue;
                }

                var home = ParseTeam(cells[2], null);
                var away = ParseTeam(cells[3], null);
                if (home is null || away is null)
                {
                    continue;
                }

                var date = ExtractDates(cells[1], startYear, endYear).FirstOrDefault();
                var inferredDate = date == default;
                date = inferredDate ? accumulator.NextFallbackDate() : date;
                accumulator.Add(home, away, score.Home, score.Away, date, context.Phase, context.Round, $"uleb-final-table-{tableOrdinal}-{rowOrdinal}", inferredDate);
            }
        }
    }

    private static void ParseUlebBracketTemplates(
        string wikitext,
        int startYear,
        int endYear,
        GameAccumulator accumulator)
    {
        var ordinal = 0;
        foreach (var template in ExtractTemplates(wikitext, "Turnierplan8"))
        {
            ordinal++;
            var parameters = SplitTopLevel(template.Body.TrimStart('|'), "|")
                .Select(value =>
                {
                    var separator = FindTopLevelDelimiter(value, "=");
                    return separator < 0
                        ? (Name: string.Empty, Value: string.Empty)
                        : (Name: value[..separator].Trim(), Value: value[(separator + 1)..].Trim());
                })
                .Where(pair => pair.Name.Length > 0)
                .ToDictionary(pair => pair.Name, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

            AddUlebBracketRound(parameters, "RD1", "Quarterfinals", [1, 3, 5, 7], accumulator, ordinal);
            AddUlebBracketRound(parameters, "RD2", "Semifinals", [1, 3], accumulator, ordinal);
            AddUlebBracketRound(parameters, "RD3", "Final", [1], accumulator, ordinal);
            AddUlebBracketRound(parameters, "RD3", "Third-place game", [3], accumulator, ordinal);
        }
    }

    private static void AddUlebBracketRound(
        IReadOnlyDictionary<string, string> parameters,
        string prefix,
        string round,
        IReadOnlyList<int> pairStarts,
        GameAccumulator accumulator,
        int ordinal)
    {
        foreach (var pairStart in pairStarts)
        {
            if (!parameters.TryGetValue($"{prefix}-team{pairStart}", out var homeRaw) ||
                !parameters.TryGetValue($"{prefix}-team{pairStart + 1}", out var awayRaw) ||
                !parameters.TryGetValue($"{prefix}-score{pairStart}", out var homeScoreRaw) ||
                !parameters.TryGetValue($"{prefix}-score{pairStart + 1}", out var awayScoreRaw) ||
                !TryParseSingleScore(homeScoreRaw, out var homeScore) ||
                !TryParseSingleScore(awayScoreRaw, out var awayScore))
            {
                continue;
            }

            var home = ParseTeam(homeRaw, null);
            var away = ParseTeam(awayRaw, null);
            if (home is null || away is null)
            {
                continue;
            }

            accumulator.Add(home, away, homeScore, awayScore, accumulator.NextFallbackDate(), "Final 8 tournament", round, $"uleb-bracket-{ordinal}-{prefix}-{pairStart}", inferredDate: true);
        }
    }

    private static void ParseSportsTableTemplates(
        string wikitext,
        int startYear,
        int endYear,
        GameAccumulator accumulator)
    {
        var templateOrdinal = 0;
        foreach (var match in ExtractTemplates(wikitext, "#invoke:Sports table"))
        {
            templateOrdinal++;
            var parameters = SplitTopLevel(match.Body.TrimStart('|'), "|")
                .Select(value =>
                {
                    var separator = FindTopLevelDelimiter(value, "=");
                    return separator < 0
                        ? (Name: string.Empty, Value: value)
                        : (Name: value[..separator].Trim(), Value: value[(separator + 1)..].Trim());
                })
                .Where(pair => pair.Name.Length > 0)
                .ToDictionary(pair => pair.Name, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
            if (!parameters.TryGetValue("team_order", out var teamOrderRaw))
            {
                continue;
            }

            var teamCodes = teamOrderRaw
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToArray();
            var teams = teamCodes
                .Select(code => parameters.TryGetValue($"name_{code}", out var raw) ? ParseTeam(raw, null) : null)
                .ToArray();
            if (teams.Any(team => team is null))
            {
                continue;
            }

            var context = FindContext(wikitext, match.Index, startYear, endYear, accumulator.FallbackDate);
            for (var homeIndex = 0; homeIndex < teamCodes.Length; homeIndex++)
            {
                for (var awayIndex = homeIndex + 1; awayIndex < teamCodes.Length; awayIndex++)
                {
                    if (!parameters.TryGetValue($"match_{teamCodes[homeIndex]}_{teamCodes[awayIndex]}", out var scoreRaw) ||
                        !TryParseScorePair(scoreRaw, out var score))
                    {
                        continue;
                    }

                    accumulator.Add(
                        teams[homeIndex]!,
                        teams[awayIndex]!,
                        score.Home,
                        score.Away,
                        accumulator.NextFallbackDate(),
                        context.Phase,
                        context.Round,
                        $"sports-table-{templateOrdinal}-{homeIndex + 1}-{awayIndex + 1}",
                        inferredDate: true);
                }
            }
        }
    }

    private static bool IsMatrixBye(string value)
    {
        var cleaned = CleanWikiText(value);
        return cleaned is "â€”" or "â€“" or "Ã¢â‚¬â€" or "Ã¢â‚¬â€œ" or "-" or "bye" or "BYE";
    }

    private static IReadOnlyList<HtmlNode> GetHtmlCells(HtmlNode row)
        => row.SelectNodes("./th|./td")?.ToArray() ?? [];

    private static string CleanHtmlText(HtmlNode cell)
        => HtmlEntity.DeEntitize(Regex.Replace(cell.InnerText, @"\s+", " ")).Trim();

    private static bool IsTodorScore(string value)
        => value.Equals("wo", StringComparison.OrdinalIgnoreCase) || TryParseScorePair(value, out _);

    private static bool IsCountryCode(string? value)
        => !string.IsNullOrWhiteSpace(value) && value.Trim().Length is >= 2 and <= 3 && value.Trim().All(char.IsLetter);

    private static bool TryParseTodorDates(
        string value,
        int startYear,
        ref int? monthHint,
        out IReadOnlyList<DateTime> dates)
    {
        var parsed = new List<DateTime>();
        var tokens = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        int? firstDay = null;
        for (var tokenIndex = 0; tokenIndex < tokens.Length; tokenIndex++)
        {
            var token = tokens[tokenIndex];
            var match = Regex.Match(token, @"^(?<day>\d{1,2})(?:\.(?<month>\d{1,2}))?$");
            if (!match.Success)
            {
                dates = [];
                return false;
            }

            var day = int.Parse(match.Groups["day"].Value, CultureInfo.InvariantCulture);
            var month = match.Groups["month"].Success
                ? int.Parse(match.Groups["month"].Value, CultureInfo.InvariantCulture)
                : monthHint ?? 9;
            if (!match.Groups["month"].Success && tokenIndex > 0 && firstDay.HasValue && day < firstDay.Value && month < 12)
            {
                month++;
            }

            var year = month >= 9 ? startYear : startYear + 1;
            if (month is >= 1 and <= 12 && day <= DateTime.DaysInMonth(year, month))
            {
                parsed.Add(new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc));
            }

            monthHint = month;
            firstDay ??= day;
        }

        dates = parsed.Distinct().ToArray();
        return dates.Count > 0;
    }

    private static TeamRef? ParseHtmlTeam(HtmlNode cell)
    {
        var anchor = cell.SelectNodes(".//a[@href and not(contains(@href, 'File:'))]")
            ?.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate.InnerText));
        var name = anchor is null ? CleanHtmlText(cell) : HtmlEntity.DeEntitize(Regex.Replace(anchor.InnerText, @"\s+", " ")).Trim();
        return string.IsNullOrWhiteSpace(name) ? null : ParseTeam($"[[{name}]]", null);
    }

    private static IReadOnlyList<IReadOnlyList<string>> ParseTableRows(string body)
    {
        var rows = new List<IReadOnlyList<string>>();
        foreach (var row in Regex.Split(body, @"(?m)^\|-.*$"))
        {
            var cells = new List<string>();
            foreach (var rawLine in row.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || (line[0] != '|' && line[0] != '!'))
                {
                    continue;
                }

                var delimiter = line[0] == '!' ? "!!" : "||";
                var lineBody = line[1..];
                var values = SplitTopLevel(lineBody, delimiter);
                if (values.Count == 1 && !lineBody.TrimStart().StartsWith("style", StringComparison.OrdinalIgnoreCase) &&
                    !lineBody.TrimStart().StartsWith("align", StringComparison.OrdinalIgnoreCase) &&
                    SplitTopLevel(lineBody, "|").Count > 1)
                {
                    values = SplitTopLevel(lineBody, "|");
                }

                foreach (var value in values)
                {
                    cells.Add(StripCellAttributes(value));
                }
            }

            if (cells.Count > 0)
            {
                rows.Add(cells);
            }
        }

        return rows;
    }

    private static string StripCellAttributes(string value)
    {
        var separator = FindTopLevelDelimiter(value.Trim(), "|");
        if (separator >= 0)
        {
            var prefix = value[..separator];
            if (prefix.Contains('=') || prefix.Contains("style", StringComparison.OrdinalIgnoreCase) || prefix.Contains("align", StringComparison.OrdinalIgnoreCase))
            {
                return value[(separator + 1)..].Trim();
            }
        }

        return value.Trim();
    }

    private static PageContext FindContext(string text, int index, int startYear, int endYear, DateTime fallbackDate)
    {
        var prefix = text[..Math.Min(index, text.Length)];
        var headings = Regex.Matches(prefix, @"(?m)^(?<marks>={2,4})\s*(?<text>.*?)\s*\k<marks>$").Cast<Match>().ToList();
        var phaseHeading = headings.LastOrDefault(match => match.Groups["marks"].Value.Length == 2);
        var roundHeading = headings.LastOrDefault(match => match.Groups["marks"].Value.Length >= 3) ?? phaseHeading;
        var phase = phaseHeading is null ? "Final phase" : CleanWikiText(phaseHeading.Groups["text"].Value);
        var round = roundHeading is null ? "Published results" : CleanWikiText(roundHeading.Groups["text"].Value);
        var contextStart = roundHeading?.Index ?? phaseHeading?.Index ?? Math.Max(0, index - 500);
        var dates = ExtractDates(text[contextStart..Math.Min(index, text.Length)], startYear, endYear);
        return new PageContext(phase, round, dates);
    }

    private static DateTime? ExtractInfoboxDate(string text, int startYear, int endYear)
    {
        var match = Regex.Match(text, @"(?im)^\|\s*duration\s*=\s*(?<value>.+)$");
        if (!match.Success)
        {
            return null;
        }

        var date = ExtractDates(match.Groups["value"].Value, startYear, endYear).FirstOrDefault();
        return date == default ? null : date;
    }

    private static IReadOnlyList<DateTime> ExtractDates(string text, int startYear, int endYear)
    {
        var cleaned = CleanWikiText(text);
        var dates = new List<DateTime>();
        foreach (Match match in Regex.Matches(cleaned, @"(?<!\d)(?<day>\d{1,2})[./-](?<month>\d{1,2})[./-](?<year>(?:19|20)\d{2})(?!\d)"))
        {
            if (int.TryParse(match.Groups["day"].Value, out var day) &&
                int.TryParse(match.Groups["month"].Value, out var month) &&
                int.TryParse(match.Groups["year"].Value, out var year) &&
                month is >= 1 and <= 12 &&
                day <= DateTime.DaysInMonth(year, month))
            {
                dates.Add(new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc));
            }
        }
        foreach (Match match in Regex.Matches(cleaned, @"(?<!\d)(?<day>\d{1,2})\s+(?:(?:de)\s+)?(?<month>[A-Za-zÃ¡Ã©Ã­Ã³Ãº]+)(?:\s+(?:(?:de)\s+)?(?<year>(?:19|20)\d{2}))?", RegexOptions.IgnoreCase))
        {
            if (!MonthNumbers.TryGetValue(match.Groups["month"].Value, out var month) || !int.TryParse(match.Groups["day"].Value, out var day))
            {
                continue;
            }

            var year = int.TryParse(match.Groups["year"].Value, out var explicitYear)
                ? explicitYear
                : month is "06" or "07" or "08" ? endYear : startYear;
            if (day <= DateTime.DaysInMonth(year, int.Parse(month, CultureInfo.InvariantCulture)))
            {
                dates.Add(new DateTime(year, int.Parse(month, CultureInfo.InvariantCulture), day, 0, 0, 0, DateTimeKind.Utc));
            }
        }

        return dates.Distinct().ToArray();
    }

    private static TeamRef? ParseTeam(string raw, string? countryCode)
    {
        var link = Regex.Match(raw, @"\[\[(?<target>[^|\]]+)(?:\|(?<display>[^\]]+))?\]\]");
        var name = CleanWikiText(raw).Trim(' ', '*', '\'', '.');
        if (string.IsNullOrWhiteSpace(name) || name is "-" or "bye" || name.Contains("{{", StringComparison.Ordinal) || IsScoreLikeTeamName(name))
        {
            return null;
        }

        var canonical = link.Success ? link.Groups["target"].Value : name;
        var id = Slugify(canonical);
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var country = string.IsNullOrWhiteSpace(countryCode) ? null : CleanWikiText(countryCode).ToUpperInvariant();
        return new TeamRef($"wiki-team:{id}", name, country);
    }

    private static bool IsScoreLikeTeamName(string value)
        => Regex.IsMatch(
            value.Trim(),
            @"^\d{1,3}\s*[^0-9\s]\s*\d{1,3}$",
            RegexOptions.CultureInvariant);

    private static string CleanWikiText(string value)
    {
        var cleaned = Regex.Replace(value, @"<ref\b[^>]*>.*?</ref>|<ref\b[^>]*/>", string.Empty, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        cleaned = Regex.Replace(cleaned, @"\{\{[^{}]*\}\}", string.Empty);
        cleaned = Regex.Replace(cleaned, @"\[\[(?<target>[^|\]]+)(?:\|(?<display>[^\]]+))?\]\]", match => match.Groups["display"].Success ? match.Groups["display"].Value : match.Groups["target"].Value);
        cleaned = Regex.Replace(cleaned, "<[^>]+>", " ");
        cleaned = cleaned.Replace("'''", string.Empty, StringComparison.Ordinal).Replace("''", string.Empty, StringComparison.Ordinal).Replace("&nbsp;", " ", StringComparison.OrdinalIgnoreCase);
        return Regex.Replace(cleaned, @"\s+", " ").Trim();
    }

    private static string Slugify(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        var dash = false;
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                dash = false;
            }
            else if (!dash && builder.Length > 0)
            {
                builder.Append('-');
                dash = true;
            }
        }

        return builder.ToString().Trim('-');
    }

    private static bool TryParseScorePair(string value, out (short Home, short Away) score)
    {
        score = default;
        var cleaned = CleanWikiText(value).Replace("*", string.Empty, StringComparison.Ordinal);
        var match = Regex.Match(cleaned, @"(?<!\d)(?<home>\d{1,3})\s*[^0-9\s]\s*(?<away>\d{1,3})(?!\d)");
        if (!match.Success || !short.TryParse(match.Groups["home"].Value, out var home) || !short.TryParse(match.Groups["away"].Value, out var away))
        {
            return false;
        }

        score = (home, away);
        return true;
    }

    private static bool TryParseSingleScore(string value, out short score)
    {
        score = default;
        var match = Regex.Match(CleanWikiText(value), @"(?<!\d)\d{1,3}(?!\d)");
        return match.Success && short.TryParse(match.Value, out score);
    }

    private static bool TryParseSpanishDate(string value, int startYear, int endYear, out DateTime date)
    {
        date = default;
        var parsed = ExtractDates(value, startYear, endYear).FirstOrDefault();
        if (parsed == default)
        {
            return false;
        }

        date = parsed;
        return true;
    }

    private static List<string> SplitTopLevel(string value, string delimiter)
    {
        var result = new List<string>();
        var start = 0;
        while (start <= value.Length)
        {
            var relative = FindTopLevelDelimiter(value[start..], delimiter);
            if (relative < 0)
            {
                result.Add(value[start..].Trim());
                break;
            }

            result.Add(value.Substring(start, relative).Trim());
            start += relative + delimiter.Length;
        }

        return result;
    }

    private static int FindTopLevelDelimiter(string value, string delimiter)
    {
        var braces = 0;
        var links = 0;
        for (var index = 0; index <= value.Length - delimiter.Length; index++)
        {
            if (value.AsSpan(index).StartsWith("{{")) { braces++; index++; continue; }
            if (value.AsSpan(index).StartsWith("}}")) { braces = Math.Max(0, braces - 1); index++; continue; }
            if (value.AsSpan(index).StartsWith("[[")) { links++; index++; continue; }
            if (value.AsSpan(index).StartsWith("]]")) { links = Math.Max(0, links - 1); index++; continue; }
            if (braces == 0 && links == 0 && value.AsSpan(index, delimiter.Length).SequenceEqual(delimiter)) return index;
        }

        return -1;
    }

    private static IReadOnlyCollection<TemplateMatch> ExtractTemplates(string text, string templateName)
    {
        var matches = new List<TemplateMatch>();
        var needle = "{{" + templateName;
        var searchStart = 0;
        while (searchStart < text.Length)
        {
            var start = text.IndexOf(needle, searchStart, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                break;
            }

            var depth = 0;
            var end = -1;
            for (var index = start; index < text.Length - 1; index++)
            {
                if (text.AsSpan(index, 2).SequenceEqual("{{"))
                {
                    depth++;
                    index++;
                }
                else if (text.AsSpan(index, 2).SequenceEqual("}}"))
                {
                    depth--;
                    index++;
                    if (depth == 0)
                    {
                        end = index - 1;
                        break;
                    }
                }
            }

            if (end < 0)
            {
                break;
            }

            matches.Add(new TemplateMatch(start, text[(start + needle.Length)..end]));
            searchStart = end + 1;
        }

        return matches;
    }

    private static int ParseStartYear(string season)
    {
        var match = Regex.Match(season, @"\b(19|20)\d{2}\b");
        return match.Success ? int.Parse(match.Value, CultureInfo.InvariantCulture) : throw new ArgumentException($"Season '{season}' has no four-digit year.", nameof(season));
    }

    private sealed record PageContext(string Phase, string Round, IReadOnlyList<DateTime> Dates);
    private sealed record TemplateMatch(int Index, string Body);
    private sealed record TeamRef(string Id, string Name, string? CountryCode);

    private sealed class GameAccumulator(
        string season,
        string pageUrl,
        DateTime fetchedAtUtc,
        string revision,
        DateTime fallbackDate,
        ICollection<string> warnings,
        string source,
        string parserVersion,
        string sourceGameIdPrefix)
    {
        private readonly HashSet<string> semanticKeys = new(StringComparer.Ordinal);
        private int fallbackOrdinal;

        public List<BasketballProviderGame> Games { get; } = [];
        public int InferredDateCount { get; private set; }
        public DateTime FallbackDate { get; } = fallbackDate;

        public DateTime NextFallbackDate()
            => DateTime.SpecifyKind(FallbackDate.Date.AddDays(fallbackOrdinal++), DateTimeKind.Utc);

        public void Add(TeamRef home, TeamRef away, short homeScore, short awayScore, DateTime date, string phase, string round, string coordinate, bool inferredDate)
        {
            if (home.Id.Equals(away.Id, StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add($"Skipped {coordinate}: both sides resolved to {home.Name}.");
                return;
            }

            var semanticKey = $"{date:yyyy-MM-dd}|{home.Id}|{away.Id}|{homeScore}|{awayScore}|{phase}|{round}";
            if (!semanticKeys.Add(semanticKey))
            {
                return;
            }

            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{season}|{coordinate}|{semanticKey}")))[..20].ToLowerInvariant();
            var exclusionReason = homeScore + awayScore == 2 && (homeScore == 0 || awayScore == 0)
                ? "Source reports an administrative 2-0 result; excluded from ELO."
                : null;
            Games.Add(new BasketballProviderGame(
                source,
                $"{sourceGameIdPrefix}-{hash}",
                DateTime.SpecifyKind(date.Date, DateTimeKind.Utc),
                "finished",
                home.Id,
                home.Name,
                away.Id,
                away.Name,
                homeScore,
                awayScore,
                    new BasketballProviderGameProvenance(pageUrl, season, fetchedAtUtc, parserVersion, revision),
                exclusionReason,
                phase,
                round,
                home.CountryCode,
                away.CountryCode));

            if (inferredDate)
            {
                InferredDateCount++;
            }
        }
    }
}
