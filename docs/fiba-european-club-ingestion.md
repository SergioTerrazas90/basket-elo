# FIBA European Champions Cup historical ingestion

The `fiba` provider now covers the men's European Cup for Champion Clubs,
the historical predecessor lineage of the modern EuroLeague, from the
1958-1959 edition through the 1999-2000 edition. The separate 2000-2001 FIBA
SuproLeague edition is also cataloged from the official FIBA archive. The
2000-2001 through 2007-2008 ULEB EuroLeague bridge is cataloged separately from
the modern API-Sports segment, which begins at 2008-2009.

## Source and identity policy

The primary source is the official [FIBA Men’s European Club Competitions –
Tier 1 archive](https://www.fiba.basketball/en/history/112-fiba-mens-european-club-competitions-tier-1).
The archive indexes editions by their ending year, while BasketElo stores the
two-year season label. For example, application season `1958-1959` resolves
the FIBA `1959` edition and `1999-2000` resolves the FIBA `2000` edition.

FIBA game IDs and source team codes are retained as the stable source
identifiers. Historical aliases and country changes are therefore not
collapsed by display-name guesses; the imported source IDs feed the existing
identity health checks for review.

The current FIBA archive also serializes complete historic game objects in the
page payload. For older editions, `teamA.code` and `teamB.code` can be `null`;
the parser uses the stable FIBA team ID (`FIBA:<teamId>`) and the published
short name instead. Visible `TBD`/`TBC` cards are not synthesized, but they do
not imply missing games when the embedded payload supplies the same records.

For genuinely sparse early editions (fewer than 50 official game records), the provider compares alternate historical archives.
Todor Krastev's [1976-77](http://todor66.com/basketball/Eurocups/Men_CC_1977.html)
through [1990-91](http://todor66.com/basketball/Eurocups/Men_CC_1991.html)
pages publish dated score rows for the group and knockout stages. The parser
handles their compact date notation and reverses return-leg home/away sides.
The corresponding Spanish Wikipedia articles (for example,
[1958-59](https://es.wikipedia.org/wiki/Copa_de_Europa_de_baloncesto_1958-59)
and [1984-85](https://es.wikipedia.org/wiki/Copa_de_Europa_de_baloncesto_1984-85))
are also compared, and the candidate with more game-level records is used.
From 1996 onward, English Wikipedia's score-matrix templates are used to fill
unresolved group stages while official FIBA knockout rows are retained.

## Coverage and gaps

The live official FIBA payload was verified for the 1984-1985 through
1995-1996 seasons with the following game counts:

| Season | Official game records |
| --- | ---: |
| 1984-1985 | 69 |
| 1985-1986 | 68 |
| 1986-1987 | 71 |
| 1987-1988 | 90 |
| 1988-1989 | 92 |
| 1989-1990 | 98 |
| 1990-1991 | 98 |
| 1991-1992 | 160 |
| 1992-1993 | 164 |
| 1993-1994 | 175 |
| 1994-1995 | 179 |
| 1995-1996 | 179 |

These counts include preliminary rounds, group/semi-final rounds, playoffs,
and final-stage games exposed by FIBA's embedded payload.

The historical predecessor audit covers all 42 seasons from 1958-1959 through
1999-2000, with 4,060 FIBA-source game rows and zero duplicate source IDs.
The only seasons currently retained entirely from the alternate historical
source are 1958-1959 (40 games) and 1959-1960 (42); 1962-1963 now contains
51 reconciled game records after adding its single-leg tie, tiebreak, and
three-game final. Every other predecessor season is official-FIBA-only after
stale-row cleanup.

The 2000-2001 SuproLeague is a separate competition from the inaugural ULEB
EuroLeague split. Its official FIBA page exposes 213 finished game records,
covering the 180-game qualification round, 20-game eighth-final play-offs,
9-game quarter-final play-offs, and 4-game Final Four. The ULEB EuroLeague
bridge uses the euroleagueR match-results release for 2000-2001 through
2007-2008. Its stable season/game codes are deduplicated; the archive includes
the regular season and knockout phases. The eight bridge seasons contain 1,794
finished games: 158, 275, 220, 220, 229, 231, 230, and 231 respectively.

The provider imports game-level cards with a stable FIBA game ID, dated game,
known home and away source codes, score, phase, round, source URL, edition
key, fetched timestamp, parser version, and source revision hash.

Some older FIBA edition pages still expose bracket placeholders or
standings-level rows. These are reported as warnings; no scores are
synthesized. Alternate archives can also omit exact dates, in which case the
parser uses deterministic edition-order dates and records that limitation in
the job warning. When an alternate source replaces a sparse FIBA season,
stale FIBA rows for that season are removed before the replacement is
inserted. Likewise, a rich official Champions Cup payload removes stale rows
whose source game IDs are not present in the incoming set. Both paths make
refreshes idempotent rather than duplicating the same scores. A
completed-with-warnings backfill job is therefore an explicit coverage
report; visible placeholder warnings alone do not mean that embedded game
records are missing.
The EuroLeague
Basketball historical matchups PDF is retained as a validation/reconciliation
source only; it is not imported automatically without an authorized
game-level archive.

## Operations

Read-only validation for one edition:

```text
dotnet run --project src/BasketElo.Tools -- fiba-dry-run \
  --country Europe \
  --league "FIBA European Champions Cup" \
  --season 1999-2000 \
  --max-requests 4
```

Queue the full historical range into the configured Postgres database:

```text
dotnet run --project src/BasketElo.Tools -- fiba-ingest \
  --max-jobs 0 \
  --max-requests 0
```

Queue the ULEB bridge separately:

```text
dotnet run --project src/BasketElo.Tools -- euroleague-historical-ingest \
  --max-jobs 8 \
  --max-requests 1
```

That command reads the public euroleagueR match-results release and stores the
bridge under `Euroleague`; the one-season FIBA SuproLeague remains under its
own `FIBA SuproLeague` competition.

Run this source after confirming the configured database backup and before
starting another provider in the same ELO pool. The backfill processor keeps
the source URL, edition key, parser version, phase, round, and warnings in the
job/game records, then runs identity checks and queues the normal ELO rebuild
when no blockers remain.
