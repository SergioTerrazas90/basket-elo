using System.Text.Json;
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

        var uri = $"/leagues?country={Uri.EscapeDataString(country)}&name={Uri.EscapeDataString(leagueName)}";
        using var request = CreateRequest(uri);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(payload);
        if (!document.RootElement.TryGetProperty("response", out var responseArray) || responseArray.GetArrayLength() == 0)
        {
            return null;
        }

        foreach (var item in responseArray.EnumerateArray())
        {
            var leagueElement = item.TryGetProperty("league", out var nestedLeagueElement)
                ? nestedLeagueElement
                : item;

            if (!leagueElement.TryGetProperty("id", out var idElement))
            {
                continue;
            }

            var id = idElement.ToString();
            var name = leagueElement.TryGetProperty("name", out var nameElement)
                ? nameElement.GetString() ?? leagueName
                : leagueName;
            string? countryCode = null;
            if (item.TryGetProperty("country", out var countryElement))
            {
                countryCode = countryElement.TryGetProperty("code", out var codeElement)
                    ? codeElement.GetString()
                    : null;
            }

            return new BasketballProviderLeague(Source, id, name, countryCode);
        }

        return null;
    }

    public async Task<(IReadOnlyCollection<BasketballProviderGame> Games, bool HasMorePages, IReadOnlyCollection<string> Warnings)> GetGamesAsync(
        BasketballProviderLeague league,
        string season,
        BackfillExecutionContext context,
        CancellationToken cancellationToken)
    {
        EnsureRequestAvailable(context);

        var seasonParameter = string.IsNullOrWhiteSpace(league.CountryCode)
            ? ToStartYearSeason(season)
            : season;
        var uri = $"/games?league={Uri.EscapeDataString(league.SourceLeagueId)}&season={Uri.EscapeDataString(seasonParameter)}";
        using var request = CreateRequest(uri);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(payload);
        var games = new List<BasketballProviderGame>();
        var warnings = ExtractApiWarnings(document.RootElement);

        var hasMorePages = false;
        if (document.RootElement.TryGetProperty("paging", out var pagingElement))
        {
            var current = pagingElement.TryGetProperty("current", out var currentElement) ? currentElement.GetInt32() : 1;
            var total = pagingElement.TryGetProperty("total", out var totalElement) ? totalElement.GetInt32() : 1;
            hasMorePages = total > current;
        }

        if (!document.RootElement.TryGetProperty("response", out var responseArray))
        {
            return (games, hasMorePages, warnings);
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

        return (games, hasMorePages, warnings);
    }

    private static IReadOnlyCollection<string> ExtractApiWarnings(JsonElement root)
    {
        if (!root.TryGetProperty("errors", out var errors) ||
            errors.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return [];
        }

        var warnings = new List<string>();
        if (errors.ValueKind == JsonValueKind.Object)
        {
            foreach (var error in errors.EnumerateObject())
            {
                if (error.Value.ValueKind == JsonValueKind.String)
                {
                    warnings.Add($"API-Sports {error.Name}: {error.Value.GetString()}");
                }
            }
        }
        else if (errors.ValueKind == JsonValueKind.Array)
        {
            warnings.AddRange(errors.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => $"API-Sports: {x.GetString()}"));
        }
        else if (errors.ValueKind == JsonValueKind.String)
        {
            warnings.Add($"API-Sports: {errors.GetString()}");
        }

        return warnings.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
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

    private static string ToStartYearSeason(string season)
    {
        var pieces = season.Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return pieces.Length > 0 && int.TryParse(pieces[0], out var startYear)
            ? startYear.ToString()
            : season;
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
}
