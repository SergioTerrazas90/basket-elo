# Country-code policy

Country codes persisted in `teams`, `competitions`, identity-review records, current-result reviews, and model-lab scopes use ISO 3166-1 alpha-2 for current countries. For example, Italy is stored and filtered as `IT`; provider aliases such as `ITA` are normalized at ingestion and are never offered as a second review option.

The shared policy is implemented by `CountryCodeCatalog`. It is used by backfill ingestion, current-results matching, identity review, game and Elo filters, coverage, and model-lab labels.

Historical national identities remain distinct codes when changing them to a modern country would alter the meaning of the record. Examples include `YUG` (Yugoslavia), `URS` (Soviet Union), `DDR`/`GDR` (East Germany), `FRG` (West Germany), and `TCH` (Czechoslovakia). Constituent nations such as England, Scotland, and Wales are also not silently collapsed into `GB`.

The normalization migration updates existing country-code fields without touching games, ratings, or ELO rebuild state.
