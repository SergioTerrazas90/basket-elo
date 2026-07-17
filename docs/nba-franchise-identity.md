# NBA franchise identity policy

Status: accepted 2026-07-16

Basket ELO uses franchise continuity for ratings. A relocation or rename keeps
one canonical `Team`; each observed Basketball-Reference abbreviation and name
is retained as a dated `TeamAlias`. Defunct franchises remain separate inactive
teams and keep their final historical identity.

## Continuity decisions

- Minneapolis and Los Angeles Lakers are one `Los Angeles Lakers` team.
- Fort Wayne and Detroit Pistons are one `Detroit Pistons` team.
- Philadelphia, San Francisco, and Golden State Warriors are one
  `Golden State Warriors` team.
- Rochester, Cincinnati, Kansas City-Omaha, Kansas City, and Sacramento are one
  `Sacramento Kings` team.
- Seattle SuperSonics and Oklahoma City Thunder are one
  `Oklahoma City Thunder` rating chain. This follows franchise-operation
  continuity; a future product view may display Seattle-era ratings separately
  without changing game identity.
- The original Charlotte Hornets history (`CHH`) and the Bobcats/current
  Hornets history (`CHA`/`CHO`) belong to the canonical `Charlotte Hornets`,
  reflecting the NBA's reassignment of Charlotte history. New Orleans history
  begins in 2002 and belongs to the `New Orleans Pelicans` chain.
- The 1949-1950 Denver Nuggets (`DNN`) are a defunct franchise and are not the
  modern Denver Nuggets (`DEN`).
- The 1947-1954 Baltimore Bullets (`BLB`) are separate from the
  Chicago/Baltimore/Capital/Washington Wizards chain (`CHP` through `WAS`).
- The Washington Capitols (`WSC`) are a defunct franchise and are not the
  Washington Wizards.

## Ingestion behavior

`NbaFranchiseCatalog` is the seeded identity authority for
`basketball-reference` imports. It contains all 30 active franchises and the
defunct BAA/NBA teams in the historical range. On import:

1. The source abbreviation and season resolve to a franchise alias.
2. Any previously observed alias in that franchise reuses the same `TeamId`.
3. The observed name is stored with its validity range.
4. Active franchises use their current canonical name; defunct franchises are
   created with `IsActive = false`.
5. Unknown abbreviations use the generic provider behavior and remain visible
   to identity health checks instead of being guessed into a franchise.

## Product presentation

- NBA rankings default to the 30 active franchises. The historical scope adds
  defunct BAA/NBA franchises without changing any rating chains.
- Franchise identity events are explicit catalog data. They are not inferred
  from aliases, because aliases can also represent source-code changes, league
  transitions, or temporary identities.
- Rankings stay focused on rating and active/defunct status. Team profiles show
  the full curated chronology of relocations, temporary moves, and renames.
- Event years use the starting year of the new identity's season. For example,
  the Lakers' 1960 event represents the 1960-1961 move from Minneapolis to Los
  Angeles.
- The Washington timeline explicitly records the 1997 rename from the
  Washington Bullets to the Washington Wizards.

Changes to continuity require an update to this document, the catalog, and the
identity regression tests in the same pull request.
