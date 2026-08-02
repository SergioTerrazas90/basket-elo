namespace BasketElo.Infrastructure.Backfill;

public sealed class BackfillOptions
{
    public const string SectionName = "Backfill";

    public bool QueueEloRebuildsAutomatically { get; set; }
}
