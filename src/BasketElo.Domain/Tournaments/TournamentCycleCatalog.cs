namespace BasketElo.Domain.Tournaments;

public static class TournamentCycleCatalog
{
    public static IReadOnlyList<string> SupportedFamilies { get; } =
    [
        "EuroBasket",
        "EuroBasket Division B",
        "AfroBasket",
        "FIBA Asia Cup",
        "FIBA AmeriCup",
        "FIBA Basketball World Cup",
        "Olympics"
    ];

    public static string? ResolveKeyFromFamily(string? family, string? editionLabel)
    {
        if (string.IsNullOrWhiteSpace(family) || string.IsNullOrWhiteSpace(editionLabel))
        {
            return null;
        }

        var normalizedFamily = SupportedFamilies.FirstOrDefault(
            value => string.Equals(value, family.Trim(), StringComparison.OrdinalIgnoreCase));
        if (normalizedFamily is null)
        {
            return null;
        }

        var normalizedEdition = ResolveEditionLabelFromFamily(normalizedFamily, editionLabel);
        if (normalizedEdition is null)
        {
            return null;
        }

        var prefix = normalizedFamily switch
        {
            "EuroBasket" => "eurobasket",
            "EuroBasket Division B" => "eurobasket-division-b",
            "AfroBasket" => "afrobasket",
            "FIBA Asia Cup" => "asiacup",
            "FIBA AmeriCup" => "americup",
            "FIBA Basketball World Cup" => "worldcup",
            "Olympics" => "olympics",
            _ => null
        };

        return prefix is null ? null : $"{prefix}-{normalizedEdition}";
    }

    public static string? ResolveEditionLabelFromFamily(string? family, string? editionLabel)
    {
        if (string.IsNullOrWhiteSpace(family) || string.IsNullOrWhiteSpace(editionLabel))
        {
            return null;
        }

        var normalizedEdition = editionLabel.Trim();
        return family.Trim().ToLowerInvariant() switch
        {
            "eurobasket" when normalizedEdition == "2021" => "2022",
            "fiba asia cup" when normalizedEdition == "1985" => "1986",
            "fiba asia cup" when normalizedEdition == "2021" => "2022",
            "fiba basketball world cup" => TerminalYear(normalizedEdition),
            _ => normalizedEdition
        };
    }

    public static string? ResolveKey(string? country, string? competitionName, string? seasonLabel)
    {
        if (string.IsNullOrWhiteSpace(country) ||
            string.IsNullOrWhiteSpace(competitionName) ||
            string.IsNullOrWhiteSpace(seasonLabel))
        {
            return null;
        }

        var normalized = competitionName.Trim();
        var isEuroBasketCompetition = string.Equals(country, "Europe", StringComparison.OrdinalIgnoreCase) &&
            (normalized.Equals("EuroBasket", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("FIBA EuroBasket", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("EuroBasket Qualifiers", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("FIBA EuroBasket Qualifiers", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("EuroBasket Pre-Qualifiers", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("FIBA EuroBasket Pre-Qualifiers", StringComparison.OrdinalIgnoreCase));
        var isEuroBasketDivisionBCompetition = string.Equals(country, "Europe", StringComparison.OrdinalIgnoreCase) &&
            normalized.Equals("FIBA EuroBasket Division B", StringComparison.OrdinalIgnoreCase);
        var isAfroBasketCompetition = string.Equals(country, "Africa", StringComparison.OrdinalIgnoreCase) &&
            (normalized.Equals("AfroBasket", StringComparison.OrdinalIgnoreCase) ||
             normalized.Equals("FIBA AfroBasket", StringComparison.OrdinalIgnoreCase) ||
             normalized.Equals("AfroBasket Qualifiers", StringComparison.OrdinalIgnoreCase) ||
             normalized.Equals("FIBA AfroBasket Qualifiers", StringComparison.OrdinalIgnoreCase) ||
             normalized.Equals("AfroBasket Pre-Qualifiers", StringComparison.OrdinalIgnoreCase) ||
             normalized.Equals("FIBA AfroBasket Pre-Qualifiers", StringComparison.OrdinalIgnoreCase));
        var isAsiaCupCompetition = string.Equals(country, "Asia", StringComparison.OrdinalIgnoreCase) &&
            (normalized.Equals("FIBA Asia Cup", StringComparison.OrdinalIgnoreCase) ||
             normalized.Equals("FIBA Asia Cup Qualification", StringComparison.OrdinalIgnoreCase) ||
             normalized.Equals("FIBA Asia Cup Qualifiers", StringComparison.OrdinalIgnoreCase) ||
             normalized.Equals("FIBA Asia Cup Pre-Qualifiers", StringComparison.OrdinalIgnoreCase));
        var isAmeriCupCompetition = string.Equals(country, "Americas", StringComparison.OrdinalIgnoreCase) &&
            (normalized.Equals("FIBA AmeriCup", StringComparison.OrdinalIgnoreCase) ||
             normalized.Equals("FIBA Americas Championship", StringComparison.OrdinalIgnoreCase) ||
             normalized.Equals("FIBA AmeriCup Qualification", StringComparison.OrdinalIgnoreCase) ||
             normalized.Equals("FIBA AmeriCup Qualifiers", StringComparison.OrdinalIgnoreCase) ||
             normalized.Equals("FIBA AmeriCup Pre-Qualifiers", StringComparison.OrdinalIgnoreCase));
        var isCentrobasketCompetition = string.Equals(country, "Americas", StringComparison.OrdinalIgnoreCase) &&
            normalized.Equals("Centrobasket Championship", StringComparison.OrdinalIgnoreCase);
        var isCocabaCompetition = string.Equals(country, "Americas", StringComparison.OrdinalIgnoreCase) &&
            normalized.Equals("COCABA Championship", StringComparison.OrdinalIgnoreCase);
        var isSouthAmericanCompetition = string.Equals(country, "Americas", StringComparison.OrdinalIgnoreCase) &&
            normalized.Equals("South American Championship", StringComparison.OrdinalIgnoreCase);
        var isCaribbeanCompetition = string.Equals(country, "Americas", StringComparison.OrdinalIgnoreCase) &&
            normalized.Equals("Caribbean Basketball Championship", StringComparison.OrdinalIgnoreCase);
        var isOceaniaCompetition = string.Equals(country, "Oceania", StringComparison.OrdinalIgnoreCase) &&
            (normalized.Equals("FIBA Oceania Championship", StringComparison.OrdinalIgnoreCase) ||
             normalized.Equals("Oceania Championship", StringComparison.OrdinalIgnoreCase));
        var isOlympicsCompetition = string.Equals(country, "World", StringComparison.OrdinalIgnoreCase) &&
            (normalized.Equals("Summer Olympics", StringComparison.OrdinalIgnoreCase) ||
             normalized.Equals("FIBA Men's Olympic Basketball Tournament", StringComparison.OrdinalIgnoreCase) ||
             normalized.Equals("Olympics Qualification", StringComparison.OrdinalIgnoreCase) ||
             normalized.Equals("FIBA Olympic Qualifying Tournament", StringComparison.OrdinalIgnoreCase) ||
             normalized.Equals("Olympics Pre-Qualification", StringComparison.OrdinalIgnoreCase) ||
             normalized.Equals("FIBA Olympic Pre-Qualifying Tournament", StringComparison.OrdinalIgnoreCase));
        // Live feeds commonly put World Cup qualifiers under a regional
        // confederation. Their competition identity still belongs to the
        // global World Cup cycle.
        var isWorldCupCompetition =
            normalized.Equals("FIBA Basketball World Cup", StringComparison.OrdinalIgnoreCase) ||
             normalized.Equals("FIBA Basketball World Cup Qualifiers", StringComparison.OrdinalIgnoreCase) ||
             normalized.Equals("FIBA Basketball World Cup Pre-Qualifiers", StringComparison.OrdinalIgnoreCase) ||
             normalized.Equals("FIBA World Cup", StringComparison.OrdinalIgnoreCase) ||
             normalized.Equals("FIBA World Cup Qualifiers", StringComparison.OrdinalIgnoreCase) ||
             normalized.Equals("FIBA World Cup Pre-Qualifiers", StringComparison.OrdinalIgnoreCase) ||
             normalized.Equals("FIBA WC Qualification", StringComparison.OrdinalIgnoreCase);

        return isEuroBasketDivisionBCompetition
            ? ResolveKeyFromFamily("EuroBasket Division B", seasonLabel)
            : isEuroBasketCompetition
            ? ResolveKeyFromFamily("EuroBasket", seasonLabel)
            : isAfroBasketCompetition
                ? ResolveKeyFromFamily("AfroBasket", seasonLabel)
                : isAsiaCupCompetition
                    ? ResolveKeyFromFamily("FIBA Asia Cup", seasonLabel)
                    : isAmeriCupCompetition
                        ? ResolveKeyFromFamily("FIBA AmeriCup", seasonLabel)
                        : isCentrobasketCompetition
                            ? $"centrobasket-{seasonLabel.Trim()}"
                            : isCocabaCompetition
                                ? $"cocaba-{seasonLabel.Trim()}"
                                : isSouthAmericanCompetition
                                    ? $"south-american-{seasonLabel.Trim()}"
                                    : isCaribbeanCompetition
                                        ? $"caribbean-{seasonLabel.Trim()}"
                                        : isOceaniaCompetition
                                            ? $"oceania-{seasonLabel.Trim()}"
                                            : isWorldCupCompetition
                                                ? ResolveKeyFromFamily("FIBA Basketball World Cup", seasonLabel)
                                            : isOlympicsCompetition
                                                ? ResolveKeyFromFamily("Olympics", seasonLabel)
                                                : null;
    }

    private static string TerminalYear(string editionLabel)
    {
        var pieces = editionLabel.Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return pieces.Length == 2 &&
               pieces[0].Length == 4 &&
               pieces[1].Length == 4 &&
               int.TryParse(pieces[0], out var firstYear) &&
               int.TryParse(pieces[1], out var secondYear) &&
               secondYear >= firstYear
            ? pieces[1]
            : editionLabel;
    }

    public static string? ResolveFamilyFromKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        var normalized = key.Trim();
        return normalized.StartsWith("eurobasket-division-b-", StringComparison.OrdinalIgnoreCase)
            ? "EuroBasket Division B"
            : normalized.StartsWith("eurobasket-", StringComparison.OrdinalIgnoreCase)
                ? "EuroBasket"
                : normalized.StartsWith("afrobasket-", StringComparison.OrdinalIgnoreCase)
                    ? "AfroBasket"
                    : normalized.StartsWith("asiacup-", StringComparison.OrdinalIgnoreCase)
                        ? "FIBA Asia Cup"
                        : normalized.StartsWith("americup-", StringComparison.OrdinalIgnoreCase)
                            ? "FIBA AmeriCup"
                            : normalized.StartsWith("worldcup-", StringComparison.OrdinalIgnoreCase)
                                ? "FIBA Basketball World Cup"
                                : normalized.StartsWith("olympics-", StringComparison.OrdinalIgnoreCase)
                                    ? "Olympics"
                                    : normalized.StartsWith("oceania-", StringComparison.OrdinalIgnoreCase)
                                        ? "FIBA Oceania Championship"
                                        : normalized.StartsWith("centrobasket-", StringComparison.OrdinalIgnoreCase)
                                            ? "Centrobasket Championship"
                                            : normalized.StartsWith("cocaba-", StringComparison.OrdinalIgnoreCase)
                                                ? "COCABA Championship"
                                                : normalized.StartsWith("south-american-", StringComparison.OrdinalIgnoreCase)
                                                    ? "South American Championship"
                                                    : normalized.StartsWith("caribbean-", StringComparison.OrdinalIgnoreCase)
                                                        ? "Caribbean Basketball Championship"
                                                        : null;
    }

    public static string DisplayName(string family, string editionLabel)
        => $"{family} {editionLabel}";
}
