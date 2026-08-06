# FIBA Korać Cup ingestion

The backfill catalog exposes the men's FIBA Korać Cup as a competition separate from the Champions Cup and Saporta Cup:

- source: `fiba`
- official history family: `164-eurocup-challenge` (EuroCup Challenge)
- application seasons: `1971-1972` through `2001-2002` (31 editions)
- ELO pool: `EuropeClubs`

FIBA labels the first archive edition `1972` and the last edition `2002`; the provider maps those archive years to the application seasons beginning in 1971 and 2001. It follows the edition's `/games` page and preserves each published score as an individual game, including both legs of a two-leg tie.

FIBA team identifiers and published country codes are retained as source
provenance. Historical Yugoslavia and Federal Republic of Yugoslavia records
are therefore not collapsed by display-name heuristics; any identity merge is
left to the existing review workflow.

For older or sparse official pages, the provider uses the historical Wikipedia edition table as a score reconciliation fallback. Official FIBA records remain preferred whenever they provide a richer game set. The fallback titles account for the first edition's `1972 FIBA Korać Cup` title, the second edition's `1973 FIBA Korać Cup` title, and the final `2001–02 FIBA Korać Cup` title.

## Coverage audit

A live read-only dry run on 2026-08-06 exercised every configured edition. It
returned 4,450 finished game records across all 31 seasons:

| Season | Games | Season | Games | Season | Games |
| --- | ---: | --- | ---: | --- | ---: |
| 1971-1972 | 14 | 1982-1983 | 103 | 1993-1994 | 184 |
| 1972-1973 | 24 | 1983-1984 | 85 | 1994-1995 | 220 |
| 1973-1974 | 71 | 1984-1985 | 99 | 1995-1996 | 224 |
| 1974-1975 | 88 | 1985-1986 | 112 | 1996-1997 | 314 |
| 1975-1976 | 82 | 1986-1987 | 106 | 1997-1998 | 296 |
| 1976-1977 | 59 | 1987-1988 | 120 | 1998-1999 | 306 |
| 1977-1978 | 83 | 1988-1989 | 132 | 1999-2000 | 298 |
| 1978-1979 | 99 | 1989-1990 | 146 | 2000-2001 | 240 |
| 1979-1980 | 95 | 1990-1991 | 150 | 2001-2002 | 196 |
| 1980-1981 | 87 | 1991-1992 | 146 |  |  |
| 1981-1982 | 95 | 1992-1993 | 176 |  |  |

All 31 editions completed with warnings because FIBA still exposes some
placeholder team cards in its historical payloads. Those unresolved cards are
not synthesized; the published, identifiable games remain importable and the
normal identity-review workflow receives the warnings before ELO rebuild.

Run the normal backfill command for `Europe:FIBA Korac Cup` after validating the edition-level coverage rows. Successful replacement runs remove stale records for the same season/source and queue the Europe-club ELO rebuild through the standard backfill processor.
