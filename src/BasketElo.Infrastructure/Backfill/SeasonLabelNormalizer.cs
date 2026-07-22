namespace BasketElo.Infrastructure.Backfill;

public static class SeasonLabelNormalizer
{
    public static string ToCanonicalSeasonLabel(string season, bool usesSingleYearLabel)
        => usesSingleYearLabel ? ToSingleYearLabel(season) : ToFullSeasonLabel(season);

    public static string ToSingleYearLabel(string season)
    {
        var trimmed = season.Trim();
        var pieces = trimmed.Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return pieces.Length > 0 && int.TryParse(pieces[0], out var year)
            ? year.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : trimmed;
    }

    public static string ToFullSeasonLabel(string season)
    {
        var trimmed = season.Trim();
        var pieces = trimmed.Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (pieces.Length == 1 && int.TryParse(pieces[0], out var singleYear))
        {
            return $"{singleYear}-{singleYear + 1}";
        }

        if (pieces.Length == 2 &&
            int.TryParse(pieces[0], out var startYear) &&
            TryParseEndYear(startYear, pieces[1], out var endYear))
        {
            return $"{startYear}-{endYear}";
        }

        return trimmed;
    }

    public static int ParseStartYear(string season)
    {
        var normalized = ToFullSeasonLabel(season);
        var pieces = normalized.Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return pieces.Length > 0 && int.TryParse(pieces[0], out var startYear) ? startYear : 2000;
    }

    public static bool TryParseSeason(string? season, out string normalized, out int startYear)
    {
        normalized = string.Empty;
        startYear = default;
        if (string.IsNullOrWhiteSpace(season))
        {
            return false;
        }

        normalized = ToFullSeasonLabel(season);
        var pieces = normalized.Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return pieces.Length == 2 &&
            int.TryParse(pieces[0], out startYear) &&
            int.TryParse(pieces[1], out var endYear) &&
            startYear >= 1900 &&
            endYear == startYear + 1;
    }

    public static bool IsSingleYearSeason(string season)
        => !season.Trim().Contains('-', StringComparison.Ordinal);

    private static bool TryParseEndYear(int startYear, string value, out int endYear)
    {
        if (!int.TryParse(value, out var parsed))
        {
            endYear = default;
            return false;
        }

        endYear = value.Length <= 2
            ? (startYear / 100 * 100) + parsed
            : parsed;

        if (endYear < startYear)
        {
            endYear += 100;
        }

        return true;
    }
}
