# Greek league and Cup historical ingestion

This runbook covers issue #126: game-level ingestion of the Greek men's top
flight and Greek Cup before modern API-Sports coverage begins in 2008-2009.

## Coverage decision

The historical catalog starts at 1996-1997 and ends at 2007-2008. The apparent
three-season gap before ESAKE's continuous 1999-2000 run was filled from two
contemporary Greek archives: Bitzenis for 1996-1997 and archived Sport.gr pages
for 1997-1998 and 1998-1999. The next older block remains separated by a real
source gap, so 1996-1997 is the cutoff.

| Competition | Season span | Provider | Notes |
| --- | --- | --- | --- |
| A1 / Greek Basket League | 1996-1997 | Bitzenis historical results | All 182 regular-season games. The available playoff summary has no game dates, so those playoff rows are not ingested. |
| A1 / Greek Basket League | 1997-1998 | Sport.gr via the Internet Archive | All 182 regular-season games and all 34 dated playoff games. |
| A1 / Greek Basket League | 1998-1999 | Sport.gr via the Internet Archive | 181 scored regular-season games and 33 dated playoff games. Panionios-AEK was interrupted and nullified without a final score, so it is excluded. |
| A1 / Greek Basket League | 1999-2000 through 2007-2008 | ESAKE official results archive | Regular season and playoffs, using stable official game and team IDs. |
| A1 / Greek Basket League | 2008-2009 onward | API-Sports | Existing modern coverage. |
| Greek Cup | 1996-1997 through 2007-2008, except 2003-2004 | EOK official Cup archive | Only cataloged when that season also has ingested A1 games. The 2003-2004 page is excluded because it starts at game 15 and omits the first 14 games. |
| Greek Cup | 2008-2009 onward, except 2015-2016 | API-Sports | Existing modern coverage. The 2015-2016 Cup-only edition is excluded because API-Sports returned no matching A1 games. |

Official 20-0 administrative awards are not treated as played games. Historical
dates without a trustworthy tip-off time are stored at 12:00 UTC.

The resulting historical production coverage is 2,491 A1 games across twelve
seasons and 470 Cup games across eleven editions:

| Season | A1 games | Greek Cup games |
| --- | ---: | ---: |
| 1996-1997 | 182 | 40 (one 20-0 award and incomplete source row 17 excluded) |
| 1997-1998 | 216 | 42 |
| 1998-1999 | 214 | 41 (one 20-0 award excluded) |
| 1999-2000 | 218 | 41 |
| 2000-2001 | 207 | 42 |
| 2001-2002 | 211 | 42 |
| 2002-2003 | 206 | 42 |
| 2003-2004 | 203 | Not ingested: incomplete official page |
| 2004-2005 | 209 | 42 |
| 2005-2006 | 207 | 47 |
| 2006-2007 | 209 | 47 |
| 2007-2008 | 209 | 44 |

## Ranked league providers

| Rank | Source | Seasons useful for this task | Assessment | Use |
| --- | --- | --- | --- | --- |
| 1 | [ESAKE official results archive](https://www.esake.gr/el/action/EsakeResults?idchampionship=0000000C&idseason=00000001&mode=2&series=undefined) | Nine continuous seasons, 1999-2000 through 2007-2008 | Authoritative, dated game scores, regular-season rounds, playoff series, stable game IDs, and stable source team IDs. | Ingested provider |
| 2 | [Sport.gr archived by the Internet Archive](https://web.archive.org/web/20080528083751id_/http://archive.sport.gr/basket/hellas/a1/1-14.htm) | 1997-1998 and 1998-1999 | Complete paired regular-season round pages and dated playoff series. Easy deterministic traversal once the archived URL patterns are known. | Ingested provider |
| 3 | [Bitzenis 1996-1997 results](https://bitzenis.gr/retro/bask.htm) | 1996-1997 regular season | A single compact page contains the full 182-game regular season. Its playoff summary lacks dates, so only the dated regular season is imported. | Ingested provider |
| 4 | [Basketball-Reference Greek league schedules](https://www.basketball-reference.com/international/greek-basket-league/2002-schedule.html) | Seven seasons, 2001-2002 through 2007-2008 | Easy single-season schedules, but starts later and is not the competition owner. | Validation |
| 5 | [Olympiacos official historical schedule](https://www.olympiacosbc.gr/en/games/gbl/schedule/1996.html), Galanis Sports Data, RealGM, and Wikipedia | Partial team schedules, standings, matrices, or summaries | Useful independent checks, not better complete ingestion feeds. | Validation only |

## Ranked Cup providers

| Rank | Source | Seasons useful for this task | Assessment | Use |
| --- | --- | --- | --- | --- |
| 1 | [EOK official men's Cup archive](https://www.basket.gr/cup-men/kypello-andron-1996-1997/1750/) | Eleven usable editions in the league-overlap window | Authoritative game-level pages exposed through the site's WordPress API, with dates, teams, scores, and page revision metadata. | Ingested provider |
| 2 | TheSports and Livesport/Flashscore archives | Shorter or later coverage | Useful for spot checks, but they do not provide a better continuous pre-2008 run. | Validation only |
| 3 | Wikipedia | Finals and edition summaries | Does not consistently contain all early-round games. | Validation only |

## Safety rules

- The Cup catalog is a subset of the historical league catalog.
- At runtime, `greece-ingest` queries actual A1 games and skips any Cup season
  without matching regular-league data.
- The incomplete 2003-2004 EOK Cup page is not cataloged.
- The provider rejects tied scores and 20-0 administrative awards as unplayed
  or non-competitive records.
- The 1996-1997 EOK page's incomplete game 17 is excluded. Its malformed Final
  Four headings are normalized to 12 April 1997 for the semifinals and 13 April
  1997 for the third-place game and final.
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
  --competition "A1" --start 2007-2008 --end 1996-1997 --interval-ms 100

dotnet run --project src/BasketElo.Tools -- greece-ingest \
  --competition "Greek Cup" --start 2007-2008 --end 1996-1997 --interval-ms 100
```

Run production ingestion only on the VPS. Stop the worker before using the tool
so it cannot race the command's in-process processor, and restart it afterward.
