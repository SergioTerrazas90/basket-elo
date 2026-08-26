namespace BasketElo.Infrastructure.CurrentResults;

public sealed class CurrentResultsOptions
{
    public const string SectionName = "CurrentResults";

    public bool Enabled { get; set; }
    public string Provider { get; set; } = "livescore";
    public int ScheduleDaysAhead { get; set; } = 7;
    public int ReconcileDaysBack { get; set; } = 1;
    public int DailyReadHourUtc { get; set; } = 5;
    public int SchedulerCheckMinutes { get; set; } = 15;
    public bool DryRun { get; set; }
}

public sealed class LiveScoreOptions
{
    public const string SectionName = "LiveScore";

    public bool Enabled { get; set; }
    public string BaseUrl { get; set; } = "https://www.livescores.com";
    public string SourceTimeZoneId { get; set; } = "UTC";
    public string UserAgent { get; set; } = "BasketElo current-results/1.0";
    public int RequestDelayMilliseconds { get; set; } = 750;
}
