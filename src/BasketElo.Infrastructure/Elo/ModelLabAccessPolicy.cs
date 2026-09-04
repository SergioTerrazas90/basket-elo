using BasketElo.Domain.Elo;
using BasketElo.Infrastructure.Backfill;

namespace BasketElo.Infrastructure.Elo;

public static class ModelLabAccessPolicy
{
    public static ModelLabOptionsResponse FilterOptions(
        ModelLabEntitlement entitlement,
        ModelLabOptionsResponse options)
    {
        if (entitlement.MinimumSeasonStartYear is not int minimumYear)
        {
            return options;
        }

        var seasons = options.Seasons
            .Where(season => SeasonLabelNormalizer.ParseStartYear(season.Label) >= minimumYear)
            .ToList();

        return options with
        {
            Seasons = seasons,
            FirstGameUtc = seasons.Count == 0 ? null : seasons.Min(season => season.FirstGameUtc)
        };
    }

    public static void EnforceHistoryLimit(
        ModelLabEntitlement entitlement,
        params DateTime[] requestedDates)
    {
        if (entitlement.MinimumSeasonStartYear is not int minimumYear)
        {
            return;
        }

        var minimumDateUtc = new DateTime(minimumYear, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        if (requestedDates.Any(date => date < minimumDateUtc))
        {
            throw new ModelLabLimitException(
                "history_restricted",
                $"{entitlement.PlanKey} users can access Model Lab data from the {minimumYear} season onwards.",
                true,
                entitlement.SavedModelLimit,
                entitlement.RequiredLeagueName,
                entitlement.StoredRunLimit,
                entitlement.MonthlyRunLimit);
        }
    }
}
