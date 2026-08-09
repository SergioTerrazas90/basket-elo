# Historical FIBA Americas regional championships

This runbook covers the men’s senior regional competitions added for issue
#188. Each family remains separate from FIBA AmeriCup finals, qualifiers, and
pre-qualifiers. Women’s, youth, 3x3, and non-senior competitions are excluded.

## Families and cycles

| Family | FIBA history family | Cycle key | Catalog editions |
| --- | --- | --- | --- |
| Centrobasket Championship | `122-centrobasket-championship` | `centrobasket-{year}` | 1965–2016 official edition labels |
| COCABA Championship | `113-cbc-championship` | `cocaba-{year}` | 2003, 2004, 2006, 2007, 2009, 2011, 2013, 2015 |
| South American Championship | `327-south-american-championship` | `south-american-{year}` | 1932, 1942, 1943, 1987–2016 official labels |
| Caribbean Basketball Championship | `113-cbc-championship` | `caribbean-{year}` | 2004, 2006, 2007, 2009, 2011, 2014, 2015 |

The FIBA CBC archive contains multiple rows with the same year. The provider
uses explicit event paths for the COCABA and Caribbean variants so they remain
distinct, especially for 2004, 2006, 2007, 2009, 2011, and 2015.

## Source policy

FIBA is canonical for usable official game cards. The importer preserves source
URLs, game IDs, edition keys, dates, teams, scores, phases, and rounds. Empty
or incomplete archive rows are documented as gaps; standings or medal tables
are never converted into synthetic games.

There is no configured GSA family for these four competitions. If a later
source overlaps AmeriCup or GSA data, reconciliation must use a verified
one-to-one match on target cycle, stage, date, normalized teams, and final
result. The regional FIBA rows are protected from generic cross-source deletion.

Use the official [Centrobasket archive](https://www.fiba.basketball/en/history/122-centrobasket-championship),
[CBC/COCABA archive](https://www.fiba.basketball/en/history/113-cbc-championship),
and [South American archive](https://www.fiba.basketball/en/history/327-south-american-championship)
for completeness checks. Wikipedia may validate edition existence and format,
but is not an ingestion source.

## Coverage cutoffs and external-source audit

Audit cutoff: 2026-08-08 (the deployed VPS snapshot below). The dates below
distinguish an archive cutoff from a coverage cutoff. An
*archive floor* is the earliest edition currently listed by the official FIBA
history family. A *game-card floor* is the earliest configured edition for
which this importer found at least one usable FIBA game card. Neither cutoff
claims that every edition between the floor and 2016 is complete.

| Family | FIBA archive floor | FIBA game-card floor | Last official edition in scope | Current incomplete editions |
| --- | ---: | ---: | ---: | --- |
| Centrobasket | 1965 | 1987 | 2016 | 1965, 1967, 1969, 1971, 1973, 1975, 1977, 1981, 1985, 2002 |
| COCABA | 2003 | 2004 | 2015 | 2003, 2007 |
| South American | 1932 | 1932 | 2016 | 1987, 1993, 2011, 2015 |
| Caribbean | 2004 in the configured FIBA catalog | 2004 | 2015 | None in the configured 2004–2015 editions |

Date quality has a separate cutoff. The South American editions 1932, 1942,
1943, 1989, 1991, 1995, and 1997 contain FIBA game cards without match dates;
the importer uses the edition start date as a documented fallback. The rows
remain usable for tournament filtering, but consumers should not treat those
dates as fixture dates.

Other sources were checked for the incomplete editions:

- The [Argentine Basketball Confederation page for South American 1987](https://www.argentina.basketball/ver/torneo/sudamericano-1987)
  confirms the men's senior tournament dates and format and lists Argentina's
  six games. The [1993 federation page](https://www.argentina.basketball/ver/torneo/sudamericano-1993)
  confirms the men's senior event and standings. These are valuable
  corroboration, but they do not expose a complete all-team game list, so they
  are not ingested as a partial tournament.
- The [Centrobasket article](https://en.wikipedia.org/wiki/Centrobasket) and
  [South American Championship article](https://en.wikipedia.org/wiki/South_American_Basketball_Championship)
  validate historical existence and format. The latter reports a 1930 origin,
  while the current official FIBA archive begins at 1932; its 2018 edition is
  reported as cancelled and is therefore not a missing tournament to ingest.
- The [COCABA article](https://en.wikipedia.org/wiki/FIBA_COCABA_Championship)
  reports a 1999 men's edition not present in the current FIBA archive rows.
  The [archived Tortola 2015 CBC history page](https://web.archive.org/web/20150618182606/http://www.tortola2015.com/history.php)
  and [Caribbean Championship article](https://en.wikipedia.org/wiki/FIBA_Caribbean_Championship)
  also point to men's CBC editions in 1998, 2000, and 2002. No complete,
  independently validated game-card source for those early editions was found
  in this pass, so they remain documented candidate history rather than
  ingested data.

The remaining gaps are therefore source-coverage gaps, not deduplication gaps.
No synthetic games or standings-to-game conversions are permitted by this
runbook.

## Verification

For each configured family, run a dry-run before the full backfill:

```text
BasketElo.Tools fiba-dry-run --country Americas --league "Centrobasket Championship" --season 2016 --max-requests 0
BasketElo.Tools fiba-dry-run --country Americas --league "COCABA Championship" --season 2015 --max-requests 0
BasketElo.Tools fiba-dry-run --country Americas --league "South American Championship" --season 2016 --max-requests 0
BasketElo.Tools fiba-dry-run --country Americas --league "Caribbean Basketball Championship" --season 2015 --max-requests 0
```

After ingestion, verify that each family’s cycle filter contains only its own
competition, source keys are unique, normalized same-stage identities have no
duplicates, and repeated ingestion updates rather than inserts the same FIBA
source rows.

## Deployed verification snapshot

The 2026-08-08 VPS pass produced 910 games: 359 Centrobasket, 77 COCABA, 322
South American, and 152 Caribbean. There are 0 source-key duplicates, 0
same-stage identity duplicates, 0 exact overlaps with AmeriCup, 0 GSA rows,
and 0 failed jobs. A repeat run queued 0 jobs for every family.

The verified pre-write backup is
`/var/backups/basket-elo/basket-elo-20260808-162221-pre-americas-regional.dump`.
