using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using BasketElo.Domain.Backfill;
using HtmlAgilityPack;
using Microsoft.Extensions.Options;

namespace BasketElo.Infrastructure.Backfill;

/// <summary>
/// Reads the official ABA League calendar archive for the inaugural seasons
/// 2001-2002 through 2007-2008. The archive exposes regular-season and playoff
/// panels in one season calendar, with stable match IDs in each match URL.
/// </summary>
public sealed class AbaLeagueOfficialBasketballDataProvider(
    HttpClient httpClient,
    IOptions<AbaLeagueOfficialOptions> options) : IBasketballDataProvider
{
    public const string Source = "aba-official";
    public const string ParserVersion = "aba-official-calendar-v1";

    private static readonly IReadOnlyDictionary<int, int> SeasonIds = new Dictionary<int, int>
    {
        [2001] = 1,
        [2002] = 2,
        [2003] = 3,
        [2004] = 4,
        [2005] = 5,
        [2006] = 6,
        [2007] = 7
    };

    private static readonly IReadOnlyDictionary<int, int> ExpectedGameCounts = new Dictionary<int, int>
    {
        [2001] = 135,
        [2002] = 135,
        [2003] = 185,
        [2004] = 247,
        [2005] = 189,
        [2006] = 189,
        [2007] = 196
    };

    private static readonly Regex SeasonPattern = new(
        "^(?<start>20\\d{2})[-/](?<end>20\\d{2})$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ScorePattern = new(
        "(?<home>\\d{1,3})\\s*:\\s*(?<away>\\d{1,3})",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex DatePattern = new(
        "(?<day>\\d{2})\\.(?<month>\\d{2})\\.(?<year>\\d{4})\\s+(?<hour>\\d{2}):(?<minute>\\d{2})\\s+(?<zone>CET|CEST)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public string SourceKey => Source;

    public Task<BasketballProviderLeague?> ResolveLeagueAsync(
        string country,
        string leagueName,
        BackfillExecutionContext context,
        CancellationToken cancellationToken)
    {
        var league = string.Equals(country, "Europe", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(leagueName, "ABA League", StringComparison.OrdinalIgnoreCase)
                ? new BasketballProviderLeague(Source, "ABA_LEAGUE", "ABA League", null)
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
            !string.Equals(league.SourceLeagueId, "ABA_LEAGUE", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The official ABA provider only supports Europe: ABA League.");
        }

        var (startYear, endYear) = ParseSeason(season);
        if (!SeasonIds.TryGetValue(startYear, out var seasonId))
        {
            return ([], false, [$"Official ABA historical coverage supports 2001-2002 through 2007-2008; {season} is outside that range."]);
        }

        if (!context.CanUseRequest())
        {
            return ([], false, [$"Request budget reached before the official ABA calendar for {season} could be fetched."]);
        }

        context.ConsumeRequest();
        if (options.Value.MinRequestIntervalMilliseconds > 0)
        {
            await Task.Delay(options.Value.MinRequestIntervalMilliseconds, cancellationToken);
        }

        var path = $"/calendar/{seasonId}/1/";
        using var response = await httpClient.GetAsync(path, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return ([], false, [$"The official ABA calendar returned HTTP {(int)response.StatusCode} for {season}."]);
        }

        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        var fetchedAtUtc = DateTime.UtcNow;
        var document = new HtmlDocument();
        document.LoadHtml(html);

        var warnings = new List<string>();
        var games = new Dictionary<string, BasketballProviderGame>(StringComparer.OrdinalIgnoreCase);
        var tables = document.DocumentNode.SelectNodes(
            "//table[contains(concat(' ', normalize-space(@class), ' '), ' league_calendar_table ')]");

        if (tables is null || tables.Count == 0)
        {
            return ([], false, [$"The official ABA calendar exposed no game tables for {season}."]);
        }

        foreach (var table in tables)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var panel = table.Ancestors("div")
                .FirstOrDefault(node => HasClass(node, "panel"));
            var headingNode = panel?.SelectSingleNode(
                ".//div[contains(concat(' ', normalize-space(@class), ' '), ' panel-heading ')]//h4");
            var heading = NormalizeText(headingNode?.InnerText) ?? "Unknown round";
            var (phase, round) = ParsePhaseAndRound(heading);
            var rows = table.SelectNodes(".//tbody/tr");
            if (rows is null)
            {
                continue;
            }

            foreach (var row in rows)
            {
                if (!TryParseGame(row, startYear, endYear, seasonId, path, fetchedAtUtc, phase, round, out var game, out var warning))
                {
                    warnings.Add($"{season}: {heading}: {warning}");
                    continue;
                }

                games[game!.SourceGameId] = game;
            }
        }

        if (ExpectedGameCounts.TryGetValue(startYear, out var expected) && games.Count != expected)
        {
            warnings.Add($"{season}: official ABA archive returned {games.Count} games; expected {expected} from the reviewed season calendar.");
        }

        return (
            games.Values
                .OrderBy(game => game.GameDateTimeUtc)
                .ThenBy(game => game.SourceGameId)
                .ToArray(),
            false,
            warnings);
    }

    private static bool TryParseGame(
        HtmlNode row,
        int startYear,
        int endYear,
        int seasonId,
        string seasonPath,
        DateTime fetchedAtUtc,
        string phase,
        string round,
        out BasketballProviderGame? game,
        out string warning)
    {
        game = null;
        warning = string.Empty;
        var cells = row.SelectNodes("./td");
        if (cells is null || cells.Count < 3)
        {
            warning = "calendar row did not contain game, score, and date cells";
            return false;
        }

        var teamCell = cells[0];
        var teamText = NormalizeText(
            teamCell.SelectSingleNode(".//p[contains(concat(' ', normalize-space(@class), ' '), ' hidden-xs ')]")?.InnerText ??
            teamCell.InnerText);
        var teams = SplitTeams(teamText);
        if (teams is null)
        {
            warning = "could not split the home and away team names";
            return false;
        }

        var mobileTeamText = NormalizeText(
            teamCell.SelectSingleNode(".//p[contains(concat(' ', normalize-space(@class), ' '), ' visible-xs ')]")?.InnerText);
        var shortCodes = SplitTeams(mobileTeamText);
        var homeTeamId = NormalizeTeamId(shortCodes?.Home ?? teams.Value.Home);
        var awayTeamId = NormalizeTeamId(shortCodes?.Away ?? teams.Value.Away);

        var scoreMatch = ScorePattern.Match(NormalizeText(cells[1].InnerText) ?? string.Empty);
        if (!scoreMatch.Success ||
            !short.TryParse(scoreMatch.Groups["home"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var homeScore) ||
            !short.TryParse(scoreMatch.Groups["away"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var awayScore))
        {
            warning = "could not parse the final score";
            return false;
        }

        var dateText = NormalizeText(cells[2].InnerText) ?? string.Empty;
        var dateMatch = DatePattern.Match(dateText);
        if (!dateMatch.Success || !TryParseUtc(dateMatch, out var gameDateTimeUtc))
        {
            warning = "could not parse the local game date and time";
            return false;
        }

        var matchLink = teamCell.SelectSingleNode(".//a[@href[contains(., '/match/')]]") ??
            row.SelectSingleNode(".//a[@href[contains(., '/match/')]]");
        var matchHref = matchLink?.GetAttributeValue("href", string.Empty);
        var matchId = ExtractMatchId(matchHref);
        if (string.IsNullOrWhiteSpace(matchId))
        {
            warning = "could not find the official match ID";
            return false;
        }

        var sourceUrl = ToAbsoluteUrl(matchHref);
        game = new BasketballProviderGame(
            Source,
            $"aba-{seasonId}-{matchId}",
            gameDateTimeUtc,
            "finished",
            homeTeamId,
            teams.Value.Home,
            awayTeamId,
            teams.Value.Away,
            homeScore,
            awayScore,
            new BasketballProviderGameProvenance(
                sourceUrl,
                $"{startYear}-{endYear}|season-id:{seasonId}",
                fetchedAtUtc,
                ParserVersion,
                seasonPath),
            CompetitionPhase: phase,
            CompetitionRound: round);

        return true;
    }

    private static (string Phase, string Round) ParsePhaseAndRound(string heading)
    {
        var normalized = heading
            .Replace("", string.Empty, StringComparison.Ordinal)
            .Trim();
        return (normalized.StartsWith("ROUND ", StringComparison.OrdinalIgnoreCase) ? "Regular Season" : "Playoffs", normalized);
    }

    private static (string Home, string Away)? SplitTeams(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var parts = text.Split(':', 2, StringSplitOptions.TrimEntries);
        return parts.Length == 2 && parts.All(part => !string.IsNullOrWhiteSpace(part))
            ? (parts[0], parts[1])
            : null;
    }

    private static string NormalizeTeamId(string value)
    {
        var slug = value.Trim().Normalize(NormalizationForm.FormD);
        var chars = slug
            .Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            .Select(character => char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '-');
        return string.Join(string.Empty, chars)
            .Replace("--", "-", StringComparison.Ordinal)
            .Trim('-');
    }

    private static bool TryParseUtc(Match match, out DateTime utc)
    {
        var offset = match.Groups["zone"].Value.Equals("CEST", StringComparison.OrdinalIgnoreCase)
            ? TimeSpan.FromHours(2)
            : TimeSpan.FromHours(1);
        var local = new DateTime(
            int.Parse(match.Groups["year"].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups["month"].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups["day"].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups["hour"].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups["minute"].Value, CultureInfo.InvariantCulture),
            0,
            DateTimeKind.Unspecified);
        utc = new DateTimeOffset(local, offset).UtcDateTime;
        return true;
    }

    private static (int StartYear, int EndYear) ParseSeason(string season)
    {
        var match = SeasonPattern.Match(season.Trim());
        if (!match.Success)
        {
            throw new ArgumentException($"Invalid ABA season '{season}'. Expected YYYY-YYYY.", nameof(season));
        }

        return (
            int.Parse(match.Groups["start"].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups["end"].Value, CultureInfo.InvariantCulture));
    }

    private static string? NormalizeText(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : Regex.Replace(value, "\\s+", " ", RegexOptions.CultureInvariant).Trim();

    private static bool HasClass(HtmlNode node, string className)
        => node.GetAttributeValue("class", string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Contains(className, StringComparer.OrdinalIgnoreCase);

    private static string? ExtractMatchId(string? href)
    {
        if (string.IsNullOrWhiteSpace(href))
        {
            return null;
        }

        var match = Regex.Match(href, @"/match/(?<id>\d+)/", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success ? match.Groups["id"].Value : null;
    }

    private static string? ToAbsoluteUrl(string? href)
    {
        if (string.IsNullOrWhiteSpace(href))
        {
            return null;
        }

        return href.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? href
            : $"https://www.aba-liga.com{href}";
    }
}
