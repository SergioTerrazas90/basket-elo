# Olympic basketball ingestion

Olympic men's basketball is ingested as one tournament family with three
separate competitions:

| Stage | Application competition | Cycle key | Canonical source |
| --- | --- | --- | --- |
| Finals | `Summer Olympics` | `olympics-{edition}` | FIBA, with GSA comparison rows |
| Olympic qualifying tournaments | `Olympics Qualification` | `olympics-{edition}` | FIBA, with GSA comparison rows |
| Olympic pre-qualifying tournaments | `Olympics Pre-Qualification` | `olympics-{edition}` | FIBA, with GSA comparison rows |

The stage boundary is part of the data contract. A cycle filter such as
`2024` includes the 2024 Olympic finals, the 2024 OQTs, and the 2024 pre-OQTs,
but the competition filter can still select one stage only.

## Source and season mapping

The official FIBA history families are:

- [Men's Olympic Basketball Tournament](https://www.fiba.basketball/en/history/320-mens-olympic-basketball-tournament)
- [Olympic Qualifying Tournament](https://www.fiba.basketball/en/history/219-fiba-olympic-qualifying-tournament)
- [Olympic Pre-Qualifying Tournament](https://www.fiba.basketball/en/history/218-fiba-olympic-pre-qualifying-tournament)

The finals catalog covers editions from 1948 through 2024. The qualifier
catalog covers the dedicated men's OQT editions exposed by FIBA, including
1968–1992 and 2008–2024. There is no invented OQT season for years where the
qualification route used another competition or FIBA exposes no dedicated
edition.

The source year is not always the target cycle:

| Target cycle | Source edition year | Reason |
| --- | ---: | --- |
| 2020 OQT | 2021 | The Tokyo cycle's OQTs were played after the one-year pandemic postponement |
| 2024 pre-OQT | 2023 | The five Paris-cycle pre-OQTs were played in August 2023 |

The target cycle is retained in the season label and `TournamentCycleId`, while
the source year remains in game provenance.

## Wikipedia reconciliation

Wikipedia is used to validate edition presence, tournament count, dates, hosts,
and qualification relationships:

- [Basketball at the Summer Olympics](https://en.wikipedia.org/wiki/Basketball_at_the_Summer_Olympics)
- [2024 FIBA Men's Olympic Qualifying Tournaments](https://en.wikipedia.org/wiki/2024_FIBA_Men%27s_Olympic_Qualifying_Tournaments)
- [2024 FIBA Men's Pre-Qualifying Olympic Qualifying Tournaments](https://en.wikipedia.org/wiki/2024_FIBA_Men%27s_Pre-Qualifying_Olympic_Qualifying_Tournaments)

FIBA is canonical when it exposes an individual game. Wikipedia is not ingested
as a parallel game feed for the same edition; this prevents a source-level
duplicate from being mistaken for additional coverage. A missing official game
page remains a documented gap rather than a synthesized result.

## GSA reconciliation and deduplication

GSA's 2024 qualification archive contains both regular OQT rounds and
pre-qualifying rounds. The import splits them into:

- `Olympics Qualification`: 36 games (qualifying/group rounds, semifinals, and
  finals);
- `Olympics Pre-Qualification`: 66 games (pre-qualifying rounds, semifinals,
  and finals).

The migration creates the separate competition and source alias
`olympics-pre-qualification`, moves the existing GSA pre-OQT rows, and assigns
all existing Olympic rows to their `olympics-{edition}` cycle. Future jobs use
different provider league IDs, so the stages cannot collapse back together.

FIBA Olympic rows are preserved during cross-source deduplication, then GSA
comparison rows are removed only by a one-to-one identity match using source
stage, normalized teams, date, and score. A final pass permits a one-day date
offset because the archives occasionally publish the same game on adjacent UTC
dates. Source IDs remain unique within each provider, and the same game cannot
be counted twice merely because FIBA and GSA both expose it.

After reconciliation, the 2024 cycle contains 26 finals, 36 Olympic
qualification games, and 67 pre-qualification games. FIBA supplies all 26
finals and 36 OQT games; the remaining pre-qualification rows are GSA coverage
for games not exposed by the current FIBA history edition pages. Wikipedia
confirms that the Paris route had five pre-OQTs, which is why the GSA remainder
is retained rather than treated as an ingestion error.

## Operational verification

Before a live reconciliation or ingest:

1. create a PostgreSQL backup;
2. deploy the migration and provider changes;
3. run FIBA and GSA jobs with unlimited request budgets;
4. check counts by stage and cycle;
5. check source-key duplicates, same-stage identity duplicates, and cross-source
   duplicates; and
6. verify the API's `olympics-2024` cycle filter returns all three stages while
   competition filters remain isolated.

The 2024 route is consistent with FIBA's explanation: four OQTs supplied the
last Olympic places, and the pre-OQTs supplied the teams that entered those
OQTs.
