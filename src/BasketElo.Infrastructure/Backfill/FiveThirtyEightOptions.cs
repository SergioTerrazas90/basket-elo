namespace BasketElo.Infrastructure.Backfill;

public sealed class FiveThirtyEightOptions
{
    public const string SectionName = "FiveThirtyEight";

    public string ArchivePath { get; set; } = "data/fivethirtyeight/nbaallelo.csv";
    public string SourceUrl { get; set; } =
        "https://github.com/fivethirtyeight/data/blob/4c1ff5e3aef1816ae04af63218015066e186c147/nba-elo/nbaallelo.csv";
    public string SourceRevision { get; set; } = "4c1ff5e3aef1816ae04af63218015066e186c147";
    public string ExpectedSha256 { get; set; } =
        "d46ed3540ee8d9eca31b3e94cc8c777e0be5156173d814ebf65b8195e8d616bc";
}
