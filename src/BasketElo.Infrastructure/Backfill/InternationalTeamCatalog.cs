using System.Globalization;
using System.Text;
using BasketElo.Infrastructure.Identity;

namespace BasketElo.Infrastructure.Backfill;

/// <summary>
/// Canonical identity rules for national teams. Provider codes remain useful
/// as stable source identifiers, but they must never become the displayed team
/// name when a full country name is available.
/// </summary>
public static class InternationalTeamCatalog
{
    private static readonly IReadOnlyDictionary<string, string> NamesByCode =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ALB"] = "Albania",
            ["ALG"] = "Algeria",
            ["ANG"] = "Angola",
            ["ARG"] = "Argentina",
            ["ASA"] = "American Samoa",
            ["AUS"] = "Australia",
            ["AUT"] = "Austria",
            ["AZE"] = "Azerbaijan",
            ["BAH"] = "Bahamas",
            ["BAN"] = "Bangladesh",
            ["BDI"] = "Burundi",
            ["BEL"] = "Belgium",
            ["BEN"] = "Benin",
            ["BIH"] = "Bosnia and Herzegovina",
            ["BLR"] = "Belarus",
            ["BOL"] = "Bolivia",
            ["BOT"] = "Botswana",
            ["BRA"] = "Brazil",
            ["BRN"] = "Bahrain",
            ["BUL"] = "Bulgaria",
            ["BUR"] = "Burkina Faso",
            ["CAF"] = "Central African Republic",
            ["CAL"] = "New Caledonia",
            ["CAN"] = "Canada",
            ["CGO"] = "Republic of the Congo",
            ["CHA"] = "Chad",
            ["CHI"] = "Chile",
            ["CHN"] = "China",
            ["CIS"] = "Commonwealth of Independent States",
            ["CIV"] = "Côte d'Ivoire",
            ["CMR"] = "Cameroon",
            ["COD"] = "Democratic Republic of the Congo",
            ["COL"] = "Colombia",
            ["CON"] = "Republic of the Congo",
            ["CPV"] = "Cabo Verde",
            ["CRO"] = "Croatia",
            ["CUB"] = "Cuba",
            ["CYP"] = "Cyprus",
            ["CZE"] = "Czech Republic",
            ["DDR"] = "East Germany",
            ["DEN"] = "Denmark",
            ["DOM"] = "Dominican Republic",
            ["ECU"] = "Ecuador",
            ["EGY"] = "Egypt",
            ["ENG"] = "England",
            ["ESP"] = "Spain",
            ["EST"] = "Estonia",
            ["ETH"] = "Ethiopia",
            ["FIN"] = "Finland",
            ["FIJ"] = "Fiji",
            ["FOR"] = "Faroe Islands",
            ["FPO"] = "French Polynesia",
            ["FRA"] = "France",
            ["GAB"] = "Gabon",
            ["GAM"] = "Gambia",
            ["GBR"] = "Great Britain",
            ["GBS"] = "Guinea-Bissau",
            ["GEO"] = "Georgia",
            ["GEQ"] = "Equatorial Guinea",
            ["GER"] = "Germany",
            ["GRE"] = "Greece",
            ["GUI"] = "Guinea",
            ["GUM"] = "Guam",
            ["HKG"] = "Hong Kong",
            ["HUN"] = "Hungary",
            ["INA"] = "Indonesia",
            ["IND"] = "India",
            ["IRI"] = "Iran",
            ["IRL"] = "Ireland",
            ["IRQ"] = "Iraq",
            ["ISL"] = "Iceland",
            ["ISR"] = "Israel",
            ["ISV"] = "U.S. Virgin Islands",
            ["ITA"] = "Italy",
            ["JOR"] = "Jordan",
            ["JPN"] = "Japan",
            ["KAZ"] = "Kazakhstan",
            ["KEN"] = "Kenya",
            ["KOR"] = "South Korea",
            ["KSA"] = "Saudi Arabia",
            ["KUW"] = "Kuwait",
            ["LAT"] = "Latvia",
            ["LBA"] = "Libya",
            ["LBN"] = "Lebanon",
            ["LTU"] = "Lithuania",
            ["LUX"] = "Luxembourg",
            ["MAD"] = "Madagascar",
            ["MAR"] = "Morocco",
            ["MAS"] = "Malaysia",
            ["MAW"] = "Malawi",
            ["MEX"] = "Mexico",
            ["MGL"] = "Mongolia",
            ["MKD"] = "North Macedonia",
            ["MLI"] = "Mali",
            ["MLT"] = "Malta",
            ["MNE"] = "Montenegro",
            ["MOZ"] = "Mozambique",
            ["MTN"] = "Mauritania",
            ["NED"] = "Netherlands",
            ["NGR"] = "Nigeria",
            ["NIG"] = "Niger",
            ["NOR"] = "Norway",
            ["NZL"] = "New Zealand",
            ["OMA"] = "Oman",
            ["PAN"] = "Panama",
            ["PAR"] = "Paraguay",
            ["PER"] = "Peru",
            ["PHI"] = "Philippines",
            ["PLE"] = "Palestine",
            ["POL"] = "Poland",
            ["POR"] = "Portugal",
            ["PUR"] = "Puerto Rico",
            ["PRK"] = "North Korea",
            ["QAT"] = "Qatar",
            ["ROU"] = "Romania",
            ["RSA"] = "South Africa",
            ["RUS"] = "Russia",
            ["RWA"] = "Rwanda",
            ["SAM"] = "Samoa",
            ["SCG"] = "Serbia and Montenegro",
            ["SMN"] = "Serbia and Montenegro",
            ["SCO"] = "Scotland",
            ["SEN"] = "Senegal",
            ["SEY"] = "Seychelles",
            ["SGP"] = "Singapore",
            ["SLO"] = "Slovenia",
            ["SOM"] = "Somalia",
            ["SRB"] = "Serbia",
            ["SRI"] = "Sri Lanka",
            ["SSD"] = "South Sudan",
            ["SUD"] = "Sudan",
            ["SUI"] = "Switzerland",
            ["SVK"] = "Slovakia",
            ["SWE"] = "Sweden",
            ["SYR"] = "Syria",
            ["TAH"] = "Tahiti",
            ["TAN"] = "Tanzania",
            ["TCH"] = "Czechoslovakia",
            ["THA"] = "Thailand",
            ["TOG"] = "Togo",
            ["TPE"] = "Chinese Taipei",
            ["TUN"] = "Tunisia",
            ["TUR"] = "Turkey",
            ["UAE"] = "United Arab Emirates",
            ["UAR"] = "United Arab Republic",
            ["UGA"] = "Uganda",
            ["UKR"] = "Ukraine",
            ["URS"] = "Soviet Union",
            ["URU"] = "Uruguay",
            ["USA"] = "United States",
            ["VEN"] = "Venezuela",
            ["VIE"] = "Vietnam",
            ["WAL"] = "Wales",
            ["YUG"] = "Yugoslavia",
            ["ZAM"] = "Zambia",
            ["ZAR"] = "Zaire",
            ["ZIM"] = "Zimbabwe"
        };

    private static readonly IReadOnlyDictionary<string, string> CodesByName =
        NamesByCode
            .GroupBy(x => NormalizeName(x.Value), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First().Key, StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlySet<string> HistoricalCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "CIS", "DDR", "FRG", "GDR", "SCG", "SMN", "TCH", "UAR", "URS", "YUG", "ZAI", "ZAR"
    };

    private static readonly IReadOnlySet<string> HistoricalNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Commonwealth of Independent States",
        "Czechoslovakia",
        "East Germany",
        "German DR",
        "Malaya",
        "Serbia and Montenegro",
        "South Vietnam",
        "Soviet Union",
        "USSR",
        "United Arab Republic",
        "West Germany",
        "Yugoslavia",
        "Zaire"
    };

    public static bool IsHistoricalIdentity(string? canonicalName, string? countryCode)
    {
        var normalizedCode = NormalizeCode(countryCode);
        return HistoricalCodes.Contains(normalizedCode) ||
               HistoricalNames.Contains(canonicalName ?? string.Empty);
    }

    public static bool TryResolve(
        string? sourceTeamId,
        string? observedName,
        string? observedCountryCode,
        out string canonicalName,
        out string countryCode)
    {
        foreach (var candidate in new[] { sourceTeamId, observedCountryCode, observedName })
        {
            var code = NormalizeCode(candidate);
            if (code is not null && NamesByCode.TryGetValue(code, out var name))
            {
                canonicalName = name;
                countryCode = CanonicalCode(code);
                return true;
            }
        }

        var normalizedName = NormalizeName(observedName);
        if (normalizedName == "FRYUGOSLAVIA")
        {
            canonicalName = "Serbia and Montenegro";
            countryCode = "SCG";
            return true;
        }

        if (CodesByName.TryGetValue(normalizedName, out var nameCode))
        {
            canonicalName = NamesByCode[nameCode];
            countryCode = CanonicalCode(nameCode);
            return true;
        }

        canonicalName = string.Empty;
        countryCode = string.Empty;
        return false;
    }

    public static bool TryGetCanonicalName(string? code, out string canonicalName)
    {
        var normalizedCode = NormalizeCode(code);
        if (!string.IsNullOrWhiteSpace(normalizedCode) && NamesByCode.TryGetValue(normalizedCode, out var name))
        {
            canonicalName = name;
            return true;
        }

        canonicalName = string.Empty;
        return false;
    }

    public static string NormalizeCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim().ToUpperInvariant();
        return normalized.Length is > 0 and <= 3 && normalized.All(char.IsLetter)
            ? normalized
            : string.Empty;
    }

    private static string CanonicalCode(string code)
        => CountryCodeCatalog.Normalize(code) ?? code.ToUpperInvariant();

    private static string NormalizeName(string? value)
        => string.Concat((value ?? string.Empty)
            .Normalize(NormalizationForm.FormD)
            .Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(character)))
            .ToUpperInvariant();
}
