# Italian league and cup historical ingestion

This runbook covers issue #124: game-level Italian men's top-flight ingestion
backward from 2007-2008. Source ranking prioritizes the number of target seasons
with individual games, then access and parsing difficulty, provenance quality,
and identity stability. The league and cup remain separate canonical
competitions with separate source boundaries.

## Ranked sources

| Rank | Source | Verified target coverage | Access and ingestion assessment | Intended use |
| --- | --- | --- | --- | --- |
| 1 | [Official Lega Basket Serie A](https://www.legabasket.it/competizioni/1/serie-a) | 34 consecutive regular seasons, 1974-1975 through 2007-2008; separate playoff calendars are exposed for 1994-1995 through 2007-2008 | Public first-party JSON used by the official site; stable game IDs, dates, scores, phases, and club IDs; easiest and most authoritative | Primary ingestion source |
| 2 | [Basketball-Reference](https://www.basketball-reference.com/international/italy-basket-serie-a/) | 10 target seasons, 1998-1999 through 2007-2008 | Structured schedules, but live access is restricted and this repository requires an authorized local archive | Reconciliation only |
| 3 | [Basketball Database](https://basketball-database.com/csgc/leagues/0/761) | 7 target seasons, 2001-2002 through 2007-2008 | Game schedules are visible, but commercial access/terms need review before automation | Manual reconciliation candidate |
| 4 | [Flashscore](https://www.flashscore.com/basketball/italy/lega-a-2006-2007/results/) | 2 target seasons confirmed, 2006-2007 and 2007-2008 | JavaScript/anti-bot behavior and limited target coverage make reliable ingestion difficult | Spot checks only |
| 5 | [Official LBA news/calendar pages](https://www.legabasket.it/news/61876/il-calendario-della-serie-a-tim-2006-2007) | Scattered seasons | First-party but not a consistent game-results archive; some pages link PDFs | Calendar/date reconciliation |

The official LBA catalog has no game-level coverage before 1974-1975. Seasons
1948-1949 through 1973-1974 remain a source-discovery gap and must not be marked
complete from standings alone. Earlier playoff formats not exposed as separate
official calendars also remain explicit coverage findings.

## Official LBA traversal

The `lba-official` provider uses the public JSON requests made by the official
competition page:

1. Read the Serie A championship catalog and select the requested start year's
   `RS` and `PO` championship IDs.
2. Read that year's teams and use `club_id` as the stable cross-season source
   identity. Sponsor/team names remain observed aliases.
3. Read each championship calendar's matchday filters.
4. Fetch every matchday by its `event_serial` and retain the official match ID,
   date/time, teams, final score, phase, round, URL, and raw-record hash.
5. Keep regular season and playoffs as phases of the canonical competition
   `Italy: Lega Basket Serie A`.

The catalog maps `lba-official` through 2007-2008 and API-Sports from 2008-2009
onward to the same canonical competition.

Live boundary probes confirmed 332 finished games for 2007-2008 (306 regular
season and 26 playoffs, zero parser warnings) and 182 finished regular-season
games for 1974-1975. The earliest probe reports the missing separate playoff
calendar as an explicit warning rather than silently claiming playoff coverage.

## Commands

Inspect one season without database writes:

```powershell
dotnet run --project src/BasketElo.Tools -- italy-serie-a-dry-run `
  --season 2007-2008 --max-requests 0 --interval-ms 100
```

Queue and process the official archive newest-first:

```powershell
dotnet run --project src/BasketElo.Tools -- italy-serie-a-ingest `
  --start 2007-2008 --end 1974-1975 --max-requests 0 --interval-ms 100
```

The ingest command migrates the configured Postgres database, skips completed
or active seasons, and refuses to run while an unrelated backfill is pending.
Start with 2007-2008 alone, review game/phase counts, warnings, aliases, and
identity findings, then extend the end season backward.

## Italian Cup sources

Cup source ranking prioritizes first-party data, complete played-edition
coverage, and machine-readable access. The official LBA catalog is
authoritative and exposes complete calendars from 2008-2009 onward; its older
edition records have empty calendar payloads.

| Rank | Source | Verified coverage | Access and ingestion assessment | Intended use |
| --- | --- | --- | --- | --- |
| 1 | [Official LBA Coppa Italia](https://www.legabasket.it/competizioni/5/coppa-italia) | Complete machine-readable games from 2008-2009 through 2025-2026 | First-party JSON with official game and club IDs | Primary modern ingestion source |
| 2 | [Italian Wikipedia edition archive](https://it.wikipedia.org/wiki/Coppa_Italia_di_pallacanestro_maschile) | All 25 played editions from 1983-1984 through 2007-2008 | One MediaWiki API request per edition, revision IDs, game-level brackets, two-leg ties, result matrices, and listed results | Historical ingestion where Serie A context exists |
| 3 | Archived LBA tournament PDFs linked by individual edition pages | Scattered historical editions | First-party documents, but links are inconsistent and often require web archives | Manual score/date reconciliation |

The `wikipedia-italian-cup` provider uses the MediaWiki revisions API and parses
the four historical result shapes used by the edition pages: knockout brackets,
two-leg templates, two-leg result tables, and round-robin matrices/lists. Team
article targets are retained as stable source identities while sponsor names are
kept as observed aliases. Verified sponsor-only cells are normalized to those
same club identities, while same-city rivals and legal successor clubs remain
separate. Administrative 2-0 results are stored but explicitly excluded from
ELO.

Live probes from 1983-1984 through 2007-2008 produced 1,000 published games
across 25 editions, with no zero-game editions. These totals are larger than
seven games per season because older formats included preliminary rounds,
home-and-away groups, and two-leg knockout ties. The source often publishes
only stage-level dates for older rounds; deterministic date fallbacks are
retained as warnings instead of being presented as exact dates. The official
LBA source adds 18 seven-game Final Eight editions from 2008-2009 through
2025-2026. API-Sports is not used for this competition because its later Cup
season keys do not consistently align with the corresponding league season.

The 1967-1968 through 1973-1974 editions are intentionally excluded even
though their results are published: the loaded Italian national-league context
begins in 1974-1975. Every cataloged Italian Cup season must have a matching
Italian first-division season, following the same policy as Spain.

Inspect one historical cup edition without database writes:

```powershell
dotnet run --project src/BasketElo.Tools -- italy-cup-dry-run `
  --season 2007-2008 --max-requests 0 --interval-ms 1000
```

Queue every retained edition newest-first, including modern official LBA data:

```powershell
dotnet run --project src/BasketElo.Tools -- italy-cup-ingest `
  --start 2025-2026 --end 1983-1984 --max-requests 0 --interval-ms 1000
```

The command skips completed or active editions and refuses to run while an
unrelated backfill is pending.
