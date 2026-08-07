# European historical ingestion coverage

This is the consolidated source and cutoff record for the European club and
domestic competitions added or repaired for the pre-2008 historical gap. It is
the documentation companion to the catalog in
[`BackfillCatalog.cs`](../src/BasketElo.Infrastructure/Backfill/BackfillCatalog.cs).

All entries below are ingested into the Europe clubs pool unless the linked
competition runbook states otherwise. These imports do not rebuild ELOs.

## Domestic leagues

| Competition | Historical provider and source | Clean source cutoff | Catalog coverage | Verified games |
| --- | --- | --- | --- | ---: |
| Czech NBL | `flashscore-czech-nbl`; Flashscore results feed with repeated “Show more matches” pagination | 2000-2001 | 2000-2001 through 2007-2008; API-Sports from 2008-2009 | 1,930 |
| Lithuanian LKL | `eurobasket-lithuania`; Eurobasket team game pages | 2008-2009 | 2008-2009 through 2010-2011; API-Sports from 2011-2012 | 468 |
| Polish PLK / Tauron Basket Liga | `flashscore-poland-plk`; Flashscore results feed with repeated “Show more matches” pagination | 2001-2002 | 2001-2002 through 2007-2008; API-Sports from 2008-2009 | 1,506 |
| Baltic Basketball League | `basketball-database`; archived league schedules | 2004-2005 | 2004-2005 through 2007-2008; API-Sports from 2009 | catalog-complete historical segment |

“Clean source cutoff” means the earliest season for which the configured
source exposes enough scored game records to support a season backfill. It is
not a claim that no earlier games existed. The Czech and Polish Flashscore
providers preserve the source's event IDs and fetch every paginated batch until
the page's listed-event count is reached or the source stops returning data.

The Czech NBL season-level verification is:

| Season | Games | First game date | Last game date | Status |
| --- | ---: | --- | --- | --- |
| 2000-2001 | 237 | 2000-09-15 | 2001-05-19 | completed, no warnings |
| 2001-2002 | 221 | 2001-10-02 | 2002-05-26 | completed, no warnings |
| 2002-2003 | 224 | 2002-09-21 | 2003-06-08 | completed, no warnings |
| 2003-2004 | 223 | 2003-09-26 | 2004-05-26 | completed, no warnings |
| 2004-2005 | 222 | 2004-10-16 | 2005-06-01 | completed, no warnings |
| 2005-2006 | 221 | 2005-10-10 | 2006-05-28 | completed, no warnings |
| 2006-2007 | 289 | 2006-10-09 | 2007-06-09 | completed, no warnings |
| 2007-2008 | 293 | 2007-10-03 | 2008-06-07 | completed, no warnings |

The Polish PLK season-level verification is:

| Season | Games | First game date | Last game date | Status |
| --- | ---: | --- | --- | --- |
| 2001-2002 | 249 | 2001-09-19 | 2002-05-22 | completed, no warnings |
| 2002-2003 | 233 | 2002-09-20 | 2003-06-14 | completed, no warnings |
| 2003-2004 | 186 | 2003-10-10 | 2004-05-18 | completed, no warnings |
| 2004-2005 | 171 | 2004-10-15 | 2005-06-01 | completed, no warnings |
| 2005-2006 | 217 | 2005-10-14 | 2006-05-19 | completed, no warnings |
| 2006-2007 | 246 | 2006-10-13 | 2007-05-31 | completed, no warnings |
| 2007-2008 | 204 | 2007-10-12 | 2008-06-04 | completed, no warnings |

For Poland, 2001-2002 is the clean cutoff because it is the earliest
Flashscore season page requested and it exposed 249/249 listed results after
the additional batches were loaded. No earlier PLK season is claimed by this
source segment. The later API-Sports segment begins at 2008-2009, so the
documented historical/modern join is explicit rather than inferred from a
catalog start-date label.

The LKL historical provider was used only where the Eurobasket team pages
were the cleanest available pre-API-Sports source. The existing LKL result is
127 games in 2008-2009, 175 in 2009-2010, and 166 in 2010-2011. The Lithuanian
Cup provider is a separate finals-focused historical competition and must not
be added to LKL season totals.

For Baltic coverage, the 2007-2008 Challenge Cup is a separate competition
and was recovered from the archived official BBL pages as 28 complete games.
The last run completed with warnings because many archived game pages were
incomplete; it is not documented as a complete 110-game regular season. See
[`baltic-basketball-league-ingestion.md`](baltic-basketball-league-ingestion.md).

## Lithuanian Cup

`wikipedia-lithuanian-cup` covers the published Final Four results from
2006-2007 through 2014-2015. The provider intentionally imports the two
published semifinal/final result rows exposed for each edition; it is not a
claim of complete earlier-round coverage. Modern LKF/King Mindaugas Cup
coverage remains on its separate API-Sports catalog row.

## European second-tier lineage

The historical second tier is kept as separate competition families so that
the source's changing names do not collapse unrelated tournaments:

| Family | Catalog provider | Catalog seasons | Runbook |
| --- | --- | --- | --- |
| Saporta Cup | `fiba` | 1967-1968 through 2001-2002 | [`fiba-european-club-ingestion.md`](fiba-european-club-ingestion.md) |
| FIBA European Tier 2 / EuroCup lineage | `fiba` | 2002-2003 through 2007-2008 | [`fiba-european-tier2-ingestion.md`](fiba-european-tier2-ingestion.md) |
| ULEB Cup | `wikipedia-uleb-cup` | 2002-2003 through 2007-2008 | [`uleb-cup-ingestion.md`](uleb-cup-ingestion.md) |
| Korac Cup | `fiba` | 1971-1972 through 2001-2002 | [`korac-cup-ingestion.md`](korac-cup-ingestion.md) |

The Korac Cup cutoff is 2001-2002 because the competition ended after that
edition. The ULEB Cup begins in 2002-2003. The FIBA Tier 2 and ULEB rows are
therefore parallel historical lineages, not duplicate source labels for one
competition.

## Operational verification

For every historical season, verify the coverage row, database game count,
warning count, identity findings, and service health. Use `maxRequests=0` for
the actual import. A clean job means the provider reached the source's own
listed result count; it does not mean the source has coverage before the
documented cutoff. Keep the source URL and parser version on every imported
game so that a later repair can be limited to the affected provider.
