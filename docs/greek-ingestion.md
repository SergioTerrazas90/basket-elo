# Greek league and Cup historical ingestion

This runbook covers issue #126: game-level ingestion of the Greek men's top
flight and Greek Cup before modern API-Sports coverage begins in 2008-2009.

## Coverage decision

The historical catalog starts at 1999-2000 and ends at 2007-2008. ESAKE's
official results selector contains usable games for 1992-1993 through
1995-1996, then no games for 1996-1997, 1997-1998, or 1998-1999. That
three-season void is the wide gap requested as the cutoff, so the isolated
older block is not ingested.

| Competition | Season span | Provider | Notes |
| --- | --- | --- | --- |
| A1 / Greek Basket League | Before 1999-2000 | None | Not cataloged because the official archive has a three-season gap immediately before the continuous run. |
| A1 / Greek Basket League | 1999-2000 through 2007-2008 | ESAKE official results archive | Regular season and playoffs, using stable official game and team IDs. |
| A1 / Greek Basket League | 2008-2009 onward | API-Sports | Existing modern coverage. |
| Greek Cup | 1999-2000 through 2007-2008, except 2003-2004 | EOK official Cup archive | Only cataloged when that season also has ingested A1 games. The 2003-2004 page is excluded because it starts at game 15 and omits the first 14 games. |
| Greek Cup | 2008-2009 onward | API-Sports | Existing modern coverage. |

Official 20-0 administrative awards are not treated as played games. Historical
dates without a trustworthy tip-off time are stored at 12:00 UTC.

## Ranked league providers

| Rank | Source | Seasons useful for this task | Assessment | Use |
| --- | --- | --- | --- | --- |
| 1 | [ESAKE official results archive](https://www.esake.gr/el/action/EsakeResults?idchampionship=0000000C&idseason=00000001&mode=2&series=undefined) | Nine continuous seasons, 1999-2000 through 2007-2008 | Authoritative, dated game scores, regular-season rounds, playoff series, stable game IDs, and stable source team IDs. The best combination of completeness and identity quality. | Ingested provider |
| 2 | [Basketball-Reference Greek league schedules](https://www.basketball-reference.com/international/greek-basket-league/2002-schedule.html) | Seven seasons, 2001-2002 through 2007-2008 | Easy single-season schedules, but starts two seasons later and is not the competition owner. | Validation |
| 3 | TheSports Greek championship archive | Approximately 2001-2002 onward | Broad round pages but no advantage over ESAKE and a shorter historical span. | Validation |
| 4 | [Galanis Sports Data](https://www.galanisportsdata.gr/company/) | Company archive dates from 1998, with inconsistent public/archived endpoints | Greek specialist useful for reconciliation, but not a stable automated season archive. | Reconciliation only |
| 5 | RealGM and Wikipedia | Patchy schedules or standings/champions | Useful spot checks, not complete continuous game-level coverage. | Validation only |

## Ranked Cup providers

| Rank | Source | Seasons useful for this task | Assessment | Use |
| --- | --- | --- | --- | --- |
| 1 | [EOK official men's Cup archive](https://www.basket.gr/cup-men/kypello-andron-1992-1993/1746/) | Eight complete editions in the league-overlap window | Authoritative game-level pages exposed through the site's WordPress API, with dates, teams, scores, and page revision metadata. | Ingested provider |
| 2 | TheSports and Livesport/Flashscore archives | Shorter or later coverage | Useful for spot checks, but they do not provide a better continuous pre-2008 run. | Validation only |
| 3 | Wikipedia | Finals and edition summaries | Does not consistently contain all early-round games. | Validation only |

## Safety rules

- The Cup catalog is a subset of the historical league catalog.
- At runtime, `greece-ingest` queries actual A1 games and skips any Cup season
  without matching regular-league data.
- The incomplete 2003-2004 EOK Cup page is not cataloged.
- The provider rejects tied scores and 20-0 administrative awards as unplayed
  or non-competitive records.
- Stable provider game IDs make reruns idempotent.

## Commands

Dry-run one season:

```bash
dotnet run --project src/BasketElo.Tools -- greece-dry-run \
  --competition "A1" --season 2007-2008 --interval-ms 100

dotnet run --project src/BasketElo.Tools -- greece-dry-run \
  --competition "Greek Cup" --season 2007-2008 --interval-ms 100
```

Ingest newest first:

```bash
dotnet run --project src/BasketElo.Tools -- greece-ingest \
  --competition "A1" --start 2007-2008 --end 1999-2000 --interval-ms 100

dotnet run --project src/BasketElo.Tools -- greece-ingest \
  --competition "Greek Cup" --start 2007-2008 --end 1999-2000 --interval-ms 100
```

Run production ingestion only on the VPS. Stop the worker before using the tool
so it cannot race the command's in-process processor, and restart it afterward.
