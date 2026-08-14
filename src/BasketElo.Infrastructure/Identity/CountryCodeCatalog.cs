using System.Globalization;

namespace BasketElo.Infrastructure.Identity;

/// <summary>
/// Canonical country-code policy for persisted and displayed country metadata.
/// Current countries use ISO 3166-1 alpha-2 codes. Historical national identities
/// and constituent nations remain source-specific codes because mapping them to a
/// modern country would change the identity represented by the data.
/// </summary>
public static class CountryCodeCatalog
{
    private static readonly IReadOnlyDictionary<string, string> ProviderAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["AEK"] = "GR",
            ["ALG"] = "DZ",
            ["ANG"] = "AO",
            ["ANT"] = "AN",
            ["ARG"] = "AR",
            ["ARM"] = "AM",
            ["ASA"] = "AS",
            ["AUS"] = "AU",
            ["AUT"] = "AT",
            ["AZE"] = "AZ",
            ["BAH"] = "BS",
            ["BAN"] = "BD",
            ["BAR"] = "BB",
            ["BDI"] = "BI",
            ["BEL"] = "BE",
            ["BEN"] = "BJ",
            ["BER"] = "BM",
            ["BFA"] = "BF",
            ["BGR"] = "BG",
            ["BHR"] = "BH",
            ["BHU"] = "BT",
            ["BIH"] = "BA",
            ["BIZ"] = "BZ",
            ["BLB"] = "BL",
            ["BLR"] = "BY",
            ["BLZ"] = "BZ",
            ["BOL"] = "BO",
            ["BOT"] = "BW",
            ["BRA"] = "BR",
            ["BRN"] = "BH",
            ["BRU"] = "BN",
            ["BUL"] = "BG",
            ["BUR"] = "BF",
            ["CAF"] = "CF",
            ["CAL"] = "NC",
            ["CAM"] = "CM",
            ["CAN"] = "CA",
            ["CAY"] = "KY",
            ["CGO"] = "CG",
            ["CHA"] = "TD",
            ["CHI"] = "CL",
            ["CHN"] = "CN",
            ["CIV"] = "CI",
            ["CMR"] = "CM",
            ["COD"] = "CD",
            ["COL"] = "CO",
            ["CON"] = "CG",
            ["CPV"] = "CV",
            ["CRC"] = "CR",
            ["CRO"] = "HR",
            ["CUB"] = "CU",
            ["CYP"] = "CY",
            ["CZE"] = "CZ",
            ["DEN"] = "DK",
            ["DMA"] = "DM",
            ["DOM"] = "DO",
            ["DOR"] = "DO",
            ["ECU"] = "EC",
            ["EGY"] = "EG",
            ["ELS"] = "SV",
            ["EQG"] = "GQ",
            ["ERI"] = "ER",
            ["ESP"] = "ES",
            ["EST"] = "EE",
            ["ETH"] = "ET",
            ["FIN"] = "FI",
            ["FIJ"] = "FJ",
            ["FOR"] = "FO",
            ["FPO"] = "PF",
            ["FRA"] = "FR",
            ["GAB"] = "GA",
            ["GAM"] = "GM",
            ["GBR"] = "GB",
            ["GBS"] = "GW",
            ["GEO"] = "GE",
            ["GEQ"] = "GQ",
            ["GER"] = "DE",
            ["GRE"] = "GR",
            ["GRN"] = "GD",
            ["GUA"] = "GT",
            ["GUI"] = "GN",
            ["GUM"] = "GU",
            ["GUY"] = "GY",
            ["HAI"] = "HT",
            ["HKG"] = "HK",
            ["HOL"] = "NL",
            ["HON"] = "HN",
            ["HUN"] = "HU",
            ["INA"] = "ID",
            ["IND"] = "IN",
            ["IRA"] = "IR",
            ["IRE"] = "IE",
            ["IRI"] = "IR",
            ["IRL"] = "IE",
            ["IRQ"] = "IQ",
            ["ISL"] = "IS",
            ["ISR"] = "IL",
            ["ISV"] = "VI",
            ["ITA"] = "IT",
            ["IVB"] = "VG",
            ["JAM"] = "JM",
            ["JER"] = "JE",
            ["JOR"] = "JO",
            ["JPN"] = "JP",
            ["KAZ"] = "KZ",
            ["KEN"] = "KE",
            ["KGZ"] = "KG",
            ["KOR"] = "KR",
            ["KOS"] = "XK",
            ["KSA"] = "SA",
            ["KUW"] = "KW",
            ["LAT"] = "LV",
            ["LBA"] = "LY",
            ["LBN"] = "LB",
            ["LBR"] = "LR",
            ["LCA"] = "LC",
            ["LES"] = "LS",
            ["LTU"] = "LT",
            ["LUX"] = "LU",
            ["MAC"] = "MO",
            ["MAD"] = "MG",
            ["MAR"] = "MA",
            ["MAS"] = "MY",
            ["MAW"] = "MW",
            ["MDA"] = "MD",
            ["MDV"] = "MV",
            ["MEX"] = "MX",
            ["MGL"] = "MN",
            ["MKD"] = "MK",
            ["MLI"] = "ML",
            ["MLT"] = "MT",
            ["MNE"] = "ME",
            ["MOZ"] = "MZ",
            ["MTN"] = "MR",
            ["NCA"] = "NI",
            ["NED"] = "NL",
            ["NEP"] = "NP",
            ["NGR"] = "NG",
            ["NIG"] = "NE",
            ["NOR"] = "NO",
            ["NZL"] = "NZ",
            ["OMA"] = "OM",
            ["PAK"] = "PK",
            ["PAN"] = "PA",
            ["PAR"] = "PY",
            ["PER"] = "PE",
            ["PHI"] = "PH",
            ["PLE"] = "PS",
            ["POL"] = "PL",
            ["POR"] = "PT",
            ["PRK"] = "KP",
            ["PUR"] = "PR",
            ["QAT"] = "QA",
            ["ROM"] = "RO",
            ["ROU"] = "RO",
            ["RSA"] = "ZA",
            ["RUS"] = "RU",
            ["RWA"] = "RW",
            ["SAM"] = "WS",
            ["SEN"] = "SN",
            ["SEY"] = "SC",
            ["SGP"] = "SG",
            ["SLO"] = "SI",
            ["SOM"] = "SO",
            ["SRI"] = "LK",
            ["SSD"] = "SS",
            ["SUD"] = "SD",
            ["SUI"] = "CH",
            ["SUR"] = "SR",
            ["SVG"] = "VC",
            ["SVK"] = "SK",
            ["SVN"] = "SI",
            ["SWE"] = "SE",
            ["SWI"] = "CH",
            ["SYR"] = "SY",
            ["TAH"] = "PF",
            ["TAI"] = "TW",
            ["TAN"] = "TZ",
            ["TCI"] = "TC",
            ["THA"] = "TH",
            ["TOG"] = "TG",
            ["TPE"] = "TW",
            ["TTO"] = "TT",
            ["TUN"] = "TN",
            ["TUR"] = "TR",
            ["UAE"] = "AE",
            ["UGA"] = "UG",
            ["UKR"] = "UA",
            ["URU"] = "UY",
            ["USA"] = "US",
            ["UZB"] = "UZ",
            ["VEN"] = "VE",
            ["VIE"] = "VN",
            ["VIN"] = "VC",
            ["VIR"] = "VI",
            ["VNM"] = "VN",
            ["XKX"] = "XK",
            ["SMN"] = "SCG",
            ["ZAI"] = "ZR",
            ["ZAM"] = "ZM",
            ["ZIM"] = "ZW"
        };

    private static readonly IReadOnlySet<string> HistoricalCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // England is a constituent nation code, not a synonym for the United Kingdom.
        "ANT", "CIS", "CSK", "CSP", "DDR", "ENG", "FRG", "FRY", "GDR", "SCG", "TCH", "UAR", "URS", "YUG", "ZAI", "ZAR"
    };

    private static readonly IReadOnlyDictionary<string, string> DisplayNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["CZ"] = "Czech Republic",
            ["ENG"] = "England",
            ["GR"] = "Greece",
            ["RU"] = "Russia",
            ["GB"] = "United Kingdom",
            ["US"] = "United States",
            ["XK"] = "Kosovo",
            ["CIS"] = "Commonwealth of Independent States",
            ["DDR"] = "East Germany",
            ["FRG"] = "West Germany",
            ["GDR"] = "East Germany",
            ["SCG"] = "Serbia and Montenegro",
            ["SMN"] = "Serbia and Montenegro",
            ["TCH"] = "Czechoslovakia",
            ["UAR"] = "United Arab Republic",
            ["URS"] = "Soviet Union",
            ["YUG"] = "Yugoslavia",
            ["ZAI"] = "Zaire",
            ["ZAR"] = "Zaire"
        };

    public static string? Normalize(string? countryCode)
    {
        if (string.IsNullOrWhiteSpace(countryCode))
        {
            return null;
        }

        var normalized = countryCode.Trim().ToUpperInvariant();
        if (normalized is "UNK" or "INT")
        {
            return normalized;
        }

        if (normalized == "UK")
        {
            return "GB";
        }

        if (normalized == "EL")
        {
            return "GR";
        }

        if (normalized.Length == 2)
        {
            return normalized;
        }

        if (HistoricalCodes.Contains(normalized))
        {
            return normalized;
        }

        if (ProviderAliases.TryGetValue(normalized, out var alias))
        {
            return alias;
        }

        try
        {
            return new RegionInfo(normalized).TwoLetterISORegionName.ToUpperInvariant();
        }
        catch (ArgumentException)
        {
            return normalized;
        }
    }

    public static string DisplayName(string? countryCode)
    {
        var normalized = Normalize(countryCode);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        if (DisplayNames.TryGetValue(normalized, out var displayName))
        {
            return displayName;
        }

        try
        {
            return new RegionInfo(normalized).EnglishName;
        }
        catch (ArgumentException)
        {
            return normalized;
        }
    }

    public static bool AreEquivalent(string? left, string? right)
        => string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);
}
