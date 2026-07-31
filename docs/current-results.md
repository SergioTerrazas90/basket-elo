# Current results and upcoming schedule

The current-results worker is configured in `src/BasketElo.Worker/appsettings.json` and is disabled by default.

When enabled, the daily run reads:

- yesterday, to reconcile final scores that were previously scheduled;
- today; and
- the next seven days, so upcoming games are already available before game day.

The same source game is upserted by `(Source, SourceGameId)`. Manual result edits are preserved. A candidate is only written to `games` when its competition and both teams can be assigned confidently. Unsupported or ambiguous candidates are stored in `current_result_reviews` and exposed through `GET /api/current-results/reviews`.

After the complete date range has been ingested, changed Elo pools are identity-checked and the three configured rulesets are queued once per pool. The worker then processes those rebuild jobs. This avoids rebuilding once per game while still rerunning ratings after the daily batch.

`GET /api/games/upcoming` returns the next schedule with current team ratings, Elo difference, and an optional `minElo` threshold. The admin UI is available at `/upcoming`.

The Livescore provider is an opt-in adapter. Enable it only after confirming that the intended use is permitted by the source’s current terms and any required license. The adapter uses the public daily HTML page and does not call hidden APIs.
