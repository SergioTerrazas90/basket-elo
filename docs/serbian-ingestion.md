# Yugoslav / Serbia and Montenegro / Serbia top-flight ingestion

The historical Serbian-area top-flight provider is `serbian-historical`. It
imports **1973-1974 through 2007-2008** into one `First League` competition.
The provider combines SerbianSport round pages, Pearl Basket's dated Yugoslav
archive, and historical Wikipedia score matrices while retaining source and
round metadata in each game.

The provider normalizes sponsor-era names such as Partizan Mobtel, NIS
Vojvodina, and FMP Železnik to stable club identities. Serbian clubs receive
`RS`; Budućnost, Lovćen, Primorka, and Mornar receive `ME`. Historical
Yugoslav, Serbia and Montenegro, and Serbia national identities remain
separate in the existing international-team catalog; this provider is for
clubs, not national-team games.

The 1973-1974 through 1999-2000 Yugoslav Cups are configured separately as
`Yugoslav Cup`, so cup games do not enter the First League ELO pool. The
available archive contains Partizan's documented route for each season; these
are not complete tournament reconstructions.

## Coverage

| Season range | Status | Source |
| --- | --- | --- |
| 2000-2001 through 2007-2008 | Configured game-level import | SerbianSport round pages |
| 1991-1992 | Verified regular season plus partial playoffs | Wikipedia's 132-result matrix, five dated Partizanopedia playoff games, and two Borba-verified Crvena zvezda–Rabotnički semifinal games (21 and 24 April 1992); the deciding semifinal score remains unresolved. The separate Yugoslav Cup has five Partizan-route games |
| 1992-1993 | 209 imported regular-season results plus documented playoffs and cup route | Serbian Wikipedia's round-by-round tables provide 206 scored regular-season games; three additional non-duplicate regular-season results come from Partizanopedia. The page still has 22 blank scheduled cells before that supplement. Partizanopedia supplies the dated club-level playoffs, while the separate Yugoslav Cup has five Partizan-route games |
| 1993-1994 | 180 league-stage results plus 10 documented Partizan playoff games | Wikipedia's first- and second-stage matrix, plus Partizanopedia's dated league and playoff route; 12 league-stage results and 21 non-Partizan playoff games remain unresolved |
| 1994-1995 | 164 league-stage results plus 9 documented Partizan playoff games | Reviewed Borba OCR rows plus Partizanopedia's dated league and playoff route; 284 league-stage results and 19 bracket games remain unresolved |
| 1995-1996 | 230 league-stage results; 14 unresolved against benchmark | Wikipedia matrix plus filtered Partizanopedia league schedule |
| 1996-1997 | 150 regular-season results; 32 unresolved against benchmark | Wikipedia matrix |
| 1997-1998 | 174 regular-season results; 8 unresolved against benchmark | Wikipedia matrix |
| 1998-1999 | 101 regular-season results; 31 unresolved against benchmark | Wikipedia matrix |
| 1999-2000 | 130 regular-season results; 2 unresolved against benchmark | Wikipedia matrix |
| 1973-1974 through 1980-1981 | Configured regular-season import | Pearl Basket dated round pages |
| 1981-1982 through 1990-1991 | Phase-reconciled historical import | Pearl Basket dated pages; explicit playoff, play-out, classification, and Stage I headings are now preserved |
| 1973-1974 through 1999-2000 | Partial cup-route import | Partizanopedia's documented Partizan cup route; complete tournament coverage is unavailable |
| Before 1973-1974 | Source gap | Not included by the agreed cutoff |

The source does not publish a date for every older regular-season game. When a
source `startDate` is absent, the provider assigns a deterministic round date
and records a warning. Those seasons require coverage review before being used
as an authoritative ELO baseline.

## Competition-season game-gap ledger

The table below records the latest game-level coverage. `Imported` is the
current number of rows in Postgres after source-identity deduplication.
`Expected benchmark` is the published regular-season or stage total used for
reconciliation. `Missing` is the remaining gap against that benchmark, not a
claim that an unlisted game definitely took place.

`Unknown` is intentional: the available source does not expose a reliable
complete total for that competition. Pearl Basket's 1981-1982 through
1990-1991 headings are now split into regular season, playoffs, play-out,
classification, and Stage I where the source provides those labels, but the
changing historical formats still need independent federation-level
benchmarks. The two Yugoslav Cup imports contain only Partizan's documented
route.

| Competition | Season | Imported | Expected benchmark | Missing | Reconciliation note |
| --- | --- | ---: | ---: | ---: | --- |
| First League | 1973-1974 | 182 | 182 | 0 | 14-team regular-season benchmark |
| First League | 1974-1975 | 181 | 182 | 1 | 14-team regular-season benchmark |
| First League | 1975-1976 | 182 | 182 | 0 | 14-team regular-season benchmark |
| First League | 1976-1977 | 182 | 182 | 0 | 14-team regular-season benchmark |
| First League | 1977-1978 | 179 | 182 | 3 | 14-team regular-season benchmark |
| First League | 1978-1979 | 131 | 132 | 1 | 12-team regular-season benchmark |
| First League | 1979-1980 | 130 | 132 | 2 | 12-team regular-season benchmark |
| First League | 1980-1981 | 132 | 132 | 0 | 12-team regular-season benchmark |
| First League - regular season | 1981-1982 | 134 | — | Unknown | 22 dated Pearl Basket rounds; the changing historical format needs an independent benchmark |
| First League - playoffs | 1981-1982 | 18 | — | Unknown | Pearl Basket labels quarterfinals, semifinals, and finals; full bracket benchmark not independently verified |
| First League - regular season | 1982-1983 | 134 | — | Unknown | Dated Pearl Basket rounds; two source classification rows were not accepted as complete game-level records |
| First League - playoffs | 1982-1983 | 19 | — | Unknown | Pearl Basket playoff headings preserved; full bracket benchmark not independently verified |
| First League - regular season | 1983-1984 | 134 | — | Unknown | Dated Pearl Basket rounds; changing historical format needs an external benchmark |
| First League - playoffs | 1983-1984 | 18 | — | Unknown | Pearl Basket playoff headings preserved; full bracket benchmark not independently verified |
| First League - regular season | 1984-1985 | 131 | — | Unknown | Dated Pearl Basket rounds; changing historical format needs an external benchmark |
| First League - playoffs | 1984-1985 | 28 | — | Unknown | Pearl Basket includes 1/8-finals through finals; full bracket benchmark not independently verified |
| First League - regular season | 1985-1986 | 132 | — | Unknown | Dated Pearl Basket rounds; changing historical format needs an external benchmark |
| First League - playoffs | 1985-1986 | 25 | — | Unknown | Pearl Basket includes 1/8-finals through finals; full bracket benchmark not independently verified |
| First League - regular season | 1986-1987 | 131 | — | Unknown | Dated Pearl Basket rounds; changing historical format needs an external benchmark |
| First League - playoffs | 1986-1987 | 28 | — | Unknown | Pearl Basket includes 1/8-finals through finals; full bracket benchmark not independently verified |
| First League - regular season | 1987-1988 | 131 | — | Unknown | Dated Pearl Basket rounds; changing historical format needs an external benchmark |
| First League - playoffs | 1987-1988 | 28 | — | Unknown | Pearl Basket includes 1/8-finals through finals; full bracket benchmark not independently verified |
| First League - regular season | 1988-1989 | 132 | — | Unknown | Dated Pearl Basket rounds; changing historical format needs an external benchmark |
| First League - playoffs | 1988-1989 | 8 | — | Unknown | Pearl Basket playoff headings preserved; full bracket benchmark not independently verified |
| First League - regular season | 1989-1990 | 132 | — | Unknown | Dated Pearl Basket rounds; changing historical format needs an external benchmark |
| First League - playoffs | 1989-1990 | 10 | — | Unknown | Pearl Basket playoff headings preserved; full bracket benchmark not independently verified |
| First League - Stage I | 1990-1991 | 130 | 132 | 2 | Two Stage I score lines were not accepted by the parser; the source page exposes 132 Stage I entries |
| First League - playoffs | 1990-1991 | 9 | — | Unknown | Pearl Basket labels Play off and Final sections; full benchmark not independently verified |
| First League - play-out | 1990-1991 | 17 | — | Unknown | Pearl Basket play-out section; full benchmark not independently verified |
| First League - classification | 1990-1991 | 13 | — | Unknown | Pearl Basket 5/8 classification section; full benchmark not independently verified |
| First League — regular season | 1991-1992 | 132 | 132 | 0 | Wikipedia matrix |
| First League — playoffs | 1991-1992 | 7 | 8 | 1 | Deciding Crvena zvezda–Rabotnički semifinal game remains unresolved |
| Yugoslav Cup | 1973-1974 | 4 | — | Unknown | Partizan route only; complete cup total unavailable |
| Yugoslav Cup | 1974-1975 | 3 | — | Unknown | Partizan route only; complete cup total unavailable |
| Yugoslav Cup | 1975-1976 | 4 | — | Unknown | Partizan route only; complete cup total unavailable |
| Yugoslav Cup | 1976-1977 | 7 | — | Unknown | Partizan route only; complete cup total unavailable |
| Yugoslav Cup | 1977-1978 | 2 | — | Unknown | Partizan route only; complete cup total unavailable |
| Yugoslav Cup | 1978-1979 | 6 | — | Unknown | Partizan route only; complete cup total unavailable |
| Yugoslav Cup | 1979-1980 | 3 | — | Unknown | Partizan route only; complete cup total unavailable |
| Yugoslav Cup | 1980-1981 | 4 | — | Unknown | Partizan route only; complete cup total unavailable |
| Yugoslav Cup | 1981-1982 | 3 | — | Unknown | Partizan route only; complete cup total unavailable |
| Yugoslav Cup | 1982-1983 | 8 | — | Unknown | Partizan route only; complete cup total unavailable |
| Yugoslav Cup | 1983-1984 | 6 | — | Unknown | Partizan route only; complete cup total unavailable |
| Yugoslav Cup | 1984-1985 | 2 | — | Unknown | Partizan route only; complete cup total unavailable |
| Yugoslav Cup | 1985-1986 | 15 | — | Unknown | Partizan route only; complete cup total unavailable |
| Yugoslav Cup | 1986-1987 | 13 | — | Unknown | Partizan route only; complete cup total unavailable |
| Yugoslav Cup | 1987-1988 | 24 | — | Unknown | Partizan route only; complete cup total unavailable |
| Yugoslav Cup | 1988-1989 | 18 | — | Unknown | Partizan route only; complete cup total unavailable |
| Yugoslav Cup | 1989-1990 | 11 | — | Unknown | Partizan route only; complete cup total unavailable |
| Yugoslav Cup | 1990-1991 | 4 | — | Unknown | Partizan route only; complete cup total unavailable |
| Yugoslav Cup | 1991-1992 | 5 | — | Unknown | Partizan route only; complete cup total unavailable |
| First League — regular season | 1992-1993 | 209 | 228 | 19 | 206 Serbian-Wikipedia scores plus 3 non-duplicate Partizanopedia results |
| First League — playoffs | 1992-1993 | 7 | 7 | 0 | Dated Partizanopedia playoff route |
| Yugoslav Cup | 1992-1993 | 5 | — | Unknown | Partizan route only; complete cup total unavailable |
| Yugoslav Cup | 1993-1994 | 6 | — | Unknown | Partizan route only; complete cup total unavailable |
| Yugoslav Cup | 1994-1995 | 4 | — | Unknown | Partizan route only; complete cup total unavailable |
| Yugoslav Cup | 1995-1996 | 6 | — | Unknown | Partizan route only; complete cup total unavailable |
| Yugoslav Cup | 1996-1997 | 6 | — | Unknown | Partizan route only; complete cup total unavailable |
| Yugoslav Cup | 1997-1998 | 5 | — | Unknown | Partizan route only; complete cup total unavailable |
| Yugoslav Cup | 1998-1999 | 6 | — | Unknown | Partizan route only; complete cup total unavailable |
| Yugoslav Cup | 1999-2000 | 4 | — | Unknown | Partizan route only; complete cup total unavailable |
| First League — league stages | 1993-1994 | 180 | 192 | 12 | 132 first-stage games plus 60 Blue/White-stage games; 178 matrix scores plus 2 non-duplicate Partizanopedia league results |
| First League — playoffs | 1993-1994 | 10 | 31 | 21 | Partizanopedia documents Partizan's quarterfinal, semifinal, and final route; the other 21 bracket games are not yet available as game-level scores |
| First League — league stages | 1994-1995 | 164 | 448 | 284 | 136 reviewed Borba OCR results plus 28 dated Partizanopedia league results; the published 32-team, 28-games-per-team league-stage benchmark includes the first and second rounds, while full game-level coverage is not available |
| First League — playoffs | 1994-1995 | 9 | 28 | 19 | Partizanopedia documents Partizan's two quarterfinals, two semifinals, and five finals; the remaining first-round and quarterfinal games are not yet available as game-level scores |
| First League - regular season | 1995-1996 | 230 | 244 | 14 | 213 Wikipedia matrix results plus 17 non-duplicate Partizanopedia results; 14 league-stage results remain unresolved |
| First League — regular season | 1996-1997 | 150 | 182 | 32 | 14 teams, 26 games each |
| First League — regular season | 1997-1998 | 174 | 182 | 8 | Matrix benchmark |
| First League — regular season | 1998-1999 | 101 | 132 | 31 | Matrix benchmark |
| First League — regular season | 1999-2000 | 130 | 132 | 2 | Matrix benchmark |
| First League — all configured phases | 2000-2001 | 153 | 153 | 0 | 132 regular season + 21 playoffs |
| First League — all configured phases | 2001-2002 | 154 | 154 | 0 | 132 regular season + 22 playoffs |
| First League — all configured phases | 2002-2003 | 151 | 151 | 0 | 132 regular season + 19 playoffs |
| First League — all configured phases | 2003-2004 | 196 | 196 | 0 | 132 regular season + 56 Super League + 8 playoffs |
| First League — all configured phases | 2004-2005 | 246 | 246 | 0 | 182 regular season + 56 Super League + 8 playoffs |
| First League — all configured phases | 2005-2006 | 204 | 204 | 0 | 132 regular season + 60 Super League + 12 playoffs |
| First League — all configured phases | 2006-2007 | 196 | 196 | 0 | 132 regular season + 56 Super League + 8 playoffs |
| First League — all configured phases | 2007-2008 | 197 | 197 | 0 | 132 regular season + 56 Super League + 9 playoffs |

The 1995-1996 row combines the 213 scored Wikipedia matrix entries with 17
non-duplicate Partizanopedia league-schedule entries. It remains 14 short of
the current 244-game benchmark and should not yet be treated as proof that
every league-stage game is present.

## VPS commands

After deploying with `deploy/vps/deploy.ps1`, run a single-season dry run first:

```bash
/opt/basket-elo/releases/tools/BasketElo.Tools serbia-dry-run \
  --competition "First League" \
  --season 2007-2008 \
  --max-requests 0 \
  --interval-ms 250
```

Then queue the reviewed range for ingestion:

```bash
/opt/basket-elo/releases/tools/BasketElo.Tools serbia-ingest \
  --start 2007-2008 \
  --end 2000-2001 \
  --max-requests 0 \
  --interval-ms 250 \
  --connection-string "$ConnectionStrings__Postgres"
```

Run one season at a time while validating counts, warnings, source URLs,
identity findings, and the affected European-club ELO rebuild.
