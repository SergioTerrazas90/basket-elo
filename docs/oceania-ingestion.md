# FIBA Oceania Championship ingestion

This runbook covers the men’s senior FIBA Oceania Championship archive for
issue #187. It intentionally excludes women’s, youth, 3x3, and other Oceania
competitions.

## Tournament identity

The official FIBA history family is
[`216-fiba-oceania-championship`](https://www.fiba.basketball/en/history/216-fiba-oceania-championship).
The catalog targets these archive edition labels:

`1971, 1975, 1978, 1979, 1981, 1983, 1985, 1987, 1989, 1991, 1993, 1995,
1997, 1999, 2001, 2003, 2005, 2007, 2009, 2011, 2013, 2015`.

Every edition is a distinct international competition named
`FIBA Oceania Championship` and belongs to its own cycle:

```text
oceania-{edition}
```

For example, the 2015 edition is returned by the shared API cycle filter as
`FIBA Oceania Championship 2015`. Oceania rows are not relabeled as Asia Cup
rows even though the Asia-Pacific structure later merged the competitions.

## Source and reconciliation policy

FIBA is canonical for all usable official game cards. The importer preserves
the FIBA source URL, source game ID, source season key, date, teams, score,
phase, and round. FIBA Oceania rows are protected from generic cross-source
deletion so a later comparison source cannot silently replace an official row.

There is no configured GSA Oceania family. If another source is added later,
rows may be removed only after a one-to-one match on cycle, stage, date,
normalized teams, and result, with a verified PostgreSQL backup first.

Wikipedia’s [FIBA Oceania Championship page](https://en.wikipedia.org/wiki/FIBA_Oceania_Championship)
is a completeness and format cross-check only. It is not a game source, and
standings or medal tables must never be turned into synthetic games.

## Operational verification

Run representative dry-runs:

```text
BasketElo.Tools fiba-dry-run --country Oceania --league "FIBA Oceania Championship" --season 2015 --max-requests 0
BasketElo.Tools fiba-dry-run --country Oceania --league "FIBA Oceania Championship" --season 1971 --max-requests 0
```

After ingestion, verify that `/api/games?tournamentCycle=oceania-2015` returns
only the 2015 cycle, source keys are unique, normalized same-stage identities
have no duplicate groups, and API, worker, and web health endpoints are healthy.

If an official edition has no usable game page, record it as an archive gap in
the completion summary rather than backfilling it from Wikipedia.
