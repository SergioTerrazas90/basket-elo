# NBA source policy

Status: accepted 2026-07-16

## Coverage target

Basket ELO treats the BAA and NBA as one canonical `NBA` competition. The target
range starts with `1946-1947` and continues through the active NBA season. The
first three seasons keep their BAA source labels only as provenance; they do not
create a separate canonical competition.

Pre-1946 NBL, ABL, and other predecessor-league games are out of scope for the
NBA import. They may be considered later as separate competitions and must not
be silently joined to the NBA rating history.

## Source decision

| Source | Role | Decision and limitations |
| --- | --- | --- |
| Operator-supplied licensed or otherwise authorized archive | Primary historical source | Required for the complete `1946-1947`-present load. The operator must record the supplier, permitted use, attribution, revision, and whether raw files may be retained before enabling an import. |
| API-Sports Basketball | Current-season and historical fallback | The league coverage endpoint reported game coverage from `2008-2009` through `2025-2026` when verified on 2026-07-16. Coverage is not assumed before 2008 or after the verified range. Respect account quotas and response rate-limit headers. Its terms state that data is provided as-is and that publication rights remain the user's responsibility. |
| Basketball-Reference | Manual validation and authorized offline input only | Its schedule pages visibly cover the inaugural `BAA_1947` season, but Sports Reference's data-use policy says not to create tools from scraped site data without permission and documents bot limiting. Production HTTP fetching is disabled unless the operator records express permission. An offline parser may process files the operator is authorized to use. |
| NBA.com / NBA Stats | Manual validation only | NBA Stats says its data is for viewing on NBA.com and is not available for download. NBA.com's terms also restrict comprehensive, regularly updated statistical databases without consent. It is not a production ingestion source. |
| Manual correction | Last-resort override | A correction must retain who supplied it, when, why, and which imported record it supersedes. It must never erase source provenance. |

This policy deliberately separates parser capability from permission to fetch or
retain data. A parser existing in the repository is not authorization to collect
data from a site.

## Canonical precedence

When two authorized sources contain the same game, Basket ELO identifies the
game by source first and reconciles it to the canonical competition, season,
teams, and tip-off time. For game status and final score, precedence is:

1. An operator-approved licensed historical archive.
2. API-Sports for a covered season.
3. A documented manual correction.

Conflicts are reported for review. Lower-precedence data does not silently
replace a completed game from a higher-precedence source.

## Storage and attribution

- Persist normalized game provenance: source key, source game ID, source season
  key, source URL or archive identifier, fetch/import timestamp, and parser
  version.
- Do not retain raw HTML or API payloads by default. Raw content may be retained
  only when the source authorization explicitly permits it and the storage
  location, retention period, and access controls are configured.
- Parser test fixtures must be synthetic, reduced to the minimum structure
  needed for a regression test, or supplied under terms that allow committing
  them to the repository.
- User-facing displays must include any attribution required by the selected
  source agreement. API-Sports and NBA.com attribution requirements must be
  reviewed again before public NBA data is enabled.

## Request controls

- Basketball-Reference production requests: zero unless express written
  permission is recorded in deployment configuration.
- API-Sports: use the lower of the account quota, response rate-limit headers,
  and the application's configured provider limit. Retry only transient failures
  with bounded backoff.
- API-Sports league 12 includes preseason and non-franchise exhibition records.
  NBA imports admit only the 30 reviewed franchise IDs on or after each season's
  regular-season opener; filtered counts remain in the job summary.
- Licensed archives: follow the supplier's delivery and refresh contract. Local
  files do not bypass retention or redistribution limits.

## Production gate

Before an NBA historical import can write to production, the operator must:

1. Record the authorized source and agreement owner.
2. Confirm that game-level storage, derived ratings, and public display are
   permitted.
3. Record required attribution and raw-file retention rules.
4. Verify the source covers every requested season or document the gaps.
5. Complete a dry-run audit and review conflicts, missing scores, and parser
   warnings.

## References

- [Sports Reference data-use policy](https://www.sports-reference.com/data_use.html)
- [Sports Reference bot-traffic guidance](https://www.sports-reference.com/bot-traffic.html)
- [1946-47 BAA schedule page](https://www.basketball-reference.com/leagues/BAA_1947_games.html)
- [NBA Stats FAQ](https://www.nba.com/stats/help/faq)
- [NBA.com terms of use](https://www.nba.com/termsofuse)
- [API-Sports Basketball documentation](https://api-sports.io/documentation/basketball/v1)
- [API-Sports Basketball terms](https://www.api-basketball.com/terms)
