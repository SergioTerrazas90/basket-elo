# FIBA European Tier 2 ingestion

The catalog exposes the post-Saporta FIBA men's second-tier lineage as a
separate competition from the Saporta Cup, KoraÄ‡ Cup, ULEB Cup, and modern
EuroCup:

- source: `fiba`
- competition: `FIBA European Tier 2`
- application seasons: `2002-2003` through `2007-2008` (six editions)
- ELO pool: `EuropeClubs`
- official archive family: `FIBA Men's European Club Competitions - Tier 2`

The historical names vary by edition: FIBA Europe Regional Challenge Cup,
FIBA Europe League, FIBA EuroCup, and EuroCup Challenge. The provider keeps
these editions under one application competition while preserving the
published phase and round. It uses the official FIBA edition rows and game
pages, including multiple edition rows when an archive year contains regional
or parallel branches.

This mapping is intentionally separate from the KoraÄ‡ provider. FIBA's archive
uses overlapping historical-family URLs, so the post-Saporta variant must not
fall back to Champions Cup or KoraÄ‡ Wikipedia pages when an official edition
is sparse.

Official archive: [FIBA Men's European Club Competitions - Tier 2](https://www.fiba.basketball/en/history/212-fiba-mens-european-club-competitions-tier-2).
