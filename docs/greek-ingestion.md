# Greek league and Cup historical ingestion

This runbook covers issue #126: game-level ingestion of the Greek men's top
flight and Greek Cup before modern API-Sports coverage begins in 2008-2009.

## Coverage decision

The continuous historical catalog starts at 1986-1987 and the reviewed archive
repairs now also include 2015-2016. Modern API-Sports coverage remains the
fallback for the seasons in between. The 1986-1987 through 1991-1992 seasons
come from complete Greek Wikipedia score matrices; their round dates are
inferred from the late-September start and cadence established by 1992-1993
because the Olympiacos archive exposes placeholder dates for this period.
This is a deliberate data-quality limitation: the imported date represents the
inferred round date, not a published game date or tip-off time. Consumers must
not use these six seasons for date-level scheduling or tip-off-time analysis.
The ingestion was validated with 90 games for each 10-team season (1986-87 to
1988-89) and 132 games for each 12-team season (1989-90 to 1991-92).
The former 1993-1994 through 1995-1996 gap is now filled from the official ESAKE
archive and matching official EOK Cup pages. The apparent gap before ESAKE's
continuous 1999-2000 run was filled from two contemporary Greek archives:
Bitzenis for 1996-1997 and archived Sport.gr pages for 1997-1998 and 1998-1999.

| Competition | Season span | Provider | Notes |
| --- | --- | --- | --- |
| A1 / Greek Basket League | 1986-1987 through 1991-1992 | Greek Wikipedia | Complete regular-season matrices: 90 games per 10-team season (1986-87 through 1988-89), then 132 games per 12-team season (1989-90 through 1991-92). Dates and rounds are inferred; playoffs are excluded. |
| A1 / Greek Basket League | 1992-1993 | ESAKE, Greek Wikipedia, and Olympiacos BC | ESAKE supplies dated rounds 1-22 (154 games). Greek Wikipedia's complete score matrix supplies the 28 reverse fixtures in rounds 23-26, dated from Olympiacos' official round schedule. All 182 regular-season games are ingested; undated playoffs are excluded. |
| A1 / Greek Basket League | 1993-1994 through 1995-1996 | ESAKE official results archive | 1993-1994 and 1994-1995 expose 181 regular-season games plus dated playoffs; the 1995-1996 archive exposes only eight regular-season rounds (56 games), with no reliable source rows for the remainder. |
| A1 / Greek Basket League | 1996-1997 | Bitzenis historical results | All 182 regular-season games. The available playoff summary has no game dates, so those playoff rows are not ingested. |
| A1 / Greek Basket League | 1997-1998 | Sport.gr via the Internet Archive | All 182 regular-season games and all 34 dated playoff games. |
| A1 / Greek Basket League | 1998-1999 | Sport.gr via the Internet Archive | 181 scored regular-season games and 33 dated playoff games. Panionios-AEK was interrupted and nullified without a final score, so it is excluded. |
| A1 / Greek Basket League | 1999-2000 through 2007-2008 | ESAKE official results archive | Regular season and playoffs, using stable official game and team IDs. |
| A1 / Greek Basket League | 2015-2016 | [Basketball-Reference](https://www.basketball-reference.com/euro/greek-basket-league/2016-schedule.html) | 182 regular-season games and 24 dated playoff games (206 total). The `/euro/` schedule exposes the regular and playoff tables separately. |
| A1 / Greek Basket League | 2008-2009 onward | API-Sports | Existing modern coverage. |
| Greek Cup | 1992-1993 through 2007-2008, except 2003-2004 | EOK official Cup archive | Preliminary rounds are included, so some editions legitimately contain dozens of games. The 2003-2004 page is excluded because it starts at game 15 and omits the first 14 games. |
| Greek Cup | 2009-2010 | [EOK official page](https://www.basket.gr/cup-men/kypello-andron-2009-2010/1769/) | 42 scored games after excluding five 20-0 administrative awards. |
| Greek Cup | 2015-2016 | [EOK official page](https://www.basket.gr/cup-men/kypello-andron-2015-2016/5706/) | 28 scored games; the official page includes the final and is paired with the ingested 2015-2016 A1 season. |
| Greek Cup | 2008-2009 onward, except 2009-2010 and 2015-2016 | API-Sports | Existing modern coverage. The 2019-2020 Cup is intentionally not ingested: the [official EOK page](https://www.basket.gr/cup-men/kypello-andron-2019-2020/19447/) stops at the semifinals, consistent with the COVID suspension. |

Official 20-0 administrative awards are not treated as played games. Historical
dates without a trustworthy tip-off time are stored at 12:00 UTC.

The reviewed historical production coverage is 4,028 A1 games across twenty-three
seasons and 703 Cup games across seventeen editions (including the 2009-2010
and 2015-2016 Cup repairs plus the 2015-2016 league repair). The newly ingested
seasons are intentionally recorded at the source's available game counts:
ESAKE exposes one fewer regular-season game in 1993-1994 and 1994-1995, and only
eight regular-season rounds (56 games) for 1995-1996. Those source gaps are not
filled with guessed scores.

| Season | A1 games | Greek Cup games |
| --- | ---: | ---: |
| 1986-1987 | 90 (regular season; inferred dates) | Not ingested: no league-linked Cup source |
| 1987-1988 | 90 (regular season; inferred dates) | Not ingested: no league-linked Cup source |
| 1988-1989 | 90 (regular season; inferred dates) | Not ingested: no league-linked Cup source |
| 1989-1990 | 132 (regular season; inferred dates) | Not ingested: no league-linked Cup source |
| 1990-1991 | 132 (regular season; inferred dates) | Not ingested: no league-linked Cup source |
| 1991-1992 | 132 (regular season; inferred dates) | Not ingested: no league-linked Cup source |
| 1992-1993 | 182 | 39 (two 20-0 administrative awards excluded) |
| 1993-1994 | 211 (181 regular season, 30 playoffs) | 40 (one 20-0 award excluded) |
| 1994-1995 | 216 (181 regular season, 35 playoffs) | 42 |
| 1995-1996 | 56 (regular season only; ESAKE exposes eight rounds) | 42 |
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
| 2009-2010 | 205 (API-Sports league; EOK Cup repair) | 42 (EOK; five 20-0 awards excluded) |
| 2015-2016 | 206 (182 regular season, 24 playoffs; Basketball-Reference) | 28 (EOK; one 20-0 award excluded) |

## Ranked league providers

| Rank | Source | Seasons useful for this task | Assessment | Use |
| --- | --- | --- | --- | --- |
| 1 | [ESAKE official results archive](https://www.esake.gr/el/action/EsakeResults?idchampionship=00000015&idseason=00000001&mode=1&series=undefined) | 1992-1993 through 2007-2008 (with the 1992-1993 archive supplemented for rounds 23-26) | Authoritative, dated game scores, regular-season rounds, playoff series, stable game IDs, and stable source team IDs. The 1995-1996 archive is incomplete after round 8. | Ingested provider |
| 2 | [Sport.gr archived by the Internet Archive](https://web.archive.org/web/20080528083751id_/http://archive.sport.gr/basket/hellas/a1/1-14.htm) | 1997-1998 and 1998-1999 | Complete paired regular-season round pages and dated playoff series. Easy deterministic traversal once the archived URL patterns are known. | Ingested provider |
| 3 | [Bitzenis 1996-1997 results](https://bitzenis.gr/retro/bask.htm) | 1996-1997 regular season | A single compact page contains the full 182-game regular season. Its playoff summary lacks dates, so only the dated regular season is imported. | Ingested provider |
| 4 | [Greek Wikipedia 1992-1993 results](https://el.wikipedia.org/wiki/%CE%A0%CF%81%CF%89%CF%84%CE%AC%CE%B8%CE%BB%CE%B7%CE%BC%CE%B1_%CE%BA%CE%B1%CE%BB%CE%B1%CE%B8%CE%BF%CF%83%CF%86%CE%B1%CE%AF%CF%81%CE%B9%CF%83%CE%B7%CF%82_%CE%911_%CE%B5%CE%B8%CE%BD%CE%B9%CE%BA%CE%AE%CF%82_%CE%BA%CE%B1%CF%84%CE%B7%CE%B3%CE%BF%CF%81%CE%AF%CE%B1%CF%82_%CE%B1%CE%BD%CE%B4%CF%81%CF%8E%CE%BD_1992-1993) plus the [Olympiacos official 1992-1993 schedule](https://www.olympiacosbc.gr/el/agones/ellada/programma/1992.html) | 1992-1993 rounds 23-26 | The full matrix supplies the missing scores; the official team schedule supplies dates and round numbers. Pairings are derived by reversing ESAKE rounds 10-13, making all 28 joins deterministic. | Ingested fallback |
| 5 | [Basketball-Reference Greek league schedules](https://www.basketball-reference.com/euro/greek-basket-league/2016-schedule.html) | 2015-2016 (and validation for 2001-2002 through 2007-2008) | Easy single-season schedules with dates and scores; used to repair 2015-2016, while ESAKE remains the historical competition owner. | Ingested repair / validation |
| 6 | Galanis Sports Data, RealGM, and other Wikipedia editions | Partial standings, matrices, or summaries | Useful independent checks, not better complete ingestion feeds for the cataloged seasons. | Validation only |

## Ranked Cup providers

| Rank | Source | Seasons useful for this task | Assessment | Use |
| --- | --- | --- | --- | --- |
| 1 | [EOK official men's Cup archive](https://www.basket.gr/cup-men/kypello-andron-1992-1993/1746/) | Seventeen usable reviewed editions (including 2009-2010 and 2015-2016; excluding 2003-2004 and incomplete 2019-2020) | Authoritative game-level pages exposed through the site's WordPress API, with dates, teams, scores, and page revision metadata. | Ingested provider |
| 2 | TheSports and Livesport/Flashscore archives | Shorter or later coverage | Useful for spot checks, but they do not provide a better continuous pre-2008 run. | Validation only |
| 3 | Wikipedia | Finals and edition summaries | Does not consistently contain all early-round games. | Validation only |

## Safety rules

- Every Cup season must overlap an A1 season in production; the Cup and league
  may come from different providers when a modern league source already exists.
- At runtime, `greece-ingest` queries actual A1 games and skips any Cup season
  without matching regular-league data.
- The incomplete 2003-2004 EOK Cup page is not cataloged.
- The 2019-2020 EOK Cup page is not cataloged because it ends at the semifinals; this is treated as a COVID-suspended/incomplete edition rather than a complete Cup season.
- The provider rejects tied scores and 20-0 administrative awards as unplayed
  or non-competitive records.
- Early A1 dates (1986-1987 through 1991-1992) are explicitly inferred from
  the 1992-1993 round cadence. They are stored at 12:00 UTC and are not
  source-published tip-off times; those seasons contain regular-season matrix
  games only, with no playoff records imported.
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
