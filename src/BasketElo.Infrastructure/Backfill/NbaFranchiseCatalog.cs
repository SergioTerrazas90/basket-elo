namespace BasketElo.Infrastructure.Backfill;

public static class NbaFranchiseCatalog
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<NbaFranchiseRelocation>> RelocationsByFranchise =
        new Dictionary<string, IReadOnlyList<NbaFranchiseRelocation>>(StringComparer.Ordinal)
        {
            ["hawks"] =
            [
                R(1951, "Tri-Cities Blackhawks", "Milwaukee Hawks"),
                R(1955, "Milwaukee Hawks", "St. Louis Hawks"),
                R(1968, "St. Louis Hawks", "Atlanta Hawks")
            ],
            ["nets"] =
            [
                R(1968, "New Jersey Americans", "New York Nets"),
                R(1977, "New York Nets", "New Jersey Nets"),
                R(2012, "New Jersey Nets", "Brooklyn Nets")
            ],
            ["pistons"] = [R(1957, "Fort Wayne Pistons", "Detroit Pistons")],
            ["warriors"] =
            [
                R(1962, "Philadelphia Warriors", "San Francisco Warriors"),
                R(1971, "San Francisco Warriors", "Golden State Warriors")
            ],
            ["rockets"] = [R(1971, "San Diego Rockets", "Houston Rockets")],
            ["clippers"] =
            [
                R(1978, "Buffalo Braves", "San Diego Clippers"),
                R(1984, "San Diego Clippers", "Los Angeles Clippers")
            ],
            ["lakers"] = [R(1960, "Minneapolis Lakers", "Los Angeles Lakers")],
            ["grizzlies"] = [R(2001, "Vancouver Grizzlies", "Memphis Grizzlies")],
            ["pelicans"] =
            [
                R(2005, "New Orleans Hornets", "New Orleans/Oklahoma City Hornets", isTemporary: true),
                R(2007, "New Orleans/Oklahoma City Hornets", "New Orleans Hornets", isTemporary: true)
            ],
            ["thunder"] = [R(2008, "Seattle SuperSonics", "Oklahoma City Thunder")],
            ["sixers"] = [R(1963, "Syracuse Nationals", "Philadelphia 76ers")],
            ["kings"] =
            [
                R(1957, "Rochester Royals", "Cincinnati Royals"),
                R(1972, "Cincinnati Royals", "Kansas City-Omaha Kings"),
                R(1985, "Kansas City Kings", "Sacramento Kings")
            ],
            ["spurs"] = [R(1973, "Dallas Chaparrals", "San Antonio Spurs")],
            ["jazz"] = [R(1979, "New Orleans Jazz", "Utah Jazz")],
            ["wizards"] =
            [
                R(1963, "Chicago Zephyrs", "Baltimore Bullets"),
                R(1973, "Baltimore Bullets", "Capital Bullets")
            ]
        };

    private static readonly IReadOnlyList<NbaFranchise> Franchises =
    [
        Active("hawks", "Atlanta Hawks", A("TRI", "Tri-Cities Blackhawks", 1949, 1950), A("MLH", "Milwaukee Hawks", 1951, 1954), A("STL", "St. Louis Hawks", 1955, 1967), A("ATL", "Atlanta Hawks", 1968)),
        Active("celtics", "Boston Celtics", A("BOS", "Boston Celtics", 1946)),
        Active("nets", "Brooklyn Nets", A("NJA", "New Jersey Americans", 1967, 1967), A("NYA", "New York Nets", 1968, 1976), A("NYN", "New York Nets", 1976, 1976), A("NJN", "New Jersey Nets", 1977, 2011), A("BRK", "Brooklyn Nets", 2012)),
        Active("hornets", "Charlotte Hornets", A("CHH", "Charlotte Hornets", 1988, 2001), A("CHA", "Charlotte Bobcats", 2004, 2013), A("CHO", "Charlotte Hornets", 2014)),
        Active("bulls", "Chicago Bulls", A("CHI", "Chicago Bulls", 1966)),
        Active("cavaliers", "Cleveland Cavaliers", A("CLE", "Cleveland Cavaliers", 1970)),
        Active("mavericks", "Dallas Mavericks", A("DAL", "Dallas Mavericks", 1980)),
        Active("nuggets", "Denver Nuggets", A("DEN", "Denver Nuggets", 1976)),
        Active("pistons", "Detroit Pistons", A("FTW", "Fort Wayne Pistons", 1948, 1956), A("DET", "Detroit Pistons", 1957)),
        Active("warriors", "Golden State Warriors", A("PHW", "Philadelphia Warriors", 1946, 1961), A("SFW", "San Francisco Warriors", 1962, 1970), A("GSW", "Golden State Warriors", 1971)),
        Active("rockets", "Houston Rockets", A("SDR", "San Diego Rockets", 1967, 1970), A("HOU", "Houston Rockets", 1971)),
        Active("pacers", "Indiana Pacers", A("INA", "Indiana Pacers", 1967, 1975), A("IND", "Indiana Pacers", 1976)),
        Active("clippers", "Los Angeles Clippers", A("BUF", "Buffalo Braves", 1970, 1977), A("SDC", "San Diego Clippers", 1978, 1983), A("LAC", "Los Angeles Clippers", 1984)),
        Active("lakers", "Los Angeles Lakers", A("MNL", "Minneapolis Lakers", 1948, 1959), A("LAL", "Los Angeles Lakers", 1960)),
        Active("grizzlies", "Memphis Grizzlies", A("VAN", "Vancouver Grizzlies", 1995, 2000), A("MEM", "Memphis Grizzlies", 2001)),
        Active("heat", "Miami Heat", A("MIA", "Miami Heat", 1988)),
        Active("bucks", "Milwaukee Bucks", A("MIL", "Milwaukee Bucks", 1968)),
        Active("timberwolves", "Minnesota Timberwolves", A("MIN", "Minnesota Timberwolves", 1989)),
        Active("pelicans", "New Orleans Pelicans", A("NOH", "New Orleans Hornets", 2002, 2012), A("NOK", "New Orleans/Oklahoma City Hornets", 2005, 2006), A("NOP", "New Orleans Pelicans", 2013)),
        Active("knicks", "New York Knicks", A("NYK", "New York Knicks", 1946)),
        Active("thunder", "Oklahoma City Thunder", A("SEA", "Seattle SuperSonics", 1967, 2007), A("OKC", "Oklahoma City Thunder", 2008)),
        Active("magic", "Orlando Magic", A("ORL", "Orlando Magic", 1989)),
        Active("sixers", "Philadelphia 76ers", A("SYR", "Syracuse Nationals", 1949, 1962), A("PHI", "Philadelphia 76ers", 1963)),
        Active("suns", "Phoenix Suns", A("PHO", "Phoenix Suns", 1968)),
        Active("blazers", "Portland Trail Blazers", A("POR", "Portland Trail Blazers", 1970)),
        Active("kings", "Sacramento Kings", A("ROC", "Rochester Royals", 1948, 1956), A("CIN", "Cincinnati Royals", 1957, 1971), A("KCO", "Kansas City-Omaha Kings", 1972, 1974), A("KCK", "Kansas City Kings", 1975, 1984), A("SAC", "Sacramento Kings", 1985)),
        Active("spurs", "San Antonio Spurs", A("DTX", "Dallas Chaparrals", 1967, 1969), A("TEX", "Texas Chaparrals", 1970, 1972), A("SAS", "San Antonio Spurs", 1973)),
        Active("raptors", "Toronto Raptors", A("TOR", "Toronto Raptors", 1995)),
        Active("jazz", "Utah Jazz", A("NOJ", "New Orleans Jazz", 1974, 1978), A("UTA", "Utah Jazz", 1979)),
        Active("wizards", "Washington Wizards", A("CHP", "Chicago Packers", 1961, 1961), A("CHZ", "Chicago Zephyrs", 1962, 1962), A("BLT", "Baltimore Bullets", 1963, 1972), A("BAL", "Baltimore Bullets", 1963, 1972), A("CAP", "Capital Bullets", 1973, 1973), A("WSB", "Washington Bullets", 1974, 1996), A("WAS", "Washington Wizards", 1997)),
        Defunct("anderson-packers", "Anderson Packers", A("AND", "Anderson Packers", 1949, 1949)),
        Defunct("baltimore-bullets", "Baltimore Bullets (1947-1954)", A("BLB", "Baltimore Bullets", 1947, 1953)),
        Defunct("chicago-stags", "Chicago Stags", A("CHS", "Chicago Stags", 1946, 1949)),
        Defunct("cleveland-rebels", "Cleveland Rebels", A("CLR", "Cleveland Rebels", 1946, 1946)),
        Defunct("denver-nuggets-1950", "Denver Nuggets (1949-1950)", A("DNN", "Denver Nuggets", 1949, 1949)),
        Defunct("detroit-falcons", "Detroit Falcons", A("DTF", "Detroit Falcons", 1946, 1946)),
        Defunct("indianapolis-jets", "Indianapolis Jets", A("INJ", "Indianapolis Jets", 1948, 1948)),
        Defunct("indianapolis-olympians", "Indianapolis Olympians", A("INO", "Indianapolis Olympians", 1949, 1952)),
        Defunct("pittsburgh-ironmen", "Pittsburgh Ironmen", A("PIT", "Pittsburgh Ironmen", 1946, 1946)),
        Defunct("providence-steamrollers", "Providence Steamrollers", A("PRO", "Providence Steamrollers", 1946, 1948)),
        Defunct("sheboygan-red-skins", "Sheboygan Red Skins", A("SHE", "Sheboygan Red Skins", 1949, 1949)),
        Defunct("st-louis-bombers", "St. Louis Bombers", A("STB", "St. Louis Bombers", 1946, 1949)),
        Defunct("toronto-huskies", "Toronto Huskies", A("TRH", "Toronto Huskies", 1946, 1946)),
        Defunct("waterloo-hawks", "Waterloo Hawks", A("WAT", "Waterloo Hawks", 1949, 1949)),
        Defunct("washington-capitols", "Washington Capitols", A("WSC", "Washington Capitols", 1946, 1950))
    ];

    public static IReadOnlyList<NbaFranchise> All => Franchises;

    public static NbaFranchise? FindByCanonicalName(string canonicalName) =>
        Franchises.FirstOrDefault(franchise =>
            franchise.CanonicalName.Equals(canonicalName.Trim(), StringComparison.OrdinalIgnoreCase));

    public static NbaFranchiseMatch? Resolve(string sourceTeamId, string observedName, int seasonStartYear)
    {
        var normalizedId = sourceTeamId.Trim().ToUpperInvariant();
        foreach (var franchise in Franchises)
        {
            var alias = franchise.Aliases.FirstOrDefault(candidate =>
                candidate.SourceTeamId == normalizedId && candidate.Contains(seasonStartYear));
            alias ??= franchise.Aliases.FirstOrDefault(candidate => candidate.SourceTeamId == normalizedId);
            alias ??= franchise.Aliases.FirstOrDefault(candidate =>
                candidate.Name.Equals(observedName.Trim(), StringComparison.OrdinalIgnoreCase) &&
                candidate.Contains(seasonStartYear));
            if (alias is not null)
            {
                return new NbaFranchiseMatch(franchise, alias);
            }
        }

        return null;
    }

    public static IReadOnlyCollection<string> GetSourceTeamIds(string franchiseKey) =>
        Franchises.Single(franchise => franchise.Key == franchiseKey)
            .Aliases
            .Select(alias => alias.SourceTeamId)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    public static (DateTime? ValidFromUtc, DateTime? ValidToUtc) GetValidity(NbaFranchiseAlias alias)
    {
        var validFrom = new DateTime(alias.StartSeasonYear, 7, 1, 0, 0, 0, DateTimeKind.Utc);
        var validTo = alias.EndSeasonYear.HasValue
            ? new DateTime(alias.EndSeasonYear.Value + 1, 7, 1, 0, 0, 0, DateTimeKind.Utc).AddTicks(-1)
            : (DateTime?)null;
        return (validFrom, validTo);
    }

    private static NbaFranchise Active(string key, string name, params NbaFranchiseAlias[] aliases) =>
        new(key, name, true, aliases, GetRelocations(key));

    private static NbaFranchise Defunct(string key, string name, params NbaFranchiseAlias[] aliases) =>
        new(key, name, false, aliases, []);

    private static NbaFranchiseAlias A(string id, string name, int startSeasonYear, int? endSeasonYear = null) =>
        new(id, name, startSeasonYear, endSeasonYear);

    private static NbaFranchiseRelocation R(int year, string fromName, string toName, bool isTemporary = false) =>
        new(year, fromName, toName, isTemporary);

    private static IReadOnlyList<NbaFranchiseRelocation> GetRelocations(string franchiseKey) =>
        RelocationsByFranchise.GetValueOrDefault(franchiseKey) ?? [];
}

public sealed record NbaFranchise(
    string Key,
    string CanonicalName,
    bool IsActive,
    IReadOnlyList<NbaFranchiseAlias> Aliases,
    IReadOnlyList<NbaFranchiseRelocation> Relocations);

public sealed record NbaFranchiseRelocation(
    int Year,
    string FromName,
    string ToName,
    bool IsTemporary);

public sealed record NbaFranchiseAlias(
    string SourceTeamId,
    string Name,
    int StartSeasonYear,
    int? EndSeasonYear)
{
    public bool Contains(int seasonStartYear) =>
        seasonStartYear >= StartSeasonYear &&
        (!EndSeasonYear.HasValue || seasonStartYear <= EndSeasonYear.Value);
}

public sealed record NbaFranchiseMatch(NbaFranchise Franchise, NbaFranchiseAlias Alias);
