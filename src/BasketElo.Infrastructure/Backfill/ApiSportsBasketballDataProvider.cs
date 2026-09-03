using System.Text.Json;
using System.Text;
using System.Globalization;
using BasketElo.Domain.Backfill;
using Microsoft.Extensions.Options;

namespace BasketElo.Infrastructure.Backfill;

public class ApiSportsBasketballDataProvider(
    HttpClient httpClient,
    IOptions<ApiSportsOptions> options) : IBasketballDataProvider
{
    public const string Source = "api-sports";
    public string SourceKey => Source;

    public async Task<BasketballProviderLeague?> ResolveLeagueAsync(
        string country,
        string leagueName,
        BackfillExecutionContext context,
        CancellationToken cancellationToken)
    {
        EnsureRequestAvailable(context);

        var uri = $"/leagues?country={Uri.EscapeDataString(country)}";
        using var request = CreateRequest(uri);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(payload);
        if (!document.RootElement.TryGetProperty("response", out var responseArray) || responseArray.GetArrayLength() == 0)
        {
            return null;
        }

        var candidates = new List<BasketballProviderLeague>();
        foreach (var item in responseArray.EnumerateArray())
        {
            var leagueElement = item;
            if (item.TryGetProperty("league", out var nestedLeagueElement))
            {
                leagueElement = nestedLeagueElement;
            }

            var id = leagueElement.GetProperty("id").ToString();
            var name = leagueElement.GetProperty("name").GetString() ?? leagueName;
            string? countryCode = null;
            if (item.TryGetProperty("country", out var countryElement))
            {
                if (countryElement.ValueKind == JsonValueKind.Object)
                {
                    countryCode = countryElement.TryGetProperty("code", out var codeElement)
                        ? codeElement.GetString()
                        : null;
                }
                else if (item.TryGetProperty("country", out var flatCountryElement) && flatCountryElement.ValueKind == JsonValueKind.String)
                {
                    countryCode = flatCountryElement.GetString();
                }
            }

            var competitionType = leagueElement.TryGetProperty("type", out var typeElement)
                ? typeElement.GetString() ?? "League"
                : "League";

            candidates.Add(new BasketballProviderLeague(Source, id, name, countryCode, competitionType));
        }

        return FindBestLeagueMatch(country, leagueName, candidates);
    }

    public async Task<(IReadOnlyCollection<BasketballProviderGame> Games, bool HasMorePages)> GetGamesAsync(
        BasketballProviderLeague league,
        string season,
        BackfillExecutionContext context,
        CancellationToken cancellationToken)
    {
        EnsureRequestAvailable(context);

        var uri = $"/games?league={Uri.EscapeDataString(league.SourceLeagueId)}&season={Uri.EscapeDataString(season)}";
        using var request = CreateRequest(uri);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(payload);
        var games = new List<BasketballProviderGame>();

        var hasMorePages = false;
        if (document.RootElement.TryGetProperty("paging", out var pagingElement))
        {
            var current = pagingElement.TryGetProperty("current", out var currentElement) ? currentElement.GetInt32() : 1;
            var total = pagingElement.TryGetProperty("total", out var totalElement) ? totalElement.GetInt32() : 1;
            hasMorePages = total > current;
        }

        if (!document.RootElement.TryGetProperty("response", out var responseArray))
        {
            return (games, hasMorePages);
        }

        foreach (var item in responseArray.EnumerateArray())
        {
            var sourceGameId = item.GetProperty("id").ToString();
            var date = item.TryGetProperty("date", out var dateElement)
                ? DateTime.Parse(dateElement.GetString() ?? DateTime.UtcNow.ToString("O")).ToUniversalTime()
                : DateTime.UtcNow;

            var status = "scheduled";
            if (item.TryGetProperty("status", out var statusElement) &&
                statusElement.TryGetProperty("long", out var longStatus))
            {
                status = (longStatus.GetString() ?? "scheduled").ToLowerInvariant();
            }

            var teams = item.GetProperty("teams");
            var home = teams.GetProperty("home");
            var away = teams.GetProperty("away");

            short? homeScore = null;
            short? awayScore = null;
            if (item.TryGetProperty("scores", out var scores))
            {
                homeScore = TryGetShort(scores, "home", "total");
                awayScore = TryGetShort(scores, "away", "total");
            }

            games.Add(new BasketballProviderGame(
                Source,
                sourceGameId,
                date,
                status,
                home.GetProperty("id").ToString(),
                home.GetProperty("name").GetString() ?? "Unknown Home",
                away.GetProperty("id").ToString(),
                away.GetProperty("name").GetString() ?? "Unknown Away",
                homeScore,
                awayScore));
        }

        return (games, hasMorePages);
    }

    private HttpRequestMessage CreateRequest(string path)
    {
        var apiKey = options.Value.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("ApiSports:ApiKey is required for API-Sports backfill.");
        }

        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("x-apisports-key", apiKey);
        return request;
    }

    private static short? TryGetShort(JsonElement parent, string child1, string child2)
    {
        if (!parent.TryGetProperty(child1, out var child) || !child.TryGetProperty(child2, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt16(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String && short.TryParse(value.GetString(), out var parsed)
            ? parsed
            : null;
    }

    private static void EnsureRequestAvailable(BackfillExecutionContext context)
    {
        if (!context.CanUseRequest())
        {
            throw new InvalidOperationException("Backfill request budget reached.");
        }
    }

    private static BasketballProviderLeague? FindBestLeagueMatch(
        string country,
        string leagueName,
        IReadOnlyCollection<BasketballProviderLeague> candidates)
    {
        if (candidates.Count == 0)
        {
            return null;
        }

        var desiredNames = GetCandidateNames(country, leagueName)
            .Select(Normalize)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        foreach (var desiredName in desiredNames)
        {
            var exactMatch = candidates.FirstOrDefault(x => Normalize(x.Name) == desiredName);
            if (exactMatch is not null)
            {
                return exactMatch;
            }
        }

        return null;
    }

    private static IEnumerable<string> GetCandidateNames(string country, string leagueName)
    {
        yield return leagueName;

        foreach (var alias in GetAliases(country, leagueName))
        {
            yield return alias;
        }
    }

    private static IReadOnlyCollection<string> GetAliases(string country, string leagueName)
    {
        var key = $"{country.Trim().ToUpperInvariant()}|{leagueName.Trim().ToUpperInvariant()}";
        return LeagueAliases.TryGetValue(key, out var aliases)
            ? aliases
            : Array.Empty<string>();
    }

    private static string Normalize(string value)
    {
        var decomposed = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var character in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }

    private static readonly IReadOnlyDictionary<string, string[]> LeagueAliases = new Dictionary<string, string[]>
    {
        ["FRANCE|LNB PRO A"] = ["LNB"],
        ["GREECE|A1 ETHNIKI"] = ["Basket League"],
        ["ITALY|SERIE A"] = ["Lega A"],
        ["TURKEY|BSL"] = ["Super Ligi"],
        ["BELGIUM|BLB"] = ["EuroMillions Basketball League", "Pro Basketball League"],
        ["GERMANY|BBL"] = ["BBL"],
        ["ISRAEL|BSL"] = ["Super League"],
        ["POLAND|PLK"] = ["Tauron Basket Liga"],
        ["RUSSIA|RUSSIA TOP TIER"] = ["Super League", "PBL"],
        ["SPAIN|COPA DEL REY"] = ["Spanish Cup"]
    };
}
