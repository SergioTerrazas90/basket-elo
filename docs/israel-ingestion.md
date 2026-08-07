# Israeli basketball historical ingestion

The official Israeli Basketball Super League archive at
[`basket.co.il/results.asp`](https://basket.co.il/results.asp?cYear=1954&lang=en)
provides a season selector and a competition (`Board`) selector. The historical
provider is `official-israel-basket`.

## Stage mapping

The provider reads the Board options exposed for each season rather than
assuming that the modern stage IDs existed in earlier years:

| Official board family | BasketELO handling |
| --- | --- |
| Winner League | Israel Super League, regular-season phase |
| Winner League playoff/final/relegation boards | Same Israel Super League competition, playoff phase |
| Winner Cup / Winner League Cup | Separate Israel Cup competition |
| Supercup | Separate Israel Super Cup competition |

The provider fetches the default Winner League page and then requests each
additional board through `results.asp?Board=...&RoundNumber=0&TeamId=0`. It
preserves the official `GameId`, `TeamId`, board label, source URL, and source
season on every imported result. A board is not inferred from a standings page.

## Historical cutoff and known archive gaps

The clean historical league cutoff is 1953-1954, the earliest season exposed
by the official season selector. The catalog covers 1953-1954 through
2007-2008; the existing API-Sports segment begins at 2008-2009. The official
selector does not expose 1955-1956 or 1974-1975, so those two seasons are
explicitly excluded instead of being represented as empty successful imports.

The archive exposes Winner Cup boards for 2006-2007 and 2007-2008 in the
historical range. Earlier historical Cup seasons are not claimed from this
page. The pre-2008 pages checked did not expose a Supercup board; the provider
supports it when the archive exposes one, but no pre-2008 Super Cup coverage is
cataloged from this source.

## Operations

Use `maxRequests=0`. A league season normally needs one request for the season
page plus one request per additional playoff board. Validate coverage by season,
board phase, warning count, and the database's actual game count. This provider
does not queue or execute ELO rebuilds.

## VPS verification

The completed historical import contains 7,725 Super League games and 16
Winner Cup games, for 7,741 official-source games total. Every job completed
without provider warnings. The coverage inspector flags 1953-1954 (55 games)
and 1956-1957 (88 games) for review because those seasons are smaller than the
archive's later-season median; they are not parser failures.

| Season | Super League games |
| --- | ---: |
| 1953-1954 | 55 |
| 1954-1955 | 129 |
| 1956-1957 | 88 |
| 1957-1958 | 130 |
| 1958-1959 | 130 |
| 1959-1960 | 131 |
| 1960-1961 | 130 |
| 1961-1962 | 132 |
| 1962-1963 | 130 |
| 1963-1964 | 155 |
| 1964-1965 | 169 |
| 1965-1966 | 182 |
| 1966-1967 | 169 |
| 1967-1968 | 182 |
| 1968-1969 | 156 |
| 1969-1970 | 132 |
| 1970-1971 | 132 |
| 1971-1972 | 132 |
| 1972-1973 | 132 |
| 1973-1974 | 132 |
| 1975-1976 | 156 |
| 1976-1977 | 110 |
| 1977-1978 | 162 |
| 1978-1979 | 150 |
| 1979-1980 | 110 |
| 1980-1981 | 132 |
| 1981-1982 | 163 |
| 1982-1983 | 149 |
| 1983-1984 | 151 |
| 1984-1985 | 151 |
| 1985-1986 | 129 |
| 1986-1987 | 151 |
| 1987-1988 | 151 |
| 1988-1989 | 142 |
| 1989-1990 | 122 |
| 1990-1991 | 120 |
| 1991-1992 | 144 |
| 1992-1993 | 196 |
| 1993-1994 | 193 |
| 1994-1995 | 192 |
| 1995-1996 | 142 |
| 1996-1997 | 120 |
| 1997-1998 | 158 |
| 1998-1999 | 152 |
| 1999-2000 | 132 |
| 2000-2001 | 234 |
| 2001-2002 | 144 |
| 2002-2003 | 172 |
| 2003-2004 | 160 |
| 2004-2005 | 163 |
| 2005-2006 | 169 |
| 2006-2007 | 139 |
| 2007-2008 | 138 |

The Winner Cup contributes 8 games in both 2006-2007 and 2007-2008. No
pre-2008 Supercup board was exposed by the official selector.

The provider preserves the official date text verbatim. A small number of
early archive rows have dates that do not align with their selected season
(for example, several 1957-1958 records are dated May 1968 on the official
page). Those records are imported with their source dates and should be
reviewed before any future historical ELO rebuild; they are source-quality
findings, not silently corrected values.
