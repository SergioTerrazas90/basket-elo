Exit code: 0
Wall time: 0.5 seconds
Output:
# Baltic Basketball League ingestion

The Baltic Basketball League was founded in 2004. The catalog keeps its
historical and modern coverage as separate provider segments:

| Coverage | Provider | Seasons |
| --- | --- | --- |
| Historical schedule archive | `basketball-database` | 2004-2005 through 2007-2008 |
| API-Sports | `api-sports` | 2009 through 2017 |

The historical provider reads the Basketball Database season pages for the
four pre-2008 editions. It imports scored schedule rows and preserves the
archive's phase labels, including group, regular-season, challenge-cup, and
playoff records where exposed.

Source archive: [Basketball Database Baltic Basketball League](https://basketball-database.com.court-side.com/csgc/leagues/0/2369).

The historical segment is assigned to the Europe clubs pool. Ingesting this
data does not itself queue or execute an ELO rebuild.

