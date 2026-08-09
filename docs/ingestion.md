# Ingestion architecture and source policy

This document describes how BasketELO imports basketball games, how provider
records become canonical database games, and which sources are used for the
international competitions currently in the catalog. The uniform coverage
matrix for EuroBasket, AfroBasket, Asia Cup, and AmeriCup is maintained in
[`fiba-national-team-tournaments.md`](fiba-national-team-tournaments.md).

## Pipeline

Ingestion is an asynchronous backfill pipeline:

1. The backfill catalog defines the provider, country/region, league name,
   competition type, ELO pool, and supported seasons.
2. The API creates a `BackfillJob` for one configured provider/league/season.
3. The worker claims the job and resolves the provider-specific league.
4. The provider fetches and parses source data into normalized provider games.
5. The worker resolves teams, creates or finds the competition and season, and
   upserts games using the provider and source game ID as provenance.
6. The job stores request usage, warnings, source URLs, parser versions,
   identity findings, and a JSON summary.
7. The coverage dashboard compares the latest job with the actual games in the
   database. Counts are provider-specific, so a FIBA row does not count GSA
   records and vice versa.

Jobs are deliberately idempotent. Re-running a season updates the same source
records rather than creating a second copy. Competition aliases allow several
provider names or source competition IDs to resolve to one canonical
competition.

### Flashscore historical domestic feeds

`flashscore-domestic` is used only for reviewed historical domestic-league
routes where the source exposes a scored result feed. The provider loads the
season results page, parses its initial event batch, and follows the source's
`tr_*` pagination feed when the page exposes the required feed identifiers.
Russian PBL pages currently use the indexed `flashscore.com.gh` route; Croatian
and Belgian historical pages use Flashscore routes, and Latvian pages use the
standard `flashscore.com` route. Source event IDs and team IDs are retained in
game provenance, and cross-source duplicate detection prevents a Flashscore
row from duplicating an existing API-Sports result.

A completed job with warnings is not a complete-season assertion. For example,
the Croatian 2009-2010 and Belgian 2009-2010 pages advertised more events than
their usable feeds exposed; those rows remain explicitly marked with warnings
and are not used to claim a clean cutoff. Flashscore domestic imports do not
trigger an ELO rebuild automatically.

## Team and country identity

Country identity has one display rule: use the full canonical country name,
never a provider code. For example, `ESP`, `Spain`, and a provider's numeric
team ID must resolve to the canonical team name `Spain`. The provider code may
remain in `Team.CountryCode` as a compact internal/source key, but it is not a
valid canonical name or ranking label. This applies equally to newly ingested
games and historical games already in the database.

Providers may expose a three-letter code, a full name, or both. Ingestion must
resolve the identity in this order: stable source alias, known international
country-code mapping, then the observed full name. Source spellings are kept
as aliases for auditability. Historical entities such as `YUG`, `URS`, and
`TCH` remain distinct canonical identities (`Yugoslavia`, `Soviet Union`, and
`Czechoslovakia`) rather than being silently merged into successor countries.
The provider label `FR Yugoslavia` is an alias for the post-1992 federation
whose canonical name is `Serbia and Montenegro`; it must be merged with that
identity, but never with historical `Yugoslavia`.

Before accepting an international backfill, verify that no national-team
ranking row is still named only by a code and that the country code is not
`UNK` when the provider supplied a resolvable identity. A bulk identity change
updates the referenced team rows; games keep their stable team foreign keys,
so their historical results remain intact. Rebuild every affected national-team
ELO ruleset after the change and verify that the rankings contain one row per
canonical team identity.

## Providers and their responsibilities

### Global Sports Archive

Global Sports Archive (GSA) is the primary source for the men's international
tournaments where it provides usable individual match pages. It supplies match
dates, times, teams, scores, stages, rounds, source URLs, and source IDs.

The GSA provider traverses an edition as follows:

1. Fetch the configured tournament seed page.
2. Use the source year selector to locate the requested edition.
3. Discover every stage link exposed for that edition.
4. Fetch each stage, including the stage's current page.
5. Follow pagination/page arrows where present.
6. Follow gameweek arrows where present. This is required for group stages and
   window-based qualifiers.
7. Parse and deduplicate matches by GSA match ID.

GSA backfills use `maxRequests=0`, meaning unlimited traversal for the selected
edition. A non-zero value is a deliberate diagnostic limit, not a source-side
limit. The provider does not use a fallback source when GSA is selected.

The provider retains an unresolved fixture when GSA exposes a match without a
final score, recording a warning and leaving it outside ELO eligibility until
the result is resolved. Future/TBD fixtures may be removed from a completed
backfill when they have not yet been played; they should not be presented as
completed games.

Primary GSA source families currently configured:

| Region | Competition families |
| --- | --- |
| Africa | FIBA AfroBasket; FIBA AfroBasket Qualifiers; FIBA AfroBasket Pre-Qualifiers |
| Asia | FIBA Asia Cup; FIBA Asia Cup Qualification; Asian Games |
| Europe | EuroBasket; EuroBasket Qualifiers; FIBA EuroBasket Pre-Qualifiers |
| Americas | FIBA AmeriCup; FIBA AmeriCup Qualifiers; FIBA AmeriCup Pre-Qualifiers |
| Americas regional | Centrobasket Championship; COCABA Championship; South American Championship; Caribbean Basketball Championship |
| Oceania | FIBA Oceania Championship |
| World | FIBA Basketball World Cup; FIBA Basketball World Cup Qualifiers; Summer Olympics (men); Olympics Qualification; Olympics Pre-Qualification |

The 2029 EuroBasket Pre-Qualifiers currently use the GSA Round 1 page
[`eurobasket-qualifiers-2029/round-1/135761`](https://globalsportsarchive.com/competition/basketball/eurobasket-qualifiers-2029/round-1/135761/).
Only games already played are part of the completed 2029 backfill; future
fixtures are not counted as completed data.

### FIBA history

The FIBA historical site is used when GSA does not provide the required older
edition or when the competition family is explicitly sourced from FIBA. The
provider reads the history family, maps official FIBA edition IDs to the target
competition season, and parses the available game pages.

Important mapping rules:

- The year displayed by FIBA is not always the year of the championship being
  qualified for. For example, the 2013 first EuroBasket qualifying tournament
  and the 2014 second qualifying round both belong to the EuroBasket 2015
  qualifier season.
- The target season is therefore assigned by the configured edition map, not by
  the source page year alone.
- Missing official game pages remain provider gaps or inspection cases. We do
  not invent games from standings or qualification outcomes.

FIBA families currently used include:

- EuroBasket Qualifiers: historical seasons through 2015, with the 1991 and
  1993 game lists sourced from Wikipedia because the FIBA historical entries do
  not expose usable games. The 2005 qualifier season is a special case: FIBA exposes it
  under the [EuroBasket 2005 event page](https://www.fiba.basketball/en/history/208-fiba-eurobasket/2725/games),
  not in the qualifiers family. The importer keeps its Qualifying Round,
  Additional Qualifying Round Games, and Additional Qualifying Tournament
  phases, while excluding the 2005 championship and promotion/relegation
  phases. The documented reliable coverage begins in 1989; no verified
  qualifier game coverage is claimed before that point.
- EuroBasket Division B: 2007, 2009, and 2011.
- EuroBasket Pre-Qualifiers: historical seasons 1995, 1997, 1999, 2001, and
  2003, with the modern 2021 and 2025 editions used for reconciliation where
  GSA contains the same games.
- AfroBasket Pre-Qualifiers: FIBA's 2021 and 2025 preliminary competitions.
- Asia Cup finals: the official FIBA finals archive editions from 1960 through
  2025 that expose a year row. The duplicated archive label for 2003 is
  resolved by event identity: event 2651 is the 2002 Malaysian tournament and
  event 2675 is the 2003 championship. Event 2651 currently exposes no game
  results, so it remains an explicit FIBA archive gap rather than a fabricated
  game set.
- Asia Cup Qualifiers: the official 2021 and 2025 qualifier editions.
- Asia Cup Pre-Qualifiers: the official 2019 edition qualifying for Asia Cup
  2021 and the official 2023 edition qualifying for Asia Cup 2025. These are
  stored under their target championship season and retain the displayed FIBA
  source year in provenance.
- AmeriCup finals: the official FIBA finals archive from 1980 through 2025.
- AmeriCup Qualifiers: the official 2022 and 2025 qualifier editions, with the
  2022 source page labeled 2021 in FIBA's historical archive.
- AmeriCup Pre-Qualifiers: four official archive pages for the 2022 cycle,
  four for 2025, and the Caribbean/Central American 2029 events. The target
  cycle is stored separately from the source page year.
- Olympic finals: the official men's Olympic archive from 1948 onward. The
  application uses a single-year season label equal to the Olympic edition.
- Olympic Qualifying Tournaments: FIBA's official OQT archive. The Tokyo 2020
  cycle is stored as season `2020`, although the qualifying tournaments were
  played in 2021.
- Olympic Pre-Qualifying Tournaments: FIBA's official pre-OQT archive. The
  Paris 2024 cycle is stored as season `2024`, although the five pre-OQTs were
  played in 2023.
- World Cup finals: FIBA's official men's archive from 1950 through the
  completed 2023 edition. The future 2027 finals row is not cataloged until
  game cards exist.
- World Cup Qualifiers: FIBA's official 2019, 2023, and 2027 qualifier
  archive editions. The 2027 archive contains separate Asian, European,
  African, and Americas events, all stored under the shared `worldcup-2027`
  cycle while remaining one competition family.
- World Cup Pre-Qualifiers: FIBA's separate archive exposes the 2017 European
  pre-qualifiers for the 2019 cycle, the 2021 regional pre-qualifiers for the
  2023 cycle, and 2024-2025 regional pre-qualifiers for the 2027 cycle. The
  deployed ingest contains 30, 72, and 72 available FIBA rows respectively.
  The 2027 Caribbean archive page currently has no parseable game cards and is
  retained as an explicit source warning, not filled with synthetic data. These
  are a separate family, not earlier editions of the main World Cup Qualifiers
  route.
- Oceania Championship: FIBA's official men's senior archive editions from
  1971 through 2015. These are stored as independent `oceania-{year}` cycles;
  the archive has no separate Oceania qualifier or pre-qualifier family.
- Americas regional championships: Centrobasket, COCABA, South American, and
  Caribbean competitions are separate menÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢s senior FIBA families with their own
  edition cycles. They are not relabeled as AmeriCup games.
- Americas regional cutoffs and missing historical editions are tracked in
  [`americas-regional-ingestion.md`](americas-regional-ingestion.md). The audit
  includes official FIBA archive floors, parseable game-card floors, date
  fallbacks, and corroborating federation/Wikipedia/archive sources. External
  evidence is not converted into synthetic games or partial tournament rows.

Relevant FIBA history families:

- [EuroBasket Qualifiers](https://www.fiba.basketball/en/history/205-fiba-eurobasket-qualifiers)
- [EuroBasket Pre-Qualifiers](https://www.fiba.basketball/en/history/204-fiba-eurobasket-pre-qualifiers)
- [EuroBasket Division B](https://www.fiba.basketball/en/history/206-fiba-eurobasket-division-b)
- [AfroBasket Qualifiers](https://www.fiba.basketball/en/history/178-fiba-afrobasket-qualifiers)
- [FIBA Asia Cup](https://www.fiba.basketball/en/history/195-fiba-asia-cup)
- [FIBA Asia Cup Qualifiers](https://www.fiba.basketball/en/history/192-fiba-asia-cup-qualifiers)
- [FIBA AmeriCup](https://www.fiba.basketball/en/history/184-fiba-americup)
- [FIBA AmeriCup Qualifiers](https://www.fiba.basketball/en/history/183-fiba-americup-qualifiers)
- [FIBA AmeriCup Pre-Qualifiers](https://www.fiba.basketball/en/history/182-fiba-americup-pre-qualifiers)
- [FIBA Basketball World Cup](https://www.fiba.basketball/en/history/201-fiba-basketball-world-cup)
- [FIBA Basketball World Cup Qualifiers](https://www.fiba.basketball/en/history/200-fiba-basketball-world-cup-qualifiers)
- [FIBA Basketball World Cup Pre-Qualifiers](https://www.fiba.basketball/en/history/199-fiba-basketball-world-cup-pre-qualifiers)
- [FIBA Oceania Championship](https://www.fiba.basketball/en/history/216-fiba-oceania-championship)
- [Centrobasket Championship](https://www.fiba.basketball/en/history/122-centrobasket-championship)
- [CBC/COCABA archive](https://www.fiba.basketball/en/history/113-cbc-championship)
- [South American Championship](https://www.fiba.basketball/en/history/327-south-american-championship)
- [FIBA Men's Olympic Basketball Tournament](https://www.fiba.basketball/en/history/320-mens-olympic-basketball-tournament)
- [FIBA Olympic Qualifying Tournament](https://www.fiba.basketball/en/history/219-fiba-olympic-qualifying-tournament)
- [FIBA Olympic Pre-Qualifying Tournament](https://www.fiba.basketball/en/history/218-fiba-olympic-pre-qualifying-tournament)

### Wikipedia

Wikipedia is used for the EuroBasket 1991 and 1993 qualification match lists,
because those historical FIBA entries do not provide complete usable game
archives. It is a documented exception, not a general fallback for FIBA or GSA.

For Olympic ingestion, Wikipedia is a reconciliation and coverage-validation
source. The canonical game rows come from FIBA or GSA; Wikipedia is not used to
manufacture a second copy of a game when an official source already exposes it.
The validation pages are [Basketball at the Summer Olympics](https://en.wikipedia.org/wiki/Basketball_at_the_Summer_Olympics),
[the 2024 Olympic qualifying tournaments](https://en.wikipedia.org/wiki/2024_FIBA_Men%27s_Olympic_Qualifying_Tournaments),
and [the 2024 pre-qualifying tournaments](https://en.wikipedia.org/wiki/2024_FIBA_Men%27s_Pre-Qualifying_Olympic_Qualifying_Tournaments).

Sources: [FIBA EuroBasket 1991 qualification](https://en.wikipedia.org/wiki/FIBA_EuroBasket_1991_qualification)
and [FIBA EuroBasket 1993 qualification](https://en.wikipedia.org/wiki/FIBA_EuroBasket_1993_qualification).

### NBA and other domestic sources

The international pipeline does not replace the existing domestic sources:

- API-Sports is used for supported modern domestic leagues and current refreshes.
- Basketball-Reference uses authorized local archives for historical imports;
  network fetching is disabled by default.
- FiveThirtyEight supplies the pinned, checksum-verified historical NBA archive
  through the 2007-2008 season and does not make runtime network requests.

### ACB historical archive

The historical ACB catalog uses the 'acb-dbasket' provider for seasons
1983-1984 through 2007-2008. It reads DBasket's season and round pages,
which contain the date, home team, away team, score, regular-season round,
and playoff phase. The [DBasket ACB historical index](https://dbasket.net/seasons/acb)
and its season/round pages are the ingestion endpoints. DBasket states that
its data is primarily collected from ACB and FEB sources and corrected against
historical archives. API-Sports
remains the provider for ACB from 2008-2009 onward. DBasket requests are
disabled by default and must be explicitly enabled with
'Dbasket:NetworkAccessEnabled'.

Spanish club-basketball ingestion begins with the 1956-1957 Liga Nacional,
which is the first continuous national league season. Pre-1957 Spanish club
basketball consisted primarily of regional championships and the national
Campeonato de EspaÃƒÆ’Ã‚Â±a/Copa competition; those records are not ingested as
league context unless a reliable national in-season source is added.

The official ACB Liga Nacional provider covers the pre-ACB seasons currently
loaded in the catalog. Historical ties are valid source results for this
competition: contemporary season tables include a separate draws column, and
the 1982-1983 Zaragoza season record explicitly lists Areslux Granollers-
Zaragoza as 78-78. The provider therefore accepts equal home and away scores
as completed games; they are not treated as parser failures or discarded.
Source cross-checks: [1982-1983 season table](https://es.wikipedia.org/wiki/Primera_Divisi%C3%B3n_de_baloncesto_1982-83)
and [C.B. Zaragoza 1982-1983 results archive](https://www.lacasadelbaloncesto.es/docs/resultados/Resultado20.pdf).

### ACB historical cups

The historical Spanish Cup catalog uses the `acb-official-tournaments`
provider for Copa del GeneralÃƒÆ’Ã‚Â­simo seasons from the Liga Nacional boundary
onward, and for Copa del Rey seasons 1983-1984 through 2007-2008. Cup games
before 1956-1957 are excluded because there is no continuous national league
context for them. The same
provider covers the Supercopa editions that were actually played in that
range: 1984-1985 through 1987-1988 and 2004-2005 through 2007-2008. It uses
the official [ACB Copa del Rey archive](https://acb.com/es/copa-del-rey) and
[Supercopa archive](https://acb.com/es/supercopa), follows the historical team
pages for each bracket, and verifies each candidate against its official ACB
Live match page before importing it.

See [`nba-source-policy.md`](nba-source-policy.md) and
[`nba-refresh-operations.md`](nba-refresh-operations.md) for NBA-specific rules.
See [`turkish-ingestion.md`](turkish-ingestion.md) for the TBLStat Turkish
league source, historical Turkish Cup final coverage, and Presidential Cup
limitations.
See [`serbian-ingestion.md`](serbian-ingestion.md) for the reviewed
Yugoslav/Serbia-area top-flight range, club identity normalization, and the
remaining pre-2000 source gap.

### ABA League and ABA Super Cup

The historical ABA League backfill uses the official [ABA League calendar](https://www.aba-liga.com/)
for the inaugural seasons 2001-2002 through 2007-2008. The calendar exposes
regular-season and postseason panels together, so the imported counts include
playoff and Final Four games:

| Season | Games |
| --- | ---: |
| 2001-2002 | 135 |
| 2002-2003 | 135 |
| 2003-2004 | 185 |
| 2004-2005 | 247 |
| 2005-2006 | 189 |
| 2006-2007 | 189 |
| 2007-2008 | 196 |

The official match ID is stored with the source season ID (`aba-{seasonId}-{matchId}`)
because match IDs restart at one in each season. Omitting the season component
would overwrite games from earlier seasons during a multi-season import.

The ABA Super Cup is a separate competition and is not part of those historical
league totals. The catalog currently imports the editions available from
API-Sports: 2017-2018, 2018-2019, 2019-2020, and 2023-2024. The four editions
currently contain 7, 7, 7, and 12 games respectively, for 33 Super Cup games
in total. The Super Cup did not exist during the 2001-2008 historical league
range, and its games therefore remain separately identifiable as
`International: ABA Supercup` with competition type `international_cup`.

For both ABA competitions, verify coverage status, game count, warnings,
identity findings, and service health after production ingestion. Do not combine
Super Cup games with the historical ABA League season counts when checking the
official calendar totals.

See [`italian-ingestion.md`](italian-ingestion.md) for the ranked Italian Serie A
sources, official LBA traversal, coverage gaps, and newest-first runbook.
See [`french-ingestion.md`](french-ingestion.md) for the ranked French league and
cup sources, the explicit historical gap, and the no-cup-only-season rule.
See [`greek-ingestion.md`](greek-ingestion.md) for the ranked Greek league and
Cup sources, the 1996-1997 cutoff, and the runtime league-overlap guard.
See [`german-ingestion.md`](german-ingestion.md) for the German BBL and German
Cup source boundaries, the 1975-1976 historical cutoff, repair policy, and
finals-only historical Cup limitation.
See [`european-historical-ingestion-coverage.md`](european-historical-ingestion-coverage.md)
for the consolidated Czech NBL, Polish PLK, Lithuanian LKL and Cup, Baltic
League, and European second-tier historical coverage matrix and verified
cutoffs.
See [`israel-ingestion.md`](israel-ingestion.md) for the official Israeli
season/stage selector mapping, 1953-1954 cutoff, and source gaps.

## Competition separation and reconciliation

Competition identity is determined by the configured family and provider alias,
not just by the text of a source page. The following distinctions are
intentional:

- championship games remain separate from their qualifiers;
- pre-qualifiers remain separate from main qualifiers;
- EuroBasket, AfroBasket, Asia Cup, and AmeriCup stages share a tournament
  cycle for filtering but never share a competition identity;
- World Cup qualifiers remain separate from continental qualifiers;
- Olympic qualifiers remain separate from the Olympics tournament;
- EuroBasket Division B remains separate from Division A.

When two providers contain the same match, the canonical record is selected by
the configured source policy. FIBA is canonical for official AfroBasket, Asia
Cup, and AmeriCup records; GSA-only or source-disputed rows remain available
for review. For the modern EuroBasket Pre-Qualifiers, GSA remains canonical
where it has the more complete stage and source metadata. Matching rows are
removed only after an explicit one-to-one reconciliation, and broad GSA
qualification pages are split into the correct pre-qualifier competition
before duplicate checks run. The complete stage/count matrix is documented in
[`fiba-national-team-tournaments.md`](fiba-national-team-tournaments.md).

Reconciliation must match at least the target season, stage, home and away
teams, and final scores. Date matching may use a documented bounded tolerance
for historical archive drift, but the candidate set must remain one-to-one and
the migration must abort before deletion if it becomes ambiguous. Manual
results and manually corrected dates are preserved and must not be overwritten
by routine re-ingestion.

## Scores, dates, and ELO eligibility

- GSA dates and times are imported as provided.
- FIBA historical pages may provide a date but not a time; the provider records
  the documented fallback and emits a warning.
- A source fixture without a final score is retained as unresolved when it is
  useful for later inspection, but it is not ELO-eligible.
- A future/TBD fixture can be removed from the current completed backfill once
  it is confirmed not to have been played.
- A manually supplied result is stored as a manual override and must survive
  later provider refreshes.
- Forfeits and cancellations are represented according to the reviewed result
  and status; they are not silently converted into ordinary wins or losses.
- Friendlies and non-official fixtures are excluded from the national-team ELO
  pool even if a provider exposes them.

The national-team competitions share the national-team ELO pool, but their
competition metadata remains available for filtering, audits, and explanations.
After a bulk correction or reconciliation, queue the affected ELO rebuilds for
the relevant rulesets.

## Backfill operations

Use the internal admin backfill page for normal operations. A direct API job has
the following shape:

```json
{
  "provider": "global-sports-archive",
  "country": "Europe",
  "leagueName": "FIBA EuroBasket Pre-Qualifiers",
  "season": "2029",
  "dryRun": false,
  "maxRequests": 0
}
```

Operational rules:

1. Take a PostgreSQL backup before destructive cleanup or reconciliation.
2. Run one provider/competition/season at a time when validating a new source
   mapping.
3. Use unlimited requests for a real GSA archive traversal; use a small limit
   only for diagnostics.
4. Review warnings, unresolved fixtures, identity blockers, and source URLs.
5. Do not delete existing manually corrected games during a refresh.
6. Rebuild the affected ELO pool after data changes.
7. Verify the coverage row, game count, warning status, and service health.

For production, the application runs on the VPS under `/opt/basket-elo` with
`basket-elo-api.service`, `basket-elo-worker.service`, and
`basket-elo-web.service`. Deploy through `deploy/vps/deploy.ps1`, then verify
`http://127.0.0.1:5100/health` and all three systemd services.

### VPS Asia Cup verification snapshot

On 2026-08-08, after deploying the FIBA catalog changes and verifying the
pre-cleanup PostgreSQL backup at
`/var/backups/basket-elo/basket-elo-20260808-100258-pre-asia-dedup.dump`, the
Asia-only FIBA ingest completed 36 jobs: 16 completed without warnings and 20
completed with source/archive warnings. The 2002 Malaysian event remains a
documented one-job archive gap with no game-level records; no games were
synthesized from standings.

The backup-backed reconciliation migration removed 427 exact one-to-one GSA
duplicates (same target season, date, teams, and final score). It left 1,159
FIBA finals rows, 142 FIBA qualifier rows, 65 FIBA pre-qualifier rows, 1,258
GSA finals rows, and 128 GSA qualification rows. Manual-result rows were not
overwritten, and the post-cleanup audit found zero duplicate source keys and
zero remaining exact cross-source duplicates in the Asia Cup scope. API,
worker, and web health checks were healthy after deployment.

### VPS World Cup verification snapshot

On 2026-08-08, the verified pre-ingest backup was
`/var/backups/basket-elo/basket-elo-20260808-200000-pre-world-cup.dump` and the
verified pre-reconciliation backup was
`/var/backups/basket-elo/basket-elo-20260808-203000-pre-world-cup-reconcile.dump`.
The FIBA ingest completed 19 finals jobs and 3 qualifier jobs without warnings.
It produced 1,213 FIBA finals rows and 1,236 FIBA qualifier rows. The
backup-backed reconciliation removed only one-to-one GSA duplicates, leaving
97 GSA finals rows and 96 GSA qualifier rows as source-only coverage.

The post-reconciliation audit found 0 duplicate source keys, 0 same-stage
identity duplicates, 0 remaining exact cross-source candidates, and 0 missing
World Cup tournament-cycle links across 2,642 rows. The repeat finals and
qualifier runs queued 0 jobs. API, worker, and web health checks were healthy.

## Known limitations

- GSA is a public site but still has practical request, timeout, and pagination
  constraints; the provider therefore reports every traversal interruption in
  the job summary.
- Some historical FIBA pages expose an edition but not a complete individual
  game list.
- FIBA's historical year labels can represent qualifying events for a later
  championship and require explicit season mapping.
- Reliable EuroBasket qualifier game coverage is documented from 1989 onward;
  earlier years remain an evidence gap.
- Source availability and future fixture status can change. A completed row
  describes the data available when that backfill ran, not a guarantee that the
  source will remain unchanged.
