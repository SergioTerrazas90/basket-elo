using System.Globalization;

namespace BasketElo.Web.Localization;

public static class SupportedCultures
{
    public const string English = "en-US";
    public const string Spanish = "es-ES";
    public const string CultureCookieName = "BasketElo.Culture";

    public static IReadOnlyList<CultureOption> All { get; } =
    [
        new(English, "English", "EN"),
        new(Spanish, "Español", "ES")
    ];

    public static bool TryNormalize(string? value, out string cultureName)
    {
        var normalized = value?.Trim().Replace('_', '-');
        var language = normalized?.Split('-', 2)[0];
        if (string.Equals(language, "en", StringComparison.OrdinalIgnoreCase))
        {
            cultureName = English;
            return true;
        }

        if (string.Equals(language, "es", StringComparison.OrdinalIgnoreCase))
        {
            cultureName = Spanish;
            return true;
        }

        cultureName = string.Empty;
        return false;
    }

    public static CultureInfo GetCulture(string cultureName)
        => CultureInfo.GetCultureInfo(cultureName);

    public sealed record CultureOption(string Name, string Label, string ShortLabel);
}
