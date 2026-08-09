# AmeriCup ingestion and reconciliation

For the cross-tournament comparison and current coverage snapshots, see
[`fiba-national-team-tournaments.md`](fiba-national-team-tournaments.md).

AmeriCup is modeled as one tournament family with separate competition stages:

- `FIBA AmeriCup` — finals.
- `FIBA AmeriCup Qualifiers` — the qualifying groups.
- `FIBA AmeriCup Pre-Qualifiers` — sub-zone and pre-qualifying events.

All stages for a championship cycle share `TournamentCycle.Key = americup-{year}`. The cycle year is the target AmeriCup finals year, not necessarily the calendar year printed on the historical FIBA page. For example, the 2022 cycle uses the 2019 FIBA pre-qualifier pages and the 2021 FIBA qualifier page.

## Canonical and validation sources

- [FIBA AmeriCup finals archive](https://www.fiba.basketball/en/history/184-fiba-americup)
- [FIBA AmeriCup qualifiers archive](https://www.fiba.basketball/en/history/183-fiba-americup-qualifiers)
- [FIBA AmeriCup pre-qualifiers archive](https://www.fiba.basketball/en/history/182-fiba-americup-pre-qualifiers)
- [Wikipedia: FIBA AmeriCup](https://en.wikipedia.org/wiki/FIBA_AmeriCup)
- [Wikipedia: 2025 AmeriCup qualification](https://en.wikipedia.org/wiki/2025_FIBA_AmeriCup_qualification)
- [Global Sports Archive finals](https://globalsportsarchive.com/competition/basketball/fiba-americup-2025-nicaragua/group-stage/120539/)
- [Global Sports Archive qualification](https://globalsportsarchive.com/competition/basketball/fiba-americup-qualification-2025-nicaragua/qualifiers/93859/)

FIBA is the canonical game source. Wikipedia is used for edition and qualification-structure validation; it is not ingested as a game source. GSA is a reconciliation/reference source, and its duplicate rows are removed only after a deterministic match to FIBA.

## FIBA season mapping

| Target cycle | FIBA source pages | Ingested games |
| --- | --- | ---: |
| Finals 1980–2017 | Official finals archive editions | 21–42 per edition |
| Finals 2022 | Official 2022 edition | 26 |
| Finals 2025 | Official 2025 event | 26 |
| Qualifiers 2022 | Archive page `208142` (source year 2021) | 42 |
| Qualifiers 2025 | Official 2025 qualifiers event | 47 |
| Pre-qualifiers 2022 | Archive pages `208038`, `208039`, `208040`, `208060` (source year 2019) | 46 |
| Pre-qualifiers 2025 | Archive pages `208515`–`208518` (source year 2023) | 24 |
| Pre-qualifiers 2029 | 2029 Caribbean and Central American events (source year 2026) | 26 |

The 2022 pre-qualifier cycle is deliberately assembled from four FIBA pages so South America, Caribbean/Central America, and the main pre-qualifier event are not collapsed or omitted.

## Reconciliation and duplicate policy

Migrations `20260808123000_ReconcileFibaAmeriCupGames`,
`20260808145000_ReconcileAmeriCupGsaIdentityVariants`, and
`20260808150500_ReconcileAmeriCupRemainingGsaNames` remove only GSA rows with
one-to-one matches to FIBA in the same target season and stage. The bounded
31-day date tolerance handles historical archive drift; the follow-up passes
also handle provider team-name variants and source score drift while keeping
FIBA's result canonical. Each pass aborts before deletion if candidates are
ambiguous, and manual result overrides are preserved.

Post-reconciliation checks are expected to remain zero for:

- duplicate source keys;
- duplicate same-stage identity keys;
- cross-source matches within the reconciliation key.

The verified VPS backups for the reconciliation passes are
`/var/backups/basket-elo/basket-elo-20260808-135534.dump` and
`/var/backups/basket-elo/basket-elo-20260808-140017-after-first-reconcile.dump`.

Known source limitations are retained as warnings rather than synthesized: 20
1992 finals records have no game date and use the edition start date, two 2022
qualifier records have no game date and use the edition start date. The FIBA
2025 Caribbean page `208515` is an unplayed event shell: it exposes 20 TBD
cards, reports 0/10 games in each group, and has no teams, scores, or stable
game links. This is not a missing set of played games: Antigua and Barbuda and
Barbados advanced into the actual June 2023 main pre-qualifier (`208517`),
whose game records are ingested. The 20 shell cards are therefore not
fabricated.
