# FIBA Saporta Cup historical ingestion

The `fiba` provider catalogs the complete second-tier European club lineage
from `1967-1968` through `2001-2002` under the canonical competition name
`FIBA Saporta Cup`. The historical names are preserved in source provenance:

- 1967-1968 through 1990-1991: European Cup Winners' Cup
- 1991-1992 through 1995-1996: European Cup
- 1996-1997 through 1997-1998: EuroCup
- 1998-1999 through 2001-2002: Saporta Cup

The primary source is the official [FIBA Men’s European Club Competitions -
Tier 2 archive](https://www.fiba.basketball/en/history/212-fiba-mens-european-club-competitions-tier-2).
FIBA indexes an edition by its ending year, so application season
`1967-1968` resolves the FIBA `1968` edition and `2001-2002` resolves `2002`.
Official game IDs, team IDs/names, dates, scores, phase, round, source URL,
edition key, parser version, and source revision are retained.

For editions with unresolved FIBA placeholder cards, the provider also checks
the corresponding English Wikipedia edition article. Those pages provide
dated two-leg results, knockout finals, and later group-stage score matrices.
The alternate parser uses deterministic dates only when the article does not
publish an exact match date; that limitation is recorded in the job warning.
The Stephan Müller [European club competition historical results
PDF](https://www.sport-record.info/basketball/basketball-ec-hist.pdf) is used
as a validation reference for winners, finals, naming, and competition format;
it is not automatically imported as a game-level source.

## Coverage audit

A live read-only audit on 2026-08-06 exercised every configured season. It
returned 2,460 finished game records across all 35 seasons:

| Season | Games | Season | Games | Season | Games |
| --- | ---: | --- | ---: | --- | ---: |
| 1967-1968 | 41 | 1979-1980 | 57 | 1991-1992 | 120 |
| 1968-1969 | 41 | 1980-1981 | 53 | 1992-1993 | 123 |
| 1969-1970 | 38 | 1981-1982 | 57 | 1993-1994 | 121 |
| 1970-1971 | 50 | 1982-1983 | 51 | 1994-1995 | 131 |
| 1971-1972 | 36 | 1983-1984 | 59 | 1995-1996 | 129 |
| 1972-1973 | 57 | 1984-1985 | 53 | 1996-1997 | 121 |
| 1973-1974 | 50 | 1985-1986 | 57 | 1997-1998 | 121 |
| 1974-1975 | 52 | 1986-1987 | 51 | 1998-1999 | 120 |
| 1975-1976 | 49 | 1987-1988 | 53 | 1999-2000 | 121 |
| 1976-1977 | 63 | 1988-1989 | 55 | 2000-2001 | 57 |
| 1977-1978 | 57 | 1989-1990 | 55 | 2001-2002 | 57 |
| 1978-1979 | 53 | 1990-1991 | 51 |  |  |

Early editions include explicit warnings where FIBA exposes unresolved
placeholder teams or the fallback article lacks an exact leg date. Those
records are retained with deterministic edition-order dates only when the
source provides enough information to identify the game; unresolved team
cards are not synthesized. A production job therefore completes with
warnings and feeds the existing identity-review workflow before the European
club ELO rebuild is allowed to run.

## Read-only checks

```text
dotnet run --project src/BasketElo.Tools -- fiba-dry-run \
  --country Europe \
  --league "FIBA Saporta Cup" \
  --season 1967-1968 \
  --max-requests 0
```

## VPS ingestion

Run after confirming the database backup. The command queues all 35 editions,
skips completed jobs, preserves idempotency, and writes the normal identity
health/rebuild follow-up records:

```text
dotnet /opt/basket-elo/releases/tools/BasketElo.Tools fiba-ingest \
  --country Europe \
  --league "FIBA Saporta Cup" \
  --max-jobs 0 \
  --max-requests 0
```

Because the same command also sees other configured FIBA competitions, use the
job summary and database filters to confirm that the Saporta rows completed
before starting another European-club provider.
