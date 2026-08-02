# Turkish historical ingestion

The historical Turkish men's top-flight catalog uses the `turkish-historical`
provider for 1966-1967 through 2015-2016. The primary source is
[TBLStat.net](https://bsl.tblstat.net/), whose season index exposes the
competition phases and whose team-season pages expose dated results. The
provider fetches the season index once and one team-season page per club, then
deduplicates the two observations of each game.

The first reviewed complete game-level season is 1966-1967. TBLStat exposes
dated regular-season rows for that season and the later historical seasons,
including playoff rows where the season had a postseason. Older rows without a
TBLStat game link receive a deterministic synthetic source ID based on season,
date, teams, and score; the source team-season URL remains in provenance.

The historical catalog also includes:

- `Turkish Cup`, 1966-1967 through 1972-1973 and 1991-1992 through
  2010-2011. The source is the published [Turkish Basketball Cup
  overview](https://en.wikipedia.org/wiki/Turkish_Basketball_Cup). It exposes
  final/final-series records, not complete early-round brackets, so only those
  final records are imported and every job carries a coverage warning.
- `Super Cup`, editions 1985 through 2010. The source is the [Turkish
  Presidential Cup overview](https://en.wikipedia.org/wiki/Turkish_Basketball_Presidential_Cup).
  It exposes the final record only. The source lists the champion first and
  does not publish exact dates or venue order in the historical table, so the
  provider records deterministic dates and retains the listed order as
  home/away with explicit warnings.

Coverage boundaries are:

| Competition | Historical coverage | Modern coverage | Notes |
| --- | --- | --- | --- |
| Super Ligi | 1966-1967 through 2015-2016 | API-Sports 2016-2017 onward | TBLStat provides dated league and playoff games. |
| Turkish Cup | 1966-1967 through 1972-1973 and 1991-1992 through 2010-2011 | API-Sports 2011-2012 onward | Historical rows are finals/final-series only. The 2020-2021 edition was cancelled. |
| Super Cup | 1985 through 2010 | API-Sports 2011 onward | Historical rows are final-only records with deterministic dates. |

For the historical Turkish Cup, a single-game final produces one imported game;
the 1966-1967, 1967-1968, 1972-1973 editions expose two-leg finals and produce
two games. A missing early-round game is therefore a source-coverage limitation,
not an incomplete worker traversal.

The modern API-Sports catalog remains separate. The historical league source
fills the 2008-2009 through 2015-2016 gap before API-Sports coverage begins;
the historical Cup source fills 2008-2009 through 2010-2011, and the historical
Super Cup source fills 2009 and 2010. Do not treat the historical Cup rows as
full-tournament coverage or combine the Super Cup's single-year labels with
league season labels when checking coverage.

Operationally, run one Turkish season at a time while validating the provider.
Use unlimited requests (`maxRequests=0`) for a real TBLStat season; the season
index plus team pages requires more than the default diagnostic budget. Review
the returned unique-game count, warnings, identity findings, and then rebuild
the affected Turkish club ELO pool.
