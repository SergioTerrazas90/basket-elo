using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BasketElo.Domain.CurrentResults;
using HtmlAgilityPack;
using Microsoft.Extensions.Options;

namespace BasketElo.Infrastructure.CurrentResults;

public sealed class LiveScoreDailyResultsProvider(
    HttpClient httpClient,
    IOptions<LiveScoreOptions> options) : ICurrentResultsProvider
{
    private const string ParserVersion = "livescore-daily-html-v2";
    private readonly LiveScoreOptions options = options.Value;

    public string Source => "livescore";

    public async Task<CurrentResultFetchResult> FetchAsync(DateOnly date, CancellationToken cancellationToken)
    {
        if (!options.Enabled)
        {
            throw new InvalidOperationException("The Livescore provider is disabled. Enable it only after confirming permitted use of the source data.");
        }

        var path = $"/basketball/{date:yyyy-MM-dd}/";
        using var response = await httpClient.GetAsync(path, cancellationToken);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        var sourceUrl = new Uri(httpClient.BaseAddress ?? new Uri(options.BaseUrl), path).ToString();
        var revision = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(html))).ToLowerInvariant()[..16];

        return new CurrentResultFetchResult(
            date,
            sourceUrl,
            revision,
            Parse(html, date, sourceUrl, revision, options.SourceTimeZoneId));
    }

    public static IReadOnlyCollection<CurrentResultCandidate> Parse(
        string html,
        DateOnly date,
        string sourceUrl,
        string sourceRevision,
        string sourceTimeZoneId)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        var candidates = new List<CurrentResultCandidate>();
        var headers = document.DocumentNode
            .SelectNodes("//div[contains(concat(' ', normalize-space(@class), ' '), ' Pa ')]")?
            .ToList() ?? [];

        foreach (var header in headers)
        {
            var parent = header.ParentNode;
            if (parent is null)
            {
                continue;
            }

            var country = Clean(header.SelectSingleNode(".//span[contains(concat(' ', normalize-space(@class), ' '), ' Sa ')]")?.InnerText);
            var competitionAndStage = Clean(header.SelectSingleNode(".//span[contains(concat(' ', normalize-space(@class), ' '), ' Ta ')]")?.InnerText);
            SplitCompetition(country, competitionAndStage, out var competition, out var stage);
            var headerIndex = parent.ChildNodes.ToList().IndexOf(header);
            if (headerIndex < 0)
            {
                continue;
            }

            for (var index = headerIndex + 1; index < parent.ChildNodes.Count; index++)
            {
                var node = parent.ChildNodes[index];
                if (HasClass(node, "Pa"))
                {
                    break;
                }

                if (!HasClass(node, "Xe"))
                {
                    continue;
                }

                var parsed = ParseEvent(node, date, sourceUrl, sourceRevision, country, competition, stage, sourceTimeZoneId);
                if (parsed is not null)
                {
                    candidates.Add(parsed);
                }
            }
        }

        return candidates;
    }

    private static CurrentResultCandidate? ParseEvent(
        HtmlNode eventNode,
        DateOnly date,
        string sourceUrl,
        string sourceRevision,
        string country,
        string competition,
        string? stage,
        string sourceTimeZoneId)
    {
        var teams = eventNode
            .SelectNodes(".//div[contains(concat(' ', normalize-space(@class), ' '), ' nf ')]//div[contains(concat(' ', normalize-space(@class), ' '), ' vf ')]")?
            .Select(x => Clean(x.InnerText))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList() ?? [];
        if (teams.Count < 2)
        {
            return null;
        }

        var scoreValues = eventNode
            .SelectNodes(".//div[contains(concat(' ', normalize-space(@class), ' '), ' rf ')]//span[contains(concat(' ', normalize-space(@class), ' '), ' hf ')]")?
            .Select(x => TryParseScore(Clean(x.InnerText)))
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .ToList() ?? [];
        var statusText = Clean(eventNode.SelectSingleNode(".//span[contains(concat(' ', normalize-space(@class), ' '), ' Ih ')]")?.InnerText);
        var status = ToStatus(statusText, scoreValues.Count >= 2);
        var gameDateTimeUtc = ExtractEventDateTimeUtc(eventNode)
            ?? ParseDateTimeUtc(date, statusText, sourceTimeZoneId);
        var sourceGameId = ExtractSourceGameId(eventNode) ?? SyntheticSourceGameId(date, country, competition, teams[0], teams[1]);
        var sourceEventUrl = ExtractSourceUrl(eventNode, sourceUrl);

        return new CurrentResultCandidate(
            sourceGameId,
            sourceEventUrl,
            date,
            gameDateTimeUtc,
            country,
            competition,
            stage,
            teams[0],
            teams[1],
            SourceTeamId(country, teams[0]),
            SourceTeamId(country, teams[1]),
            scoreValues.Count >= 2 ? scoreValues[0] : null,
            scoreValues.Count >= 2 ? scoreValues[1] : null,
            status,
            statusText,
            sourceRevision,
            ParserVersion);
    }

    private static string? ExtractSourceGameId(HtmlNode eventNode)
    {
        var button = eventNode.SelectSingleNode(".//button[@data-eventId or @data-eventid]");
        var buttonId = button?.GetAttributeValue("data-eventId", string.Empty);
        if (string.IsNullOrWhiteSpace(buttonId))
        {
            buttonId = button?.GetAttributeValue("data-eventid", string.Empty);
        }
        if (!string.IsNullOrWhiteSpace(buttonId))
        {
            return buttonId.Trim();
        }

        var href = eventNode.SelectSingleNode(".//a[@href]")?.GetAttributeValue("href", string.Empty);
        var match = Regex.Match(href ?? string.Empty, @"/(?<id>\d+)/?(?:\?.*)?$", RegexOptions.CultureInvariant);
        return match.Success ? match.Groups["id"].Value : null;
    }

    private static string ExtractSourceUrl(HtmlNode eventNode, string pageUrl)
    {
        var href = eventNode.SelectSingleNode(".//a[@href]")?.GetAttributeValue("href", string.Empty);
        if (string.IsNullOrWhiteSpace(href))
        {
            return pageUrl;
        }

        return Uri.TryCreate(new Uri(pageUrl), href, out var absolute)
            ? absolute.ToString()
            : pageUrl;
    }

    private static DateTime? ExtractEventDateTimeUtc(HtmlNode eventNode)
    {
        var button = eventNode.SelectSingleNode(".//button[@data-favouritesDetails or @data-favouritesdetails]");
        var favouritesDetails = button?.GetAttributeValue("data-favouritesDetails", string.Empty);
        if (string.IsNullOrWhiteSpace(favouritesDetails))
        {
            favouritesDetails = button?.GetAttributeValue("data-favouritesdetails", string.Empty);
        }

        var match = Regex.Match(
            favouritesDetails ?? string.Empty,
            @"(?:^|-)(?<epoch>\d{10,13})$",
            RegexOptions.CultureInvariant);
        if (!match.Success ||
            !long.TryParse(match.Groups["epoch"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var epoch))
        {
            return null;
        }

        try
        {
            if (match.Groups["epoch"].Value.Length <= 10)
            {
                epoch = checked(epoch * 1000);
            }

            return DateTimeOffset.FromUnixTimeMilliseconds(epoch).UtcDateTime;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    private static DateTime ParseDateTimeUtc(DateOnly date, string statusText, string sourceTimeZoneId)
    {
        var local = date.ToDateTime(TimeOnly.MinValue);
        if (TimeSpan.TryParseExact(statusText, ["hh\\:mm", "h\\:mm"], CultureInfo.InvariantCulture, out var time))
        {
            local = date.ToDateTime(TimeOnly.FromTimeSpan(time));
        }

        var zone = FindTimeZone(sourceTimeZoneId);
        return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), zone);
    }

    private static TimeZoneInfo FindTimeZone(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException)
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id == "Europe/Madrid" ? "Romance Standard Time" : "Europe/Madrid");
            }
            catch (TimeZoneNotFoundException)
            {
                return TimeZoneInfo.Utc;
            }
        }
    }

    private static string ToStatus(string rawStatus, bool hasScore)
    {
        var status = rawStatus.ToLowerInvariant();
        if (status.Contains("postponed") || status is "ppd" or "postp.") return CurrentResultStatuses.Postponed;
        if (status.Contains("cancelled") || status.Contains("canceled")) return CurrentResultStatuses.Cancelled;
        if (status is "ft" or "ot" or "aot" || status.Contains("final")) return CurrentResultStatuses.Finished;
        if (Regex.IsMatch(rawStatus, @"^\d{1,2}:\d{2}$", RegexOptions.CultureInvariant)) return CurrentResultStatuses.Scheduled;
        return hasScore ? CurrentResultStatuses.Live : CurrentResultStatuses.Scheduled;
    }

    private static void SplitCompetition(string parentName, string value, out string competition, out string? stage)
    {
        if (!string.IsNullOrWhiteSpace(parentName) &&
            Regex.IsMatch(value, @"^Group\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            competition = parentName;
            stage = Clean(value);
            return;
        }

        var separator = value.IndexOf(':');
        if (separator > 0)
        {
            competition = Clean(value[..separator]);
            stage = Clean(value[(separator + 1)..]);
            return;
        }

        competition = value;
        stage = null;
    }

    private static short? TryParseScore(string value) => short.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var score) ? score : null;

    private static string SourceTeamId(string country, string team) => $"team:{Normalize(country)}:{Normalize(team)}";

    private static string SyntheticSourceGameId(DateOnly date, string country, string competition, string home, string away) =>
        $"synthetic:{date:yyyy-MM-dd}:{Normalize(country)}:{Normalize(competition)}:{Normalize(home)}:{Normalize(away)}";

    private static string Normalize(string value) =>
        string.Join('-', Clean(value).ToLowerInvariant().Split([' ', '/', '\\', ':'], StringSplitOptions.RemoveEmptyEntries));

    private static string Clean(string? value) => HtmlEntity.DeEntitize(value ?? string.Empty).Trim();

    private static bool HasClass(HtmlNode node, string className) =>
        node.GetAttributeValue("class", string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(className, StringComparer.Ordinal);
}
