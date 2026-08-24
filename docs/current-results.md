# Current results and upcoming schedule

The current-results worker is configured in `src/BasketElo.Worker/appsettings.json`. The repository configuration now enables the Livescore daily run; keep it disabled if source-use approval or licensing is not in place.

When enabled, the daily run reads:

- yesterday, to reconcile final scores that were previously scheduled;
- today; and
- the next seven days, so upcoming games are already available before game day.

The same source game is upserted by `(Source, SourceGameId)`. Manual result edits are preserved. Competition identity is resolved before team identity. Competitions have an explicit current-results support policy: supported competitions continue through team resolution, while unsupported competitions are skipped without creating games, team reviews, or ELO work. Unknown or ambiguous competitions are stored as competition-only items in `current_result_reviews` and exposed through `GET /api/current-results/reviews/unmatched-competitions`.

Administrators manage canonical competitions at `/admin/competitions`. The page supports creating competitions, changing their current-results policy, and adding or removing source-specific aliases with an optional source competition ID. From `/admin/current-results-review`, an unmatched source competition can be merged into an existing supported competition or create a new supported competition, with an optional tournament-cycle assignment. For FIBA and Olympic families, the merge form can reuse an existing cycle or create one such as `worldcup-2031` or `olympics-2028`; the assignment is stored on the detected source reviews and does not make the cycle global to the competition. The merge saves the alias for future runs. An unmatched source can also be marked unsupported and inactive, which creates the persistent skip policy, preserves the alias for future recognition, and ignores its existing reviews.

After the complete date range has been ingested, changed Elo pools are identity-checked and the three configured rulesets are queued once per pool. The worker then processes those rebuild jobs. This avoids rebuilding once per game while still rerunning ratings after the daily batch.

`GET /api/games/upcoming` returns the next schedule with current team ratings, Elo difference, and an optional `minElo` threshold. The admin UI is available at `/upcoming`.

## Cross-source reconciliation

When a Livescore candidate has a confident competition and team mapping, ingestion first checks for an existing scheduled game from another source. It reconciles only when the competition, home team, away team, and fixture time identify one planned game within a 36-hour window. If more than one planned game is equally close, it is not reconciled by guesswork and follows the normal Livescore upsert path.

This allows daily Livescore results to complete planned FIBA fixtures without creating a second row. The existing canonical source, source game ID, and source provenance are preserved; Livescore supplies the current score and status. Games with no safe planned match remain Livescore rows, subject to the normal competition/team review rules.

If the candidate matches multiple scheduled fixtures equally closely, ingestion opens a `current_result_reviews` record instead of inserting a Livescore row. Admins can handle these cases at `/admin/current-results-review`: choose the canonical planned game, apply the score/status, and queue Elo rebuilds, or ignore the candidate. The selected game ID is retained so future daily runs continue updating the same canonical row.

The reverse path is also protected: if Livescore created a current planned row first and an official backfill fixture arrives later, the backfill reuses that row when there is exactly one matching Livescore fixture within the same 36-hour window. It promotes the row to the official source identity and does not overwrite an existing finished result with a later scheduled placeholder.

For a recognized tournament family with no confirmed cycle, current-results ingestion stores the game with `EloEligible = false`, opens a `tournament_cycle_confirmation_required` review, and does not queue an Elo rebuild. The review page keeps it visible until the official tournament-cycle ingestion confirms the cycle; assigning a fixture manually cannot bypass this gate.

ELO is fail-closed for chronology: before a rebuild starts deleting or recomputing ratings, it scans the affected pool for any `finished` game with `EloEligible = false`. If one exists, the run is marked `blocked`, existing ratings are left untouched, and no later game in that pool is rated. The run notes identify the blocking game and exclusion reason. Once the game is corrected or the tournament cycle is confirmed, a new rebuild can be queued (or the blocked run can be retried); unrelated Elo pools remain independent.

The Livescore provider is an opt-in adapter. Enable it only after confirming that the intended use is permitted by the source’s current terms and any required license. The adapter uses the public daily HTML page and does not call hidden APIs.
