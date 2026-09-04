using Microsoft.AspNetCore.Http;

namespace BasketElo.Web.Localization;

public static class CultureInference
{
    private static readonly HashSet<string> SpanishCountryCodes =
    [
        "AR", "BO", "CL", "CO", "CR", "CU", "DO", "EC", "ES", "GQ", "GT", "HN",
        "MX", "NI", "PA", "PE", "PR", "PY", "SV", "UY", "VE"
    ];

    public static string? Infer(HttpRequest request)
    {
        var countryCode = request.Headers["CF-IPCountry"].FirstOrDefault()
            ?? request.Headers["X-Country-Code"].FirstOrDefault()
            ?? request.Headers["X-Geo-Country"].FirstOrDefault();
        var normalizedCountryCode = countryCode?.Trim().ToUpperInvariant() ?? string.Empty;

        if (SpanishCountryCodes.Contains(normalizedCountryCode))
        {
            return SupportedCultures.Spanish;
        }

        if (normalizedCountryCode.Length == 2 && normalizedCountryCode.All(char.IsAsciiLetter))
        {
            return SupportedCultures.English;
        }

        return InferFromAcceptLanguage(request.Headers.AcceptLanguage.ToString());
    }

    private static string? InferFromAcceptLanguage(string value)
    {
        foreach (var entry in value.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var languageTag = entry.Split(';', 2)[0].Trim();
            if (SupportedCultures.TryNormalize(languageTag, out var cultureName))
            {
                return cultureName;
            }
        }

        return null;
    }
}
