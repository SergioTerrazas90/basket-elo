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

