using BasketElo.Infrastructure.Backfill;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

return await RunAsync(args);

static async Task<int> RunAsync(string[] args)
{
    AuditCommandOptions command;
    try
    {
        command = AuditCommandOptions.Parse(args);
    }
    catch (ArgumentException exception)
    {
        Console.Error.WriteLine(exception.Message);
        PrintUsage();
        return 1;
    }

    if (command.ShowHelp)
    {
        PrintUsage();
        return 0;
    }

    var builder = Host.CreateApplicationBuilder();
    builder.Services.Configure<BasketballReferenceOptions>(
        builder.Configuration.GetSection(BasketballReferenceOptions.SectionName));
    builder.Services.AddSingleton<IBasketballReferenceRateLimiter, BasketballReferenceRateLimiter>();
    builder.Services.AddHttpClient<BasketballReferenceBasketballDataProvider>((serviceProvider, client) =>
    {
        var providerOptions = serviceProvider.GetRequiredService<IOptions<BasketballReferenceOptions>>().Value;
        client.BaseAddress = new Uri(providerOptions.BaseUrl);
        client.Timeout = TimeSpan.FromSeconds(30);
    });
    builder.Services.AddTransient<INbaHistoricalAuditService, NbaHistoricalAuditService>();

    using var host = builder.Build();
    var resumeReport = command.Resume
        ? await NbaAuditReportWriter.ReadResumeReportAsync(command.OutputPath, CancellationToken.None)
        : null;
    var audit = host.Services.GetRequiredService<INbaHistoricalAuditService>();
    var report = await audit.RunAsync(
        new NbaAuditRequest(command.StartSeason, command.EndSeason, command.MaxRequests),
        resumeReport,
        CancellationToken.None);
    await NbaAuditReportWriter.WriteAsync(report, command.OutputPath, CancellationToken.None);

    var failed = report.Seasons.Count(result => result.Status == "failed");
    Console.WriteLine(
        $"NBA audit wrote {report.Seasons.Count} seasons to '{Path.GetFullPath(command.OutputPath)}': " +
        $"{failed} failed, {report.RequestCount} requests, {report.ElapsedMilliseconds} ms, 0 database writes.");
    return failed == 0 ? 0 : 2;
}

static void PrintUsage()
{
    Console.WriteLine("""
        BasketElo NBA historical audit (read-only)

        dotnet run --project src/BasketElo.Tools -- nba-audit \
          --start 1946-1947 --end 1959-1960 \
          --output artifacts/nba-audit-1946-1960.json \
          [--max-requests 0] [--resume]

        Output must be .json or .csv. Resume requires JSON. Provider archive and
        authorized network settings use BasketballReference__* configuration.
        """);
}

file sealed record AuditCommandOptions(
    string StartSeason,
    string EndSeason,
    string OutputPath,
    int MaxRequests,
    bool Resume,
    bool ShowHelp)
{
    public static AuditCommandOptions Parse(string[] args)
    {
        if (args.Length == 0 || args.Any(arg => arg is "--help" or "-h"))
        {
            return new("1946-1947", "1946-1947", string.Empty, 0, false, true);
        }

        var offset = args[0].Equals("nba-audit", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var resume = false;
        for (var index = offset; index < args.Length; index++)
        {
            var argument = args[index];
            if (argument.Equals("--resume", StringComparison.OrdinalIgnoreCase))
            {
                resume = true;
                continue;
            }

            if (!argument.StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Length)
            {
                throw new ArgumentException($"Unknown or incomplete argument '{argument}'.");
            }

            values[argument] = args[++index];
        }

        var start = Required(values, "--start");
        var end = Required(values, "--end");
        _ = NbaHistoricalAuditService.GetSeasonRange(start, end);
        var output = values.GetValueOrDefault("--output") ??
            $"artifacts/nba-audit-{start}-{end}.json";
        var maxRequestsText = values.GetValueOrDefault("--max-requests") ?? "0";
        if (!int.TryParse(maxRequestsText, out var maxRequests) || maxRequests < 0)
        {
            throw new ArgumentException("--max-requests must be a non-negative integer.");
        }

        var extension = Path.GetExtension(output);
        if (!extension.Equals(".json", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".csv", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("--output must end in .json or .csv.");
        }

        if (resume && !extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("--resume requires JSON output.");
        }

        return new(start, end, output, maxRequests, resume, false);
    }

    private static string Required(IReadOnlyDictionary<string, string> values, string name) =>
        values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"{name} is required.");
}
