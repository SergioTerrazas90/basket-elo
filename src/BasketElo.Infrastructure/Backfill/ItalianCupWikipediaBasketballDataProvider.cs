using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BasketElo.Domain.Backfill;
using HtmlAgilityPack;
using Microsoft.Extensions.Options;

namespace BasketElo.Infrastructure.Backfill;

/// <summary>
/// Reads the historical men's Coppa Italia edition pages from Italian
/// Wikipedia. The official LBA catalog lists these editions, but its calendars
/// are empty before 2008-2009; the edition articles retain the published game
/// results for every edition that was actually played in the historical range.
/// </summary>
public sealed class ItalianCupWikipediaBasketballDataProvider(
    HttpClient httpClient,
    IOptions<ItalianCupWikipediaOptions> options) : IBasketballDataProvider
{
    public const string Source = "wikipedia-italian-cup";
    public const string ParserVersion = "wikipedia-italian-cup-wikitext-v1";

    private static readonly HashSet<int> PlayedStartYears =
    [
        1967, 1968, 1969, 1970, 1971, 1972, 1973,
        1983, 1984, 1985, 1986, 1987, 1988, 1989, 1990, 1991, 1992, 1993,
        1994, 1995, 1996, 1997, 1998, 1999, 2000, 2001, 2002, 2003, 2004,
        2005, 2006, 2007
    ];

    private static readonly IReadOnlyDictionary<string, int> ItalianMonths =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["gennaio"] = 1,
            ["febbraio"] = 2,
            ["marzo"] = 3,
            ["aprile"] = 4,
            ["maggio"] = 5,
            ["giugno"] = 6,
            ["luglio"] = 7,
            ["agosto"] = 8,
            ["settembre"] = 9,
            ["ottobre"] = 10,
            ["novembre"] = 11,
            ["dicembre"] = 12
        };

    private const string MonthPattern =
        "gennaio|febbraio|marzo|aprile|maggio|giugno|luglio|agosto|settembre|ottobre|novembre|dicembre";

    public string SourceKey => Source;

    public Task<BasketballProviderLeague?> ResolveLeagueAsync(
        string country,
        string leagueName,
        BackfillExecutionContext context,
        CancellationToken cancellationToken)
    {
        var league = string.Equals(country, "Italy", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(leagueName, "Italian Cup", StringComparison.OrdinalIgnoreCase)
                ? new BasketballProviderLeague(Source, "COPPA_ITALIA", "Italian Cup", "IT", "start_year")
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
            !string.Equals(league.SourceLeagueId, "COPPA_ITALIA", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Italian Wikipedia provider only supports Italy: Italian Cup.");
        }

        var (startYear, endYear) = ParseSeason(season);
        if (!PlayedStartYears.Contains(startYear))
        {
            return ([], false, [$"No Italian Cup edition was played in {season}."]);
        }

        var requestedTitle = $"Coppa Italia di pallacanestro maschile {endYear}";
        if (!context.CanUseRequest())
        {
            return ([], false, [$"Wikipedia request budget reached before {requestedTitle} could be fetched."]);
        }

        context.ConsumeRequest();
        if (options.Value.MinRequestIntervalMilliseconds > 0)
        {
            await Task.Delay(options.Value.MinRequestIntervalMilliseconds, cancellationToken);
        }

        var path = "/w/api.php?action=query&format=json&formatversion=2&redirects=1" +
            "&prop=revisions&rvprop=ids%7Ctimestamp%7Ccontent&rvslots=main&maxlag=5" +
            $"&titles={Uri.EscapeDataString(requestedTitle)}";
        using var response = await httpClient.GetAsync(path, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var page = document.RootElement.GetProperty("query").GetProperty("pages")[0];
        if (page.TryGetProperty("missing", out _))
        {
            return ([], false, [$"Wikipedia page was not found: {requestedTitle}."]);
        }

        var resolvedTitle = page.GetProperty("title").GetString() ?? requestedTitle;
        var revision = page.GetProperty("revisions")[0];
        var revisionId = revision.GetProperty("revid").GetInt64().ToString(CultureInfo.InvariantCulture);
        var wikitext = revision.GetProperty("slots").GetProperty("main").GetProperty("content").GetString()
            ?? string.Empty;
        var pageUrl = $"https://it.wikipedia.org/wiki/{Uri.EscapeDataString(resolvedTitle).Replace("%20", "_", StringComparison.Ordinal)}";
        var warnings = new List<string>();
        var games = ParseGames(wikitext, season, pageUrl, DateTime.UtcNow, revisionId, warnings);
        if (games.Count == 0)
        {
            warnings.Add($"{season}: the published edition page produced no parseable game results.");
        }
        else
        {
            warnings.Add("Wikipedia supplies dates without tip-off times; imported times are 00:00 UTC.");
        }

        return (games, false, warnings);
    }

    internal static IReadOnlyCollection<BasketballProviderGame> ParseGames(
        string wikitext,
        string season,
        string pageUrl,
        DateTime fetchedAtUtc,
        string revision,
        ICollection<string> warnings)
    {
        var (startYear, endYear) = ParseSeason(season);
        var fallbackDate = ExtractInfoboxDate(wikitext, "data_inizio", startYear, endYear)
            ?? new DateTime(startYear, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var accumulator = new GameAccumulator(
            season,
            endYear,
            pageUrl,
            fetchedAtUtc,
            revision,
            warnings);

        ParseBracketGames(wikitext, startYear, endYear, fallbackDate, accumulator);
        ParseTwoLegTemplates(wikitext, startYear, endYear, fallbackDate, accumulator);
        ParseWikiTables(wikitext, startYear, endYear, fallbackDate, accumulator);
        ParseBulletGames(wikitext, startYear, endYear, fallbackDate, accumulator);

        warnings.Add(
            $"Parsed {accumulator.BracketCount} bracket, {accumulator.TwoLegTemplateCount} two-leg template, " +
            $"{accumulator.TableCount} table, and {accumulator.BulletCount} listed game(s)." );
        if (accumulator.InferredDateCount > 0)
        {
            warnings.Add(
                $"{accumulator.InferredDateCount} game(s) use a stage-level or deterministic fallback date because the source does not publish an exact game date.");
        }

        return accumulator.Games
            .OrderBy(game => game.GameDateTimeUtc)
            .ThenBy(game => game.SourceGameId, StringComparer.Ordinal)
            .ToArray();
    }

    private static void ParseBracketGames(
        string wikitext,
        int startYear,
        int endYear,
        DateTime fallbackDate,
        GameAccumulator accumulator)
    {
        var teams = new Dictionary<(int Round, int Slot), TeamRef>();
        var teamMatches = Regex.Matches(
                     wikitext,
                     @"\|\s*RD(?<round>\d+)-team(?<slot>\d+)\s*=\s*(?<value>.*?)(?=\|\s*RD\d+|\r?\n|\}\})",
                     RegexOptions.IgnoreCase | RegexOptions.Singleline);
        foreach (Match match in teamMatches)
        {
            var team = ParseTeam(match.Groups["value"].Value);
            if (team is not null)
            {
                teams[(int.Parse(match.Groups["round"].Value, CultureInfo.InvariantCulture),
                    int.Parse(match.Groups["slot"].Value, CultureInfo.InvariantCulture))] = team;
            }
        }

        var scores = new Dictionary<(int Round, int Slot, int Leg), short>();
        foreach (Match match in Regex.Matches(
                     wikitext,
                     @"\|\s*RD(?<round>\d+)-score(?<slot>\d+)(?:(?:-(?<leg>\d+))|(?<namedleg>firstleg|secondleg|aggregate))?\s*=\s*(?<value>.*?)(?=\|\s*RD\d+|\r?\n|\}\})",
                     RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            if (match.Groups["namedleg"].Value.Equals("aggregate", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (TryParseSingleScore(match.Groups["value"].Value, out var score))
            {
                var leg = match.Groups["leg"].Success
                    ? int.Parse(match.Groups["leg"].Value, CultureInfo.InvariantCulture)
                    : match.Groups["namedleg"].Value.Equals("firstleg", StringComparison.OrdinalIgnoreCase)
                        ? 1
                        : match.Groups["namedleg"].Value.Equals("secondleg", StringComparison.OrdinalIgnoreCase)
                            ? 2
                            : 0;
                scores[(
                    int.Parse(match.Groups["round"].Value, CultureInfo.InvariantCulture),
                    int.Parse(match.Groups["slot"].Value, CultureInfo.InvariantCulture),
                    leg)] = score;
            }
        }

        var roundLabels = Regex.Matches(wikitext, @"(?im)^\|\s*RD(?<round>\d+)\s*=\s*(?<value>.+)$")
            .Cast<Match>()
            .GroupBy(match => int.Parse(match.Groups["round"].Value, CultureInfo.InvariantCulture))
            .ToDictionary(
                group => group.Key,
                group => group.First().Groups["value"].Value);

        var maximumRound = teams.Keys.Select(key => key.Round).DefaultIfEmpty(0).Max();
        foreach (var round in teams.Keys.Select(key => key.Round).Distinct().OrderBy(value => value))
        {
            var label = roundLabels.GetValueOrDefault(round) ?? $"Round {round}";
            var roundName = roundLabels.ContainsKey(round)
                ? CleanRoundLabel(label)
                : InferBracketRoundName(round, maximumRound);
            var dates = ExtractDates(label, startYear, endYear);
            var slots = teams.Keys
                .Where(key => key.Round == round)
                .Select(key => key.Slot)
                .Distinct()
                .OrderBy(value => value)
                .ToList();

            foreach (var oddSlot in slots.Where(slot => slot % 2 == 1))
            {
                var evenSlot = oddSlot + 1;
                if (!teams.TryGetValue((round, oddSlot), out var firstTeam) ||
                    !teams.TryGetValue((round, evenSlot), out var secondTeam))
                {
                    continue;
                }

                var legs = scores.Keys
                    .Where(key => key.Round == round && (key.Slot == oddSlot || key.Slot == evenSlot))
                    .Select(key => key.Leg)
                    .Distinct()
                    .OrderBy(value => value)
                    .ToList();
                foreach (var leg in legs)
                {
                    if (!scores.TryGetValue((round, oddSlot, leg), out var firstScore) ||
                        !scores.TryGetValue((round, evenSlot, leg), out var secondScore))
                    {
                        continue;
                    }

                    var reverse = leg == 2;
                    var date = DateForLeg(dates, fallbackDate.AddDays(round - 1), leg, out var inferred);
                    accumulator.Add(
                        reverse ? secondTeam : firstTeam,
                        reverse ? firstTeam : secondTeam,
                        reverse ? secondScore : firstScore,
                        reverse ? firstScore : secondScore,
                        date,
                        "Final phase",
                        roundName,
                        $"bracket-r{round}-p{oddSlot}-l{leg}",
                        GameFormat.Bracket,
                        inferred);
                }
            }
        }
    }

    private static void ParseTwoLegTemplates(
        string wikitext,
        int startYear,
        int endYear,
        DateTime fallbackDate,
        GameAccumulator accumulator)
    {
        var matches = Regex.Matches(
            wikitext,
            @"\{\{TwoLegResult(?<body>.*?)\}\}",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        var ordinal = 0;
        foreach (Match match in matches)
        {
            ordinal++;
            var values = SplitTopLevel(match.Groups["body"].Value.TrimStart('|'), "|");
            if (values.Count < 7)
            {
                continue;
            }

            var firstTeam = ParseTeam(values[0]);
            var secondTeam = ParseTeam(values[3]);
            if (firstTeam is null || secondTeam is null ||
                !TryParseScorePair(values[5], out var firstLeg) ||
                !TryParseScorePair(values[6], out var secondLeg))
            {
                continue;
            }

            var context = FindContext(wikitext, match.Index, startYear, endYear, fallbackDate);
            var firstDate = DateForLeg(context.Dates, context.FallbackDate, 1, out var firstInferred);
            var secondDate = DateForLeg(context.Dates, context.FallbackDate.AddDays(1), 2, out var secondInferred);
            accumulator.Add(
                firstTeam,
                secondTeam,
                firstLeg.Home,
                firstLeg.Away,
                firstDate,
                context.Phase,
                context.Round,
                $"twoleg-{ordinal}-1",
                GameFormat.TwoLegTemplate,
                firstInferred);
            accumulator.Add(
                secondTeam,
                firstTeam,
                secondLeg.Away,
                secondLeg.Home,
                secondDate,
                context.Phase,
                context.Round,
                $"twoleg-{ordinal}-2",
                GameFormat.TwoLegTemplate,
                secondInferred);
        }
    }

    private static void ParseWikiTables(
        string wikitext,
        int startYear,
        int endYear,
        DateTime fallbackDate,
        GameAccumulator accumulator)
    {
        var tableOrdinal = 0;
        foreach (Match tableMatch in Regex.Matches(wikitext, @"(?ms)^\{\|(?<body>.*?)^\|\}"))
        {
            tableOrdinal++;
            var tableText = tableMatch.Value;
            var rows = ParseTableRows(tableMatch.Groups["body"].Value);
            var context = FindContext(wikitext, tableMatch.Index, startYear, endYear, fallbackDate);
            if (Regex.IsMatch(tableText, "Risultati\\s+girone", RegexOptions.IgnoreCase))
            {
                ParseRoundRobinTable(rows, tableOrdinal, context, accumulator);
                continue;
            }

            var rowOrdinal = 0;
            foreach (var cells in rows)
            {
                rowOrdinal++;
                if (cells.Count < 2 || !TrySplitMatchup(cells[0], out var firstRaw, out var secondRaw))
                {
                    continue;
                }

                var firstTeam = ParseTeam(firstRaw);
                var secondTeam = ParseTeam(secondRaw);
                if (firstTeam is null || secondTeam is null)
                {
                    continue;
                }

                var parsedScores = cells.Skip(1)
                    .Select(cell => TryParseScorePair(cell, out var score) ? score : ((short Home, short Away)?)null)
                    .Where(score => score.HasValue)
                    .Select(score => score!.Value)
                    .Take(3)
                    .ToList();
                for (var index = 0; index < parsedScores.Count; index++)
                {
                    var reverse = index == 1;
                    var date = DateForLeg(
                        context.Dates,
                        context.FallbackDate.AddDays(index),
                        index + 1,
                        out var inferred);
                    var score = parsedScores[index];
                    accumulator.Add(
                        reverse ? secondTeam : firstTeam,
                        reverse ? firstTeam : secondTeam,
                        reverse ? score.Away : score.Home,
                        reverse ? score.Home : score.Away,
                        date,
                        context.Phase,
                        context.Round,
                        $"table-{tableOrdinal}-row-{rowOrdinal}-leg-{index + 1}",
                        GameFormat.Table,
                        inferred);
                }
            }
        }
    }

    private static void ParseRoundRobinTable(
        IReadOnlyList<IReadOnlyList<string>> rows,
        int tableOrdinal,
        PageContext context,
        GameAccumulator accumulator)
    {
        var teamRows = rows
            .Select((cells, index) => new { Cells = cells, Index = index, Team = cells.Count > 0 ? ParseTeam(cells[0]) : null })
            .Where(row => row.Team is not null && row.Cells.Skip(1).Any(cell => TryParseScorePair(cell, out _)))
            .ToList();
        var teamCount = teamRows.Count;
        if (teamCount < 2)
        {
            return;
        }

        for (var rowIndex = 0; rowIndex < teamRows.Count; rowIndex++)
        {
            var row = teamRows[rowIndex];
            for (var columnIndex = 0; columnIndex < teamCount; columnIndex++)
            {
                var cellIndex = columnIndex + 1;
                if (cellIndex >= row.Cells.Count ||
                    !TryParseScorePair(row.Cells[cellIndex], out var score) ||
                    rowIndex == columnIndex)
                {
                    continue;
                }

                var away = teamRows[columnIndex].Team!;
                var date = context.Dates.Count > 0
                    ? context.Dates[(rowIndex * teamCount + columnIndex) % context.Dates.Count]
                    : context.FallbackDate.AddDays((rowIndex * teamCount + columnIndex) % Math.Max(teamCount - 1, 1));
                accumulator.Add(
                    row.Team!,
                    away,
                    score.Home,
                    score.Away,
                    date,
                    context.Phase,
                    context.Round,
                    $"matrix-{tableOrdinal}-r{rowIndex}-c{columnIndex}",
                    GameFormat.Table,
                    context.Dates.Count == 0);
            }
        }
    }

    private static void ParseBulletGames(
        string wikitext,
        int startYear,
        int endYear,
        DateTime fallbackDate,
        GameAccumulator accumulator)
    {
        var lines = wikitext.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var phase = "Final phase";
        var round = "Published results";
        IReadOnlyList<DateTime> dates = [];
        var stageGameOrdinal = 0;

        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var line = lines[lineIndex].Trim();
            var heading = Regex.Match(line, @"^(?<marks>={2,4})\s*(?<text>.*?)\s*\k<marks>$");
            if (heading.Success)
            {
                var value = CleanWikiText(heading.Groups["text"].Value);
                if (heading.Groups["marks"].Value.Length == 2)
                {
                    phase = value;
                    round = value;
                }
                else
                {
                    round = value;
                }

                stageGameOrdinal = 0;
                continue;
            }

            if (!line.StartsWith('*'))
            {
                var lineDates = ExtractDates(line, startYear, endYear);
                if (lineDates.Count > 0)
                {
                    dates = lineDates;
                    stageGameOrdinal = 0;
                }
                continue;
            }

            var scoreMatches = Regex.Matches(line, @"(?<!\d)(?<home>\d{1,3})\s*[-–—]\s*(?<away>\d{1,3})(?!\d)");
            if (scoreMatches.Count == 0)
            {
                continue;
            }

            var matchupText = line[1..scoreMatches[0].Index];
            if (!TrySplitMatchup(matchupText, out var firstRaw, out var secondRaw))
            {
                continue;
            }

            var firstTeam = ParseTeam(firstRaw);
            var secondTeam = ParseTeam(secondRaw);
            if (firstTeam is null || secondTeam is null)
            {
                continue;
            }

            for (var scoreIndex = 0; scoreIndex < scoreMatches.Count; scoreIndex++)
            {
                var scoreMatch = scoreMatches[scoreIndex];
                var firstScore = short.Parse(scoreMatch.Groups["home"].Value, CultureInfo.InvariantCulture);
                var secondScore = short.Parse(scoreMatch.Groups["away"].Value, CultureInfo.InvariantCulture);
                var reverse = scoreIndex == 1;
                var date = dates.Count > scoreIndex
                    ? dates[scoreIndex]
                    : dates.Count > 0
                        ? dates[^1].AddDays(scoreIndex)
                        : fallbackDate.AddDays(stageGameOrdinal);
                accumulator.Add(
                    reverse ? secondTeam : firstTeam,
                    reverse ? firstTeam : secondTeam,
                    reverse ? secondScore : firstScore,
                    reverse ? firstScore : secondScore,
                    date,
                    phase,
                    round,
                    $"bullet-line-{lineIndex + 1}-leg-{scoreIndex + 1}",
                    GameFormat.Bullet,
                    dates.Count <= scoreIndex);
            }

            stageGameOrdinal++;
        }
    }

    private static IReadOnlyList<IReadOnlyList<string>> ParseTableRows(string body)
    {
        var result = new List<IReadOnlyList<string>>();
        foreach (var row in Regex.Split(body, @"(?m)^\|-.*$").Skip(1))
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
                foreach (var value in SplitTopLevel(line[1..], delimiter))
                {
                    cells.Add(StripCellAttributes(value));
                }
            }

            if (cells.Count > 0)
            {
                result.Add(cells);
            }
        }

        return result;
    }

    private static string StripCellAttributes(string value)
    {
        value = value.Trim();
        var separator = FindTopLevelDelimiter(value, "|");
        if (separator >= 0)
        {
            var prefix = value[..separator];
            if (prefix.Contains('=') || prefix.Contains("align", StringComparison.OrdinalIgnoreCase) ||
                prefix.Contains("style", StringComparison.OrdinalIgnoreCase) ||
                prefix.Contains("span", StringComparison.OrdinalIgnoreCase))
            {
                return value[(separator + 1)..].Trim();
            }
        }

        return value;
    }

    private static PageContext FindContext(
        string wikitext,
        int index,
        int startYear,
        int endYear,
        DateTime fallbackDate)
    {
        var prefix = wikitext[..Math.Min(index, wikitext.Length)];
        var headings = Regex.Matches(prefix, @"(?m)^(?<marks>={2,4})\s*(?<text>.*?)\s*\k<marks>$")
            .Cast<Match>()
            .ToList();
        var phaseHeading = headings.LastOrDefault(match => match.Groups["marks"].Value.Length == 2);
        var roundHeading = headings.LastOrDefault(match => match.Groups["marks"].Value.Length >= 3) ?? phaseHeading;
        var phase = phaseHeading is null ? "Final phase" : CleanWikiText(phaseHeading.Groups["text"].Value);
        var round = roundHeading is null ? "Published results" : CleanWikiText(roundHeading.Groups["text"].Value);
        var contextStart = roundHeading?.Index ?? phaseHeading?.Index ?? Math.Max(0, index - 600);
        var contextText = wikitext[contextStart..Math.Min(index, wikitext.Length)];
        var dates = ExtractDates(contextText, startYear, endYear);
        if (dates.Count == 0)
        {
            var datedLines = Regex.Matches(
                prefix,
                $@"(?im)^.*(?:{MonthPattern}).*$",
                RegexOptions.IgnoreCase);
            if (datedLines.Count > 0)
            {
                dates = ExtractDates(datedLines[^1].Value, startYear, endYear);
            }
        }
        return new PageContext(phase, round, dates, dates.FirstOrDefault(fallbackDate));
    }

    private static DateTime DateForLeg(
        IReadOnlyList<DateTime> dates,
        DateTime fallback,
        int leg,
        out bool inferred)
    {
        if (dates.Count == 0)
        {
            inferred = true;
            return DateTime.SpecifyKind(fallback.Date, DateTimeKind.Utc);
        }

        inferred = leg > dates.Count;
        return dates[Math.Min(Math.Max(leg - 1, 0), dates.Count - 1)];
    }

    private static DateTime? ExtractInfoboxDate(string wikitext, string field, int startYear, int endYear)
    {
        var match = Regex.Match(wikitext, $@"(?im)^\|\s*{Regex.Escape(field)}\s*=\s*(?<value>.+)$");
        return match.Success ? ExtractDates(match.Groups["value"].Value, startYear, endYear).FirstOrDefault() : null;
    }

    private static IReadOnlyList<DateTime> ExtractDates(string text, int startYear, int endYear)
    {
        var cleaned = CleanWikiText(text).Replace("º", string.Empty, StringComparison.Ordinal)
            .Replace("°", string.Empty, StringComparison.Ordinal);
        var values = new List<(int Position, DateTime Date)>();
        foreach (Match match in Regex.Matches(
                     cleaned,
                     $@"(?<!\d)(?<day>\d{{1,2}})\s+(?<month>{MonthPattern})(?:\s+(?<year>(?:19|20)\d{{2}}))?",
                     RegexOptions.IgnoreCase))
        {
            AddDate(match.Index, match.Groups["day"].Value, match.Groups["month"].Value,
                match.Groups["year"].Value, startYear, endYear, values);
        }

        foreach (Match match in Regex.Matches(
                     cleaned,
                     $@"(?<!\d)(?<day>\d{{1,2}})\s*(?:,|e|al|-)\s*(?<day2>\d{{1,2}})\s+(?<month>{MonthPattern})(?:\s+(?<year>(?:19|20)\d{{2}}))?",
                     RegexOptions.IgnoreCase))
        {
            AddDate(match.Index, match.Groups["day"].Value, match.Groups["month"].Value,
                match.Groups["year"].Value, startYear, endYear, values);
        }

        return values
            .OrderBy(value => value.Date)
            .ThenBy(value => value.Position)
            .Select(value => DateTime.SpecifyKind(value.Date.Date, DateTimeKind.Utc))
            .Distinct()
            .ToArray();
    }

    private static void AddDate(
        int position,
        string dayValue,
        string monthValue,
        string yearValue,
        int startYear,
        int endYear,
        ICollection<(int Position, DateTime Date)> values)
    {
        if (!int.TryParse(dayValue, NumberStyles.None, CultureInfo.InvariantCulture, out var day) ||
            !ItalianMonths.TryGetValue(monthValue, out var month))
        {
            return;
        }

        var year = int.TryParse(yearValue, NumberStyles.None, CultureInfo.InvariantCulture, out var explicitYear)
            ? explicitYear
            : startYear == 1967 && month >= 6
                ? endYear
                : month >= 7 ? startYear : endYear;
        if (day <= DateTime.DaysInMonth(year, month))
        {
            values.Add((position, new DateTime(year, month, day)));
        }
    }

    private static string CleanRoundLabel(string value)
    {
        var firstLine = Regex.Split(value, @"<br\s*/?>", RegexOptions.IgnoreCase)[0];
        return CleanWikiText(firstLine);
    }

    private static string InferBracketRoundName(int round, int maximumRound)
    {
        var names = maximumRound switch
        {
            5 => new[] { "Sedicesimi di finale", "Ottavi di finale", "Quarti di finale", "Semifinali", "Finale" },
            4 => new[] { "Ottavi di finale", "Quarti di finale", "Semifinali", "Finale" },
            3 => new[] { "Quarti di finale", "Semifinali", "Finale" },
            2 => new[] { "Semifinali", "Finale" },
            _ => []
        };
        return round >= 1 && round <= names.Length ? names[round - 1] : $"Round {round}";
    }

    private static TeamRef? ParseTeam(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var link = Regex.Match(raw, @"\[\[(?<target>[^|\]]+)(?:\|(?<display>[^\]]+))?\]\]");
        var basketTemplate = Regex.Match(raw, @"\{\{\s*Basket\s+(?<name>[^|}]+)(?:\|[^}]*)?\}\}", RegexOptions.IgnoreCase);
        var canonical = link.Success
            ? link.Groups["target"].Value
            : basketTemplate.Success
                ? basketTemplate.Groups["name"].Value
                : CleanWikiText(raw);
        var name = CleanWikiText(raw).Trim(' ', '*', '\'', '.');
        if (string.IsNullOrWhiteSpace(name) ||
            name.Equals("bye", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("-", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var id = Slugify(canonical);
        return string.IsNullOrWhiteSpace(id) ? null : new TeamRef($"wiki-team:{id}", name);
    }

    private static string CleanWikiText(string value)
    {
        var cleaned = Regex.Replace(value, @"<ref\b[^>]*>.*?</ref>|<ref\b[^>]*/>", string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        cleaned = Regex.Replace(cleaned, @"\{\{\s*Basket\s+(?<name>[^|}]+)(?:\|[^}]*)?\}\}",
            match => match.Groups["name"].Value, RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\{\{[^{}]*\}\}", string.Empty);
        cleaned = Regex.Replace(cleaned, @"\[\[(?<target>[^|\]]+)(?:\|(?<display>[^\]]+))?\]\]",
            match => match.Groups["display"].Success
                ? match.Groups["display"].Value
                : match.Groups["target"].Value);
        cleaned = Regex.Replace(cleaned, "<[^>]+>", " ");
        cleaned = cleaned.Replace("'''", string.Empty, StringComparison.Ordinal)
            .Replace("''", string.Empty, StringComparison.Ordinal)
            .Replace("&nbsp;", " ", StringComparison.OrdinalIgnoreCase);
        return Regex.Replace(HtmlEntity.DeEntitize(cleaned), @"\s+", " ").Trim();
    }

    private static string Slugify(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        var previousDash = false;
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                previousDash = false;
            }
            else if (!previousDash && builder.Length > 0)
            {
                builder.Append('-');
                previousDash = true;
            }
        }

        return builder.ToString().Trim('-');
    }

    private static bool TrySplitMatchup(string value, out string first, out string second)
    {
        first = string.Empty;
        second = string.Empty;
        var braceDepth = 0;
        var linkDepth = 0;
        for (var index = 0; index < value.Length; index++)
        {
            if (index + 1 < value.Length && value.AsSpan(index, 2).SequenceEqual("{{"))
            {
                braceDepth++;
                index++;
                continue;
            }
            if (index + 1 < value.Length && value.AsSpan(index, 2).SequenceEqual("}}"))
            {
                braceDepth = Math.Max(0, braceDepth - 1);
                index++;
                continue;
            }
            if (index + 1 < value.Length && value.AsSpan(index, 2).SequenceEqual("[["))
            {
                linkDepth++;
                index++;
                continue;
            }
            if (index + 1 < value.Length && value.AsSpan(index, 2).SequenceEqual("]]"))
            {
                linkDepth = Math.Max(0, linkDepth - 1);
                index++;
                continue;
            }

            if (braceDepth == 0 && linkDepth == 0 && value[index] is '-' or '–' or '—')
            {
                first = value[..index].Trim();
                second = value[(index + 1)..].Trim();
                return first.Length > 0 && second.Length > 0;
            }
        }

        return false;
    }

    private static bool TryParseSingleScore(string value, out short score)
    {
        score = default;
        var cleaned = CleanWikiText(value);
        var match = Regex.Match(cleaned, @"(?<!\d)\d{1,3}(?!\d)");
        return match.Success && short.TryParse(match.Value, NumberStyles.None, CultureInfo.InvariantCulture, out score);
    }

    private static bool TryParseScorePair(string value, out (short Home, short Away) score)
    {
        score = default;
        var match = Regex.Match(CleanWikiText(value), @"(?<!\d)(?<home>\d{1,3})\s*[-–—]\s*(?<away>\d{1,3})(?!\d)");
        if (!match.Success ||
            !short.TryParse(match.Groups["home"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var home) ||
            !short.TryParse(match.Groups["away"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var away))
        {
            return false;
        }

        score = (home, away);
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
        var braceDepth = 0;
        var linkDepth = 0;
        for (var index = 0; index <= value.Length - delimiter.Length; index++)
        {
            if (index + 1 < value.Length && value.AsSpan(index, 2).SequenceEqual("{{"))
            {
                braceDepth++;
                index++;
                continue;
            }
            if (index + 1 < value.Length && value.AsSpan(index, 2).SequenceEqual("}}"))
            {
                braceDepth = Math.Max(0, braceDepth - 1);
                index++;
                continue;
            }
            if (index + 1 < value.Length && value.AsSpan(index, 2).SequenceEqual("[["))
            {
                linkDepth++;
                index++;
                continue;
            }
            if (index + 1 < value.Length && value.AsSpan(index, 2).SequenceEqual("]]"))
            {
                linkDepth = Math.Max(0, linkDepth - 1);
                index++;
                continue;
            }

            if (braceDepth == 0 && linkDepth == 0 && value.AsSpan(index, delimiter.Length).SequenceEqual(delimiter))
            {
                return index;
            }
        }

        return -1;
    }

    private static (int StartYear, int EndYear) ParseSeason(string season)
    {
        var canonical = SeasonLabelNormalizer.ToFullSeasonLabel(season);
        var pieces = canonical.Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (pieces.Length != 2 ||
            !int.TryParse(pieces[0], NumberStyles.None, CultureInfo.InvariantCulture, out var startYear) ||
            !int.TryParse(pieces[1], NumberStyles.None, CultureInfo.InvariantCulture, out var endYear) ||
            endYear != startYear + 1 || startYear is < 1967 or > 2007)
        {
            throw new ArgumentException(
                "Historical Italian Cup coverage supports played editions from 1967-1968 through 2007-2008.",
                nameof(season));
        }

        return (startYear, endYear);
    }

    private sealed class GameAccumulator(
        string season,
        int editionYear,
        string pageUrl,
        DateTime fetchedAtUtc,
        string revision,
        ICollection<string> warnings)
    {
        private readonly HashSet<string> semanticKeys = new(StringComparer.Ordinal);

        public List<BasketballProviderGame> Games { get; } = [];
        public int BracketCount { get; private set; }
        public int TwoLegTemplateCount { get; private set; }
        public int TableCount { get; private set; }
        public int BulletCount { get; private set; }
        public int InferredDateCount { get; private set; }

        public void Add(
            TeamRef home,
            TeamRef away,
            short homeScore,
            short awayScore,
            DateTime date,
            string phase,
            string round,
            string coordinate,
            GameFormat format,
            bool inferredDate)
        {
            if (home.Id == away.Id)
            {
                warnings.Add($"Skipped {coordinate}: both sides resolved to {home.Name}.");
                return;
            }

            var semanticKey = $"{date:yyyy-MM-dd}|{home.Id}|{away.Id}|{homeScore}|{awayScore}|{phase}|{round}";
            if (!semanticKeys.Add(semanticKey))
            {
                return;
            }

            var idInput = $"{season}|{coordinate}|{semanticKey}";
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(idInput)))[..20].ToLowerInvariant();
            var exclusionReason = homeScore + awayScore == 2 && (homeScore == 0 || awayScore == 0)
                ? "Source reports an administrative 2-0 result; excluded from ELO."
                : null;
            Games.Add(new BasketballProviderGame(
                Source,
                $"wiki-it-cup-{editionYear}-{hash}",
                DateTime.SpecifyKind(date.Date, DateTimeKind.Utc),
                "finished",
                home.Id,
                home.Name,
                away.Id,
                away.Name,
                homeScore,
                awayScore,
                new BasketballProviderGameProvenance(
                    pageUrl,
                    season,
                    fetchedAtUtc,
                    ParserVersion,
                    revision),
                exclusionReason,
                phase,
                round,
                "IT",
                "IT"));

            if (inferredDate)
            {
                InferredDateCount++;
            }
            switch (format)
            {
                case GameFormat.Bracket: BracketCount++; break;
                case GameFormat.TwoLegTemplate: TwoLegTemplateCount++; break;
                case GameFormat.Table: TableCount++; break;
                case GameFormat.Bullet: BulletCount++; break;
            }
        }
    }

    private sealed record TeamRef(string Id, string Name);
    private sealed record PageContext(
        string Phase,
        string Round,
        IReadOnlyList<DateTime> Dates,
        DateTime FallbackDate);
    private enum GameFormat { Bracket, TwoLegTemplate, Table, Bullet }
}
