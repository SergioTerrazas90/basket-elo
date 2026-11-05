namespace BasketElo.Domain.Admin;

public static class CompetitionGameGapPolicy
{
    public static readonly TimeSpan AlertAfter = TimeSpan.FromDays(14);

    public static bool ShouldAlert(DateTime lastGameOrFollowedSinceUtc, DateTime nowUtc)
    {
        var start = EnsureUtc(lastGameOrFollowedSinceUtc);
        var now = EnsureUtc(nowUtc);
        if (now <= start || IsSummerExclusion(now))
        {
            return false;
        }

        var monitoredTime = now - start;
        for (var year = start.Year; year <= now.Year; year++)
        {
            var exclusionStart = new DateTime(year, 6, 15, 0, 0, 0, DateTimeKind.Utc);
            var exclusionEnd = new DateTime(year, 10, 1, 0, 0, 0, DateTimeKind.Utc);
            var overlapStart = start > exclusionStart ? start : exclusionStart;
            var overlapEnd = now < exclusionEnd ? now : exclusionEnd;

            if (overlapEnd > overlapStart)
            {
                monitoredTime -= overlapEnd - overlapStart;
            }
        }

        return monitoredTime >= AlertAfter;
    }

    public static bool IsSummerExclusion(DateTime utc)
    {
        var value = EnsureUtc(utc);
        var start = new DateTime(value.Year, 6, 15, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(value.Year, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        return value >= start && value < end;
    }

    private static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
