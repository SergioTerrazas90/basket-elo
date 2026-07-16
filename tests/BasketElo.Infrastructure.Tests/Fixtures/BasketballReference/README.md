# Basketball-Reference parser fixtures

These files are reduced synthetic fixtures. They reproduce the schedule-table
shape and representative public facts needed for parser regression tests; they
are not copies of full source pages and must not be used as a game dataset.

The fixture matrix covers:

| Season | Reason |
| --- | --- |
| `1946-1947` | Inaugural BAA season |
| `1949-1950` | First NBA-branded season |
| `1965-1966` | 1960s markup and franchise names |
| `1998-1999` | Lockout-shortened season |
| `2011-2012` | Lockout-shortened season |
| `2019-2020` | Interrupted and neutral-site restart season |
| `2025-2026` | Latest completed season when the matrix was updated |

When parser behavior changes, update only the smallest HTML fragment needed to
represent the new shape. Add or update the matching expectation in
`NbaHistoricalFixtureCoverageTests`; do not replace these files with downloaded
pages unless the repository has explicit redistribution permission.
