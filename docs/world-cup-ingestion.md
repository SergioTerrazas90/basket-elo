# FIBA World Cup ingestion

This runbook covers the men's senior FIBA Basketball World Cup finals and
World Cup qualification routes. Women's, youth, 3x3, and unrelated Olympic
qualification competitions are excluded.

## Catalog and cycle semantics

| Competition | Cycle key | FIBA archive editions |
| --- | --- | --- |
| FIBA World Cup | `worldcup-{year}` | Played finals 1950, 1954, 1959, 1963, 1967, 1970, 1974, 1978, 1982, 1986, 1990, 1994, 1998, 2002, 2006, 2010, 2014, 2019, 2023 |
| FIBA World Cup Qualifiers | `worldcup-{year}` | 2019, 2023, 2027 |
| Historical World Cup qualification routes | `worldcup-{year}` | Existing Olympic and continental qualifying tournaments linked for 1950â€“2014 where source rows are available |
| FIBA World Cup Pre-Qualifiers* | `worldcup-{year}` | 2017 (2019 cycle), 2021 (2023 cycle), 2024â€“2025 (2027 cycle) |

The official archive also displays a future 2027 finals row, but it has no
played game cards at the 2026-08-08 audit cutoff and is not cataloged. The
qualifier archive has separate regional event rows; these remain one FIBA
qualifier competition and share the `worldcup-2027` cycle.

* Pre-qualifiers are a distinct official FIBA family. The deployed pass ingests
  the live game-card editions for the 2019, 2023, and 2027 cycles; they remain a
  separate competition while sharing the target World Cup cycle key.

## Source and reconciliation policy

FIBA is canonical for official game cards. The existing GSA competition
`FIBA Basketball World Cup` is retained as a source-only comparison for finals,
and `FIBA WC Qualification` is retained as the source-only comparison for
qualifiers. The reconciliation migration removes only one-to-one GSA matches
on target season, bounded fixture date, normalized home/away teams, and final
scores. Manual-result rows are preserved; GSA-only games remain.

The official sources are the [FIBA Basketball World Cup archive](https://www.fiba.basketball/en/history/201-fiba-basketball-world-cup),
the [FIBA Basketball World Cup Qualifiers archive](https://www.fiba.basketball/en/history/200-fiba-basketball-world-cup-qualifiers),
and the separate [World Cup Pre-Qualifiers archive](https://www.fiba.basketball/en/history/199-fiba-basketball-world-cup-pre-qualifiers).
For historical cycles, the original continental/Olympic game row remains
canonical and receives a secondary World Cup qualifier-cycle link; no second
game row is created.

## Historical qualification audit

There were World Cup qualification editions and qualification routes before
2019, but they were composite tournament-based systems rather than one global
home-and-away FIBA World Cup Qualifiers competition. Wikipedia's qualification
history identifies the following model:

- 1950â€“1963: qualification used combinations of the Olympics, regional
  championships, and invitations.
- From 1967 through 2014: continental championships generally doubled as the
  World Championship/World Cup qualifying tournaments, with hosts, Olympic
  champions, and later wild cards also affecting the field.
- The 2006, 2010, and 2014 cycles explicitly document Africa, Americas, Asia,
  Europe, and Oceania championships as the qualifying tournaments. In
  particular, the [2010 qualification edition](https://en.wikipedia.org/wiki/2010_FIBA_World_Championship_qualification)
  lists the five continental championships, the 2008 Olympics/host route, and
  four wild-card places; it records 106 participating countries.
- 2019 was the first home-and-away window-based global qualifier system. The
  2017 European pre-qualifiers were a preliminary stage for that new system,
  not an earlier edition of the main World Cup Qualifiers family.

Therefore the pre-2019 qualification history is represented by the relevant
Olympics and continental tournament families and is also exposed through the
linked `worldcup-{year}` qualifier cycle. The standalone global window-based
World Cup Qualifiers family begins at 2019. Historical games are linked to the
World Cup cycle without being copied or removed from their original competition.

## Remaining gaps

- The separate [World Cup Pre-Qualifiers archive](https://www.fiba.basketball/en/history/199-fiba-basketball-world-cup-pre-qualifiers) is ingested for the 2019, 2023, and 2027 cycles. The 2017, 2021, and 2024-2025 source editions feed those target cycles.
- The 2027 archive includes a Caribbean history entry, but the official [Caribbean page](https://www.fiba.basketball/en/history/199-fiba-basketball-world-cup-pre-qualifiers/208746) currently returns `Content Unavailable` and exposes no parseable game cards. The ingest includes the four available 2027 regional event pages and records one provider warning rather than fabricating Caribbean rows.

- For 1950â€“1963, qualification also used regional events and invitations. The
  current links cover the available Olympic route games, but do not claim a
  complete reconstruction of every regional or invitation decision.
- The 1967â€“1978 links reflect the qualifying competitions available in the
  current FIBA/GSA catalog; those cycles did not use the modern five-zone,
  window-based archive structure.

The modern standalone World Cup Qualifiers coverage for 2019, 2023, and 2027
is complete at the current audit cutoff. The separate pre-qualifier family has
174 ingested FIBA rows: 30 for 2019, 72 for 2023, and 72 available for 2027.

## Verification

For each FIBA family, run a dry-run before production ingestion:

```text
BasketElo.Tools fiba-dry-run --country World --league "FIBA World Cup" --season 2023 --max-requests 0
BasketElo.Tools fiba-dry-run --country World --league "FIBA World Cup Qualifiers" --season 2027 --max-requests 0
BasketElo.Tools fiba-dry-run --country World --league "FIBA World Cup Pre-Qualifiers" --season 2027 --max-requests 0
```

Audit source keys, same-stage identities, cross-source duplicates, and cycle
links after ingestion. Historical qualification links must be unique by game
and World Cup cycle. The API exposes them through both the `worldcup-{year}`
tournament-cycle filter and the `FIBA Basketball World Cup Qualifiers` league
alias, while retaining the original competition name in each row. Repeat
ingestion must queue zero jobs or update the same FIBA source rows.

## VPS audit

Audit cutoff: 2026-08-08. The verified pre-ingest backup was
`/var/backups/basket-elo/basket-elo-20260808-200000-pre-world-cup.dump`; the
verified pre-reconciliation backup was
`/var/backups/basket-elo/basket-elo-20260808-203000-pre-world-cup-reconcile.dump`.

The FIBA ingest completed 19 finals editions and 3 qualifier cycles without
warnings, producing 1,213 finals rows and 1,236 qualifier rows. Reconciliation
left 97 GSA finals rows and 96 GSA qualifier rows as source-only coverage. The
full World Cup scope contains 2,642 primary rows across 20 cycles. Historical
qualification links add 3,102 deduplicated canonical games across 17 cycles
from 1950 through 2014; the 2010 cycle contains 288 unique route games,
including the five continental routes and the 2008 Olympic route. The audit
found 0 duplicate source keys, 0 duplicate historical links, 0 same-stage
identity duplicates, 0 remaining exact cross-source matches in the primary
World Cup scope, and 0 missing primary cycle links. Repeated finals and
qualifier ingests queued 0 jobs.

The 2027 qualifier refresh after the FIBA status fix contains 420 rows under
`worldcup-2027`: 240 finished and 180 scheduled. The August 27–September 1
window is now represented as scheduled games with null scores until results
arrive. The refresh found 0 duplicate source keys and 0 future `0–0` games
marked final. Its backup is
`/var/backups/basket-elo/basket-elo-20260808-pre-fiba-status-fix.dump`.
