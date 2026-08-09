# FIBA national-team tournament coverage

This is the cross-tournament reference for the primary continental national-team
families currently represented in the ingestion catalog, plus the historical
Americas regional families. It complements the detailed
qualification-history guide in [`international-qualification-systems.md`](international-qualification-systems.md)
and the operational rules in [`ingestion.md`](ingestion.md).

## Common rules

Each family uses a shared tournament-cycle key across finals, qualifiers, and
pre-qualifiers:

| Family | Cycle key | Finals competition | Qualifier competition | Pre-qualifier competition |
| --- | --- | --- | --- | --- |
| EuroBasket | `eurobasket-{year}` | `EuroBasket` | `EuroBasket Qualifiers` | `FIBA EuroBasket Pre-Qualifiers` |
| AfroBasket | `afrobasket-{year}` | `FIBA AfroBasket` | `FIBA AfroBasket Qualifiers` | `FIBA AfroBasket Pre-Qualifiers` |
| Asia Cup | `asiacup-{year}` | `FIBA Asia Cup` | `FIBA Asia Cup Qualifiers` / `FIBA Asia Cup Qualification` | `FIBA Asia Cup Pre-Qualifiers` |
| AmeriCup | `americup-{year}` | `FIBA AmeriCup` | `FIBA AmeriCup Qualifiers` | `FIBA AmeriCup Pre-Qualifiers` |
| World Cup | `worldcup-{year}` | `FIBA Basketball World Cup` | `FIBA Basketball World Cup Qualifiers` | `FIBA Basketball World Cup Pre-Qualifiers`* |
| Centrobasket | `centrobasket-{year}` | `Centrobasket Championship` | â€” | â€” |
| COCABA | `cocaba-{year}` | `COCABA Championship` | â€” | â€” |
| South American | `south-american-{year}` | `South American Championship` | â€” | â€” |
| Caribbean | `caribbean-{year}` | `Caribbean Basketball Championship` | â€” | â€” |
| Oceania Championship | `oceania-{year}` | `FIBA Oceania Championship` | â€” | â€” |
| Olympics | `olympics-{year}` | `Summer Olympics` | `Olympics Qualification` | `Olympics Pre-Qualification` |

The cycle year is the championship being qualified for. It is not always the
calendar year shown on the source page. Every stage remains a separate
competition, while the cycle filter groups the stages intentionally.

Olympic-specific source mappings and reconciliation rules are documented in
[`olympics-ingestion.md`](olympics-ingestion.md). Although this table includes
the family for operational consistency, the Olympics are a World-level
tournament family rather than a continental championship.

FIBA is canonical when an official FIBA game record exists for the relevant
family and stage. GSA-only rows remain available when the sources disagree or
when FIBA does not expose the game. Wikipedia is used only for documented
historical gaps and validation, never as an unreviewed general fallback.

Before destructive reconciliation:

1. create and verify a PostgreSQL backup;
2. require a one-to-one candidate match;
3. preserve manual result overrides;
4. delete only the verified duplicate source rows; and
5. audit source keys, same-stage identities, and cross-source identities.

## Verification snapshot â€” 2026-08-08

The following are current database row counts after the deployed ingestion and
reconciliation work. They are coverage snapshots, not claims that every
historical source edition is complete.

| Family | Finals | Qualifiers | Pre-qualifiers | Cycle range |
| --- | ---: | ---: | ---: | --- |
| EuroBasket | 2,242 | 1,691 | 435 | 1935â€“2029 |
| AfroBasket | 1,408 | 460 | 34 | 1962â€“2025 |
| Asia Cup | 2,417 | 142 + 128 legacy qualification | 65 | 1960â€“2025 |
| AmeriCup | 695 | 89 | 96 | 1980â€“2029 |
| World Cup | 1,213 | 1,236 | 174 | 1950â€“2027 |

The AmeriCup counts are now FIBA-canonical: 695 finals rows, 89 qualifier
rows, and 96 pre-qualifier rows. The GSA comparison rows were reconciled and
removed only where the normalized team/date candidate was one-to-one; no GSA
AmeriCup rows remain after the deployed passes.

The Oceania archive currently contains 58 FIBA-canonical game rows across all
22 official edition labels. The ingest completed 9 editions without warnings
and 13 with historical date fallbacks where FIBA game cards did not expose a
match date. No GSA Oceania source is configured or present.

The historical Americas regional pass added 910 FIBA-canonical rows: 359
Centrobasket, 77 COCABA, 322 South American, and 152 Caribbean. FIBA archive
pages without parseable game cards remain explicit gaps: Centrobasket
1965â€“1985, 2002; COCABA 2003 and 2007; South American 1987, 1993, 2011, and
2015. The 1932â€“1997 South American rows with missing card dates use the
edition-start-date fallback and retain warnings.

World Cup finals and qualifiers are now FIBA-canonical where the official
FIBA archive exposes game cards. The deployed scope has 1,213 FIBA finals rows
and 1,236 FIBA qualifier rows across played finals from 1950 through 2023 and
qualifier cycles 2019, 2023, and 2027. Historical World Cup qualification
routes before 2019 are linked to the corresponding `worldcup-{year}` cycle
without duplicating their canonical continental/Olympic game rows. A further 97 GSA finals rows and 96 GSA
qualifier rows remain source-only because FIBA did not expose a one-to-one
match. The World Cup cycle is separate from Olympic Qualification, Olympic
Pre-Qualification, and all continental qualifier cycles. See
[`world-cup-ingestion.md`](world-cup-ingestion.md) for the source cutoff and
VPS audit.

\* FIBA also exposes a separate [World Cup Pre-Qualifiers archive](https://www.fiba.basketball/en/history/199-fiba-basketball-world-cup-pre-qualifiers)
with 2017, 2021, 2024, and 2025 source editions. The deployed family contains
30 rows for the 2019 cycle, 72 for 2023, and 72 available rows for 2027. The
2027 Caribbean entry is currently unavailable on FIBA and remains a documented
source gap: [official page](https://www.fiba.basketball/en/history/199-fiba-basketball-world-cup-pre-qualifiers/208746).

The regional coverage cutoffs and the external-source audit are maintained in
[`americas-regional-ingestion.md`](americas-regional-ingestion.md). In brief,
the FIBA game-card floors are 1987 for Centrobasket, 2004 for COCABA, 1932 for
South American, and 2004 for the configured Caribbean editions. Argentine
federation pages corroborate South American 1987 and 1993 but expose only
partial team-level fixtures; Wikipedia and an archived CBC history page report
older COCABA/Caribbean editions that lack a complete validated game source.
Those records are documented, not synthesized or ingested.

## EuroBasket

Source policy is mixed by historical availability. GSA supplies the current
2,242 finals rows. Qualifiers contain FIBA, GSA, and the documented Wikipedia
fallback for the 1991/1993 historical gaps. Pre-qualifiers retain both FIBA
and GSA records where the source archives do not match cleanly.

Keep EuroBasket Division B and World Cup European qualification outside the
EuroBasket finals/qualifier family. Historical rounds are assigned to the
EuroBasket they qualify for, rather than to the source page year.

Sources: [FIBA EuroBasket archive](https://www.fiba.basketball/en/history/208-fiba-eurobasket),
[qualifiers](https://www.fiba.basketball/en/history/205-fiba-eurobasket-qualifiers),
[pre-qualifiers](https://www.fiba.basketball/en/history/204-fiba-eurobasket-pre-qualifiers),
and [Wikipediaâ€™s 1991 qualification tables](https://en.wikipedia.org/wiki/FIBA_EuroBasket_1991_qualification).

## AfroBasket

AfroBasket finals, qualifiers, and pre-qualifiers are separate competitions
under `afrobasket-{year}`. FIBA provides 839 finals rows, 436 qualifier rows,
and 32 pre-qualifier rows in the current snapshot; the remaining rows are
unmatched GSA comparison records.

Earlier qualifying phases can be embedded in the finals edition pages, so the
provider uses FIBA phase and round metadata to keep qualification games out of
the finals competition. World Cup African qualification is always separate.

Sources: [FIBA AfroBasket qualifiers archive](https://www.fiba.basketball/en/history/178-fiba-afrobasket-qualifiers),
[AfroBasket 2025 qualification guide](https://www.fiba.basketball/en/news/afrobasket-2025-qualifiers-news-your-guide-to-the-2025-afrobasket),
and [AfroBasket history](https://www.fiba.basketball/en/events/fiba-afrobasket-2025/history).

## FIBA Asia Cup

Asia Cup finals use the official FIBA archive from 1960 onward, with explicit
handling for the duplicated 2003 archive label. Qualifiers and pre-qualifiers
are stored under the championship cycle they feed. The current snapshot has
1,159 FIBA finals rows, 142 FIBA qualifier rows, 65 FIBA pre-qualifier rows,
and retained GSA comparison rows where reconciliation was not exact.

Asia/Oceania World Cup qualification and the separate Oceania Championship are
not relabeled as Asia Cup games. Australian and New Zealand participation in
the Asia Cup does not change that competition identity.

Sources: [FIBA Asia Cup archive](https://www.fiba.basketball/en/history/195-fiba-asia-cup),
[Asia Cup qualifiers](https://www.fiba.basketball/en/history/192-fiba-asia-cup-qualifiers),
and [FIBAâ€™s 2025 qualification explanation](https://www.fiba.basketball/en/news/everything-you-need-to-know-about-the-fiba-asia-cup-2025).

## FIBA AmeriCup

AmeriCup uses three explicit stages: finals, qualifiers, and pre-qualifiers.
The 2022 pre-qualifier cycle is assembled from four official FIBA regional and
archive pages, preserving South American, Caribbean/Central American, and main
pre-qualifier coverage instead of collapsing them into one qualifier league.

The current FIBA totals are 695 finals rows, 89 qualifier rows, 96
pre-qualifier rows, and 26 games in the 2029 pre-qualifier cycle. Target-cycle
mapping is explicit: the 2022 cycle uses FIBA source years 2019/2021, the 2025
cycle uses 2023/2025, and the 2029 pre-qualifiers use the 2026 events.

Migrations `20260808123000_ReconcileFibaAmeriCupGames`,
`20260808145000_ReconcileAmeriCupGsaIdentityVariants`, and
`20260808150500_ReconcileAmeriCupRemainingGsaNames` removed 852 verified GSA
duplicates. They use the same target season and stage, a bounded 31-day date
tolerance, normalized provider team names, and nearest-candidate checks; FIBA
scores remain canonical when GSA scores drift. The passes abort if candidates
are ambiguous and preserve manual overrides.

See the detailed [AmeriCup ingestion runbook](americup-ingestion.md). Sources:
[FIBA finals](https://www.fiba.basketball/en/history/184-fiba-americup),
[qualifiers](https://www.fiba.basketball/en/history/183-fiba-americup-qualifiers),
[pre-qualifiers](https://www.fiba.basketball/en/history/182-fiba-americup-pre-qualifiers),
[Wikipediaâ€™s AmeriCup history](https://en.wikipedia.org/wiki/FIBA_AmeriCup),
and [GSAâ€™s AmeriCup archive](https://globalsportsarchive.com/competition/basketball/fiba-americup-2025-nicaragua/group-stage/120539/).

## Filter and deployment verification

The API and Games page expose the shared cycle filter. For example:

```text
GET /api/games?tournamentCycle=americup-2025&pageSize=200
```

The deployed VPS returned 104 AmeriCup-cycle games for this filter, spanning
finals, qualifiers, and pre-qualifier rows, all labeled `FIBA AmeriCup 2025`.
The API, worker, and web health endpoints returned healthy responses after the
deployment. The pre-AmeriCup reconciliation backup is documented in
[`americup-ingestion.md`](americup-ingestion.md).

