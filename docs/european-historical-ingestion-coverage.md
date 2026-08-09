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
| Israeli Super League | `official-israel-basket`; official basket.co.il season and Board selectors | 1953-1954 | 1953-1954 through 2007-2008; API-Sports from 2008-2009 | 7,725 league + 16 Winner Cup |
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

## Additional European domestic targets

The following four target leagues were probed and backfilled where the
configured API-Sports source actually exposes scored games. These imports did
not rebuild ELOs.

| Competition | Provider | Clean cutoff | VPS result | Notes |
| --- | --- | --- | --- | --- |
| Latvian LBL | `api-sports`, league 146 | 2011-2012 | 2011-2012 through 2017-2018 complete; 2018-2019 partial (20), 2019-2020 no data, 2020-2021 partial (23) | 2010-2011 and earlier are not exposed by this provider |
| Croatian Premijer liga | `api-sports`, league 30 | 2008-2009 | 2008-2009 present (64); 2009-2010 and earlier returned no data | Existing coverage continues from 2010-2011 onward |
| Belgian top tier | `api-sports`, leagues 24/374 | 2010-2011 | 2010-2011 onward present | 2009-2010 and earlier returned no data |
| Russian PBL | `api-sports`, league 188 | 2011-2012 | 2011-2012: 111; 2012-2013: 58 | Earlier PBL seasons are not exposed; VTB coverage remains a separate competition |

The Latvia and Russia jobs were queued with `maxRequests=0`, completed without
warnings, and are idempotent API-Sports imports. The VPS backup preceding the
write was `basket_elo_pre_lv_ru_backfill_20260807T091648Z.dump`.

### Flashscore domestic follow-up

The follow-up source probe added the configurable `flashscore-domestic`
provider. It uses the Flashscore paginated results feeds and preserves the
source event/team IDs. The Russian historical pages are served from the
Flashscore Ghana domain, while the Croatian, Belgian, and Latvian routes use
the primary Flashscore domains. These imports also did not rebuild ELOs. The
VPS backup preceding the write was
`basket_elo_pre_flashscore_domestic_20260807T1725Z.dump`.

Representative source pages: [Russia PBL 2007-2008](https://www.flashscore.com.gh/basketball/russia/pbl-2007-2008/results/), [Croatia A1 Liga 2008-2009](https://www.flashscore.info/basketball/croatia/premijer-liga-2008-2009/results/), [Belgium Ethias League 2009-2010](https://www.flashscore.com/basketball/belgium/pro-basketball-league-2009-2010/results/), and [Latvia LBL 2018-2019](https://www.flashscore.com/basketball/latvia/lbl-2018-2019/results/).

| Competition | Source cutoff found | Season | Source games | Database result | Status |
| --- | --- | --- | ---: | ---: | --- |
| Russian PBL / Superleague A | 2005-2006 | 2005-2006 | 206 | 206 inserted | complete, no warnings |
| Russian PBL / Superleague A | 2005-2006 | 2006-2007 | 204 | 204 inserted | complete, no warnings |
| Russian PBL / Superleague A | 2005-2006 | 2007-2008 | 210 | 210 inserted | complete, no warnings |
| Russian PBL / Superleague A | 2005-2006 | 2008-2009 | 174 | 174 inserted | complete, no warnings |
| Croatian A1 Liga / Premijer liga | 2008-2009 | 2008-2009 | 64 | 0 inserted; 64 deduplicated | matches existing API-Sports coverage |
| Croatian A1 Liga / Premijer liga | 2008-2009 | 2009-2010 | 105 | 105 inserted | incomplete source feed; warning |
| Croatian A1 Liga / Premijer liga | 2008-2009 | 2010-2011 | 183 | 0 inserted; 183 deduplicated | matches existing API-Sports coverage |
| Croatian A1 Liga / Premijer liga | 2008-2009 | 2011-2012 | 205 | 0 inserted; 205 deduplicated | matches existing API-Sports coverage |
| Croatian A1 Liga / Premijer liga | 2008-2009 | 2012-2013 | 186 | 0 inserted; 186 deduplicated | matches existing API-Sports coverage |
| Croatian A1 Liga / Premijer liga | 2008-2009 | 2013-2014 | 175 | 0 inserted; 175 deduplicated | matches existing API-Sports coverage |

The Russian Flashscore source is the cleanest newly discovered extension: its
documented cutoff is 2005-2006 and 794 games were added through 2008-2009.
Croatia's Flashscore pages confirm the 2008-2009 API boundary and later
seasons, but only the 2009-2010 feed added new rows. That season is retained as
`completed_with_warnings` because Flashscore advertised 156 events while the
usable result feed exposed 105; it must not be treated as a complete league
season without a second source.

Belgium's 2009-2010 Flashscore page exposed 105 usable games against a listed
156-event count and no pagination token, so it was dry-run only and was not
imported. Latvia's 2018-2019 Flashscore page exposed the same 20-game partial
segment already present from API-Sports, so it was also validation-only. The
remaining Belgian and Latvian gaps stay open rather than being filled with
unverified partial data.

The clean source cutoffs are therefore: Russia 2005-2006, Croatia 2008-2009,
Belgium unresolved before 2010-2011, and Latvia unresolved before 2011-2012.

### Latvia LBL season verification

| Season | Games | First game | Last game | Result |
| --- | ---: | --- | --- | --- |
| 2011-2012 | 122 | 2011-10-08 | 2012-05-20 | complete |
| 2012-2013 | 185 | 2012-10-03 | 2013-05-27 | complete |
| 2013-2014 | 220 | 2013-10-01 | 2014-05-17 | complete |
| 2014-2015 | 165 | 2014-10-01 | 2015-05-28 | complete |
| 2015-2016 | 208 | 2015-09-29 | 2016-05-31 | complete |
| 2016-2017 | 170 | 2016-09-28 | 2017-05-25 | complete |
| 2017-2018 | 143 | 2017-09-27 | 2018-06-06 | complete |
| 2018-2019 | 20 | 2019-04-09 | 2019-05-17 | partial provider coverage |
| 2019-2020 | 0 | — | — | no provider data |
| 2020-2021 | 23 | 2021-04-13 | 2021-05-17 | partial provider coverage |

The 2018-2019 and 2020-2021 rows must not be treated as complete league
seasons. The API-Sports season metadata itself starts late in those editions.

### Russian PBL season verification

| Season | Games | First game | Last game | Result |
| --- | ---: | --- | --- | --- |
| 2011-2012 | 111 | 2011-10-06 | 2012-05-19 | complete provider return |
| 2012-2013 | 58 | 2012-10-03 | 2013-05-12 | complete provider return |

These PBL seasons are separate from the multinational VTB United League
competition. No historical PBL games before 2011-2012 were exposed by
API-Sports.

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
