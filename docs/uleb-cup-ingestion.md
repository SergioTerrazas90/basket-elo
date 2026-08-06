# ULEB Cup historical ingestion

The catalog exposes the ULEB Cup separately from both the FIBA Saporta Cup and
the modern EuroCup:

- source: `wikipedia-uleb-cup`
- application seasons: `2002-2003` through `2007-2008` (six editions)
- ELO pool: `EuropeClubs`

The provider imports edition-level game scores from the historical ULEB Cup
articles, preserving the published stage, round, home and away sides, scores,
and source identifiers. Two-leg knockout ties are stored as separate games.
When an English edition is standings-only, the provider falls back to the
corresponding German Wikipedia edition, which retains score matrices for later
ULEB seasons.
The official [Euroleague historical matchups reference](https://mediacentre.euroleague.net/uploads/EuroleagueCore/pastmatchups/round4017.pdf)
is retained in provenance and used to reconcile season structure, finals, and
match-count expectations. It is not treated as a game-level source when it
contains standings-only material.

Historical group tables may publish scores without an exact date for every
match. Those rows receive deterministic edition-order dates and a warning;
the coverage row must be reviewed on the VPS before the ELO rebuild is queued.
Historical country labels and source team IDs are retained so former
Yugoslavia/FR Yugoslavia identities are not merged by display-name heuristics.

## VPS validation and ingest

After deployment, run one edition first:

```bash
/opt/basket-elo/releases/tools/BasketElo.Tools uleb-cup-dry-run \
  --season 2002-2003 \
  --max-requests 4
```

Then run the complete six-edition ingest. The command skips completed and
active season keys, replaces stale source rows on rerun, and uses the normal
identity-health and Europe-club ELO rebuild workflow:

```bash
/opt/basket-elo/releases/tools/BasketElo.Tools uleb-cup-ingest \
  --max-jobs 0 \
  --max-requests 4
```

Record the VPS dry-run/ingest counts in the coverage audit below after
validation.

## Coverage audit

| Season | Game records | Notes |
| --- | ---: | --- |
| 2002-2003 | 150 | English edition; 120 group-stage plus 30 knockout/final games |
| 2003-2004 | 209 | German fallback; 180 group-stage plus 29 knockout/final games |
| 2004-2005 | 239 | German fallback; 210 group-stage plus 29 knockout/final games |
| 2005-2006 | 149 | German fallback; 120 group-stage plus 29 knockout/final games |
| 2006-2007 | 149 | German fallback; 120 group-stage plus 29 knockout/final games |
| 2007-2008 | 326 | German fallback; 270 group-stage plus 56 knockout/final-stage games |
