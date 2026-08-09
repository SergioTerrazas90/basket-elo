namespace BasketElo.Domain.Tournaments;

public static class TournamentCycleCatalog
{
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
        var isAfroBasketCompetition = string.Equals(country, "Africa", StringComparison.OrdinalIgnoreCase) &&
            (normalized.Equals("AfroBasket", StringComparison.OrdinalIgnoreCase) ||
             normalized.Equals("FIBA AfroBasket", StringComparison.OrdinalIgnoreCase) ||
             normalized.Equals("AfroBasket Qualifiers", StringComparison.OrdinalIgnoreCase) ||
             normalized.Equals("FIBA AfroBasket Qualifiers", StringComparison.OrdinalIgnoreCase) ||
             normalized.Equals("AfroBasket Pre-Qualifiers", StringComparison.OrdinalIgnoreCase) ||
             normalized.Equals("FIBA AfroBasket Pre-Qualifiers", StringComparison.OrdinalIgnoreCase));
        var isAsiaCupCompetition = string.Equals(country, "Asia", StringComparison.OrdinalIgnoreCase) &&
            (normalized.Equals("FIBA Asia Cup", StringComparison.OrdinalIgnoreCase) ||
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
        var isWorldCupCompetition = string.Equals(country, "World", StringComparison.OrdinalIgnoreCase) &&
            (normalized.Equals("FIBA Basketball World Cup", StringComparison.OrdinalIgnoreCase) ||
             normalized.Equals("FIBA Basketball World Cup Qualifiers", StringComparison.OrdinalIgnoreCase) ||
             normalized.Equals("FIBA Basketball World Cup Pre-Qualifiers", StringComparison.OrdinalIgnoreCase) ||
             normalized.Equals("FIBA WC Qualification", StringComparison.OrdinalIgnoreCase));

        return isEuroBasketCompetition
            ? $"eurobasket-{seasonLabel.Trim()}"
            : isAfroBasketCompetition
                ? $"afrobasket-{seasonLabel.Trim()}"
                : isAsiaCupCompetition
                    ? $"asiacup-{seasonLabel.Trim()}"
                    : isAmeriCupCompetition
                        ? $"americup-{seasonLabel.Trim()}"
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
                                                ? $"worldcup-{seasonLabel.Trim()}"
                                            : isOlympicsCompetition
                                                ? $"olympics-{seasonLabel.Trim()}"
                                                : null;
    }

    public static string DisplayName(string family, string editionLabel)
        => $"{family} {editionLabel}";
}
