# German league and Cup historical ingestion

This runbook covers issue #129: game-level ingestion of Germany's men's top
flight and German Cup through the historical source boundary. The catalog
does not claim coverage before 1975-1976.

## Production coverage

The catalog uses separate historical and modern providers while keeping one
canonical competition for each German competition:

| Competition | Season span | Provider | Notes |
| --- | --- | --- | --- |
| Basketball Bundesliga (BBL) | 1975-1976 through 2007-2008 | `german-official` | Official easyCredit BBL archive API. Regular season and postseason records are retained where exposed. Reviewed historical repairs are parser-versioned and preserve the source score. |
| Basketball Bundesliga (BBL) | 2008-2009 onward | `api-sports` | Modern provider boundary. |
| German Cup / BBL-Pokal | 1975-1976 through 2007-2008 | `wikipedia-german-cup` | Published final and final-series records from the historical BBL-Pokal overview: 45 games across 33 editions. Early-round games are not exposed by this source and are not synthesized. |
| German Cup / BBL-Pokal | 2008-2009 onward | `api-sports` | Modern provider boundary. |

The 1975-1976 BBL season is therefore the clean historical cutoff for this
project. Seasons before it remain outside the catalog until a complete,
game-level source is found. The [1975-1976 season archive](https://de.wikipedia.org/wiki/Basketball-Bundesliga_1975/76)
also provides an independent check for the German Cup final series.

## League source and repair policy

The league provider reads the [easyCredit BBL archive](https://www.easycredit-bbl.de/)
and its public results API. Source team IDs, game IDs, dates, scores, phases,
and rounds are retained as provenance. Historical sponsor names and successor
clubs are resolved to the existing German team identities using `DE` country
identity and observed aliases.

Some older archive rows are incomplete. The provider repairs only cases where
the missing team or date can be established from the same season's round,
opponent schedule, roster identity, or postseason ordering. It does not infer
scores from standings, champions lists, or aggregate records. These repairs
use explicit parser versions (`v2` through `v15`) so they remain visible in
the game's provenance and can be audited or replaced if a better source is
found.

For historical postseason rows whose source date is absent, the provider uses
deterministic dates after the regular season. The date is suitable for Elo
chronology but is not a claim about the historical tip-off time.

## German Cup source and limitations

The historical Cup provider uses the [BBL-Pokal overview](https://en.wikipedia.org/wiki/BBL-Pokal),
which publishes the historical finals and two-legged final series. It imports:

- two-legged finals from 1975-1976 through 1983-1984;
- single-game finals from 1984-1985 through 1988-1989;
- two-legged finals from 1989-1990 through 1991-1992; and
- single-game finals from 1992-1993 through 2007-2008.

Historical Cup dates are deterministic postseason placement dates at 00:00
UTC when the catalog does not contain an exact published date. The imported
records are marked `Final phase` / `Final`, and every backfill warning states
that the source does not expose the earlier rounds. This is intentionally
finals-only coverage, not a complete historical Cup bracket.

The modern API-Sports segment remains separate at the provider boundary and is
not replaced by the historical finals provider.

## Identity and ELO safety

Both competitions use the Europe-club ELO pool. Historical provider aliases
are allowed to merge into existing German team identities by normalized name
and country; source-specific IDs remain attached to the alias for auditability.
Review identity findings and coverage warnings before any rating rebuild.

Automatic ELO rebuilds are disabled by default through
`Backfill:QueueEloRebuildsAutomatically`. Backfills change game data only;
rebuild the affected ELO pool manually after the data and identity review is
complete.

## Operations

Deploy through the VPS script:

```powershell
.\deploy\vps\deploy.ps1 `
  -User ubuntu `
  -Server 152.228.139.241 `
  -IdentityFile $env:USERPROFILE\.ssh\ovh_vps-22091453
```

Verify `basket-elo-api.service`, `basket-elo-worker.service`, and
`basket-elo-web.service` after deployment. Queue historical seasons through
the internal backfill range endpoint or the admin backfill page using:

```json
{
  "provider": "wikipedia-german-cup",
  "country": "Germany",
  "leagueName": "German Cup",
  "startSeason": "1975-1976",
  "endSeason": "2007-2008",
  "onlyMissing": true,
  "replaceExisting": false,
  "dryRun": false,
  "maxRequests": 0
}
```

For the BBL historical range, use provider `german-official`, country
`Germany`, and league name `BBL`. Inspect job warnings, source provenance,
season coverage, and team identity findings before rebuilding Elo.
