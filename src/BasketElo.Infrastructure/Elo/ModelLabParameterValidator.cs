using BasketElo.Domain.Elo;

namespace BasketElo.Infrastructure.Elo;

public static class ModelLabParameterValidator
{
    public static void Validate(ModelLabParameterSet parameters)
    {
        if (parameters.KFactor is < 1 or > 100)
        {
            throw new ArgumentException("K factor must be between 1 and 100.");
        }

        if (parameters.BaseRating is < 500m or > 2500m)
        {
            throw new ArgumentException("Base rating must be between 500 and 2500.");
        }

        if (parameters.HomeAdvantageElo is < -200m or > 300m)
        {
            throw new ArgumentException("Home advantage must be between -200 and 300 ELO.");
        }

        if (parameters.ProbabilityScale is < 200m or > 800m)
        {
            throw new ArgumentException("Probability scale must be between 200 and 800 ELO.");
        }

        if (parameters.CompetitionWeight is <= 0m or > 3m)
        {
            throw new ArgumentException("Competition weight must be greater than 0 and at most 3.");
        }

        if (parameters.MarginDampenerFactor is < 1m or > 20m)
        {
            throw new ArgumentException("Margin dampener factor must be between 1 and 20.");
        }

        if (parameters.MaxMarginMultiplier is < 1m or > 3m)
        {
            throw new ArgumentException("Max margin multiplier must be between 1 and 3.");
        }

        if (parameters.UsesMarginAdjustment &&
            (!parameters.PointsPerEloMargin.HasValue || parameters.PointsPerEloMargin.Value is < 5m or > 100m))
        {
            throw new ArgumentException("Point margin scale must be between 5 and 100 ELO per point when margin adjustment is on.");
        }
    }
}
