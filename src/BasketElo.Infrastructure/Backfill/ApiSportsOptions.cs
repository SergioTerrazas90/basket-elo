namespace BasketElo.Infrastructure.Backfill;

public class ApiSportsOptions
{
    public const string SectionName = "ApiSports";

    public string BaseUrl { get; set; } = "https://v1.basketball.api-sports.io";
    public string? ApiKey { get; set; }
}
