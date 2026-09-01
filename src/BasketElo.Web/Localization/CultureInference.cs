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
        var browserCulture = InferFromAcceptLanguage(request.Headers.AcceptLanguage.ToString());
        if (browserCulture is not null)
        {
            return browserCulture;
        }

        var countryCode = request.Headers["CF-IPCountry"].FirstOrDefault()
            ?? request.Headers["X-Country-Code"].FirstOrDefault()
            ?? request.Headers["X-Geo-Country"].FirstOrDefault();

        if (SpanishCountryCodes.Contains(countryCode?.Trim().ToUpperInvariant() ?? string.Empty))
        {
            return SupportedCultures.Spanish;
        }

        return null;
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
