# French league and cup historical ingestion

This runbook covers issue #125: game-level ingestion of the French men's top
flight and the Coupe de France through 2007-2008. Modern API-Sports coverage
starts in 2008-2009.

## Ranked league sources

| Rank | Source | Historical game coverage | Access and quality | Use |
| --- | --- | --- | --- | --- |
| 1 | [TheSports Pro A archive](https://www.the-sports.org/basketball-french-national-championships-events-list-s6-c0-b0-g40-p2.html) | Regular season and playoffs from 2001-2002 onward; seven seasons in this backfill | Complete dated scores and stable team IDs. Requires one index request and one AJAX request per round, but avoids the known date and score typos in overlapping BasketArchives playoff pages. | 2001-2002 through 2007-2008 |
| 2 | [BasketArchives](http://www.basketarchives.fr/somstats.htm) | Complete score pages for 1981-1982 and 1998-1999 through 2004-2005; eight full seasons, but not contiguous | Easiest source (one HTML request per season) and the specialist French archive. Some overlapping playoff pages contain incorrect years or impossible score transcriptions, so only the non-overlapping seasons are imported. | 1981-1982 and 1998-1999 through 2000-2001 |
| 3 | [LNB](https://www.lnb.fr/) historical publications | Official season validation and historical reports | Authoritative, but no stable bulk game-level archive for the requested years was found. | Validation only |
| 4 | French/English Wikipedia season articles | Standings, champions and selected playoff results | Easy to access, but regular-season game lists are incomplete. | Validation only |

The verified league catalog therefore contains the isolated complete 1981-1982
season, an explicit 1982-1983 through 1997-1998 gap, and a continuous
1998-1999 through 2007-2008 run. Standings matrices and win/loss totals are not
expanded into synthetic games.

## Ranked cup sources

| Rank | Source | Historical game coverage | Access and quality | Use |
| --- | --- | --- | --- | --- |
| 1 | [French Wikipedia Coupe de France archive](https://fr.wikipedia.org/wiki/Coupe_de_France_masculine_de_basket-ball) | Complete game-level edition articles for 2004-2005 through 2007-2008 | One MediaWiki API request per edition, stable revision IDs, early-round tables and full final brackets. | All four historical editions |
| 2 | TheSports | A small number of historical Cup brackets, including 2006-2007 | Useful cross-check, but less complete than the four edition articles and not a continuous archive. | Validation only |
| 3 | FFBB archived articles | Contemporary official reports linked from the edition articles | Authoritative but fragmented, with many obsolete URLs and no complete season index. | Validation only |

Only the four complete 2004-2005 through 2007-2008 Cup editions are cataloged.
Each has a matching ingested top-flight season. Walkovers and byes are not
invented as games, so edition game totals can be lower than the participant
count minus one when the source records an automatic advance.

## Expected game totals

| Competition | Season | Games |
| --- | --- | ---: |
| LNB | 1981-1982 | 182 |
| LNB | 1998-1999 | 258 |
| LNB | 1999-2000 | 258 |
| LNB | 2000-2001 | 258 |
| LNB | 2001-2002 | 256 |
| LNB | 2002-2003 | 258 |
| LNB | 2003-2004 | 323 |
| LNB | 2004-2005 | 327 |
| LNB | 2005-2006 | 333 |
| LNB | 2006-2007 | 322 |
| LNB | 2007-2008 | 256 |
| French Cup | 2004-2005 | 54 |
| French Cup | 2005-2006 | 54 |
| French Cup | 2006-2007 | 54 |
| French Cup | 2007-2008 | 52 |

## Commands

Dry-run one season:

```bash
dotnet run --project src/BasketElo.Tools -- france-dry-run \
  --competition "LNB" --season 2007-2008 --interval-ms 100
```

Ingest newest first:

```bash
dotnet run --project src/BasketElo.Tools -- france-ingest \
  --competition "LNB" --start 2007-2008 --end 1981-1982 --interval-ms 100

dotnet run --project src/BasketElo.Tools -- france-ingest \
  --competition "French Cup" --start 2007-2008 --end 2004-2005 --interval-ms 100
```

Run production ingestion only on the VPS. Stop the worker before using the tool
so it cannot race the command's in-process backfill processor, and restart it in
a cleanup trap.
