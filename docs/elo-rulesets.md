# ELO Rulesets

Basket ELO stores ratings by ruleset version from the start, so a new formula can be rebuilt without deleting or corrupting ratings produced by an older formula.

## Ruleset Names

Ruleset names follow short model-style slugs:

- `basic-elo-v1`: plain win/loss ELO.
- `point-margin-elo-v1`: legacy ELO with a point-margin adjustment.
- `adjusted-v1`: default public ELO with point-margin adjustment and issue #8 constants.

`adjusted-v1` is the default public ruleset.

## Adjusted V1 Constants

`adjusted-v1` uses:

- Base rating: `1500`
- K-factor: `20`
- Home advantage: `70` ELO points
- Points per ELO margin: `28`
- Competition weight: `1.0`
- Margin dampener factor: `5`
- Max margin multiplier: `1.5`
- Min margin multiplier: `0.6667`

The home advantage value comes from the accepted `adjusted-v1` ruleset contract. It is worth `2.5` expected points with the `28` points-per-ELO-margin conversion.

## Legacy Constants

`basic-elo-v1` and `point-margin-elo-v1` preserve the original day-one home advantage of `100` ELO points so historical runs can coexist with `adjusted-v1` without silent formula drift.

Both legacy rulesets use:

- Base rating: `1500`
- K-factor: `20`
- Home advantage: `100` ELO points
- Competition weight: `1.0`

The home advantage value is intentionally aligned with FiveThirtyEight's published NBA ELO methodology, where the home team receives a constant 100-point ELO adjustment.

## Point-Margin Conversion

`adjusted-v1` and `point-margin-elo-v1` convert ELO difference into expected point margin with:

```text
expectedMargin = eloDiff / 28
```

We chose `28` because FiveThirtyEight's NBA ELO methodology used the same basketball-specific conversion: team ELO difference plus home advantage, divided by 28, gives projected point margin. For example:

- `70` ELO home advantage is worth `2.5` expected points.
- `100` ELO home advantage is worth about `3.6` expected points.
- `140` ELO points is worth `5` expected points.
- `280` ELO points is worth `10` expected points.

This is a practical day-one default because it is basketball-specific, transparent, and already proven in a public ELO system. It can later be calibrated per competition or ruleset if Basket ELO has enough historical data to justify a learned factor.

## Neutral-site games

The ruleset value is a default, not a guarantee that every game has a home
advantage. Each competition has a `HomeAdvantagePolicy`:

- `automatic` keeps the normal home advantage except for hosted tournament
  metadata such as `Final Four`, `Final Eight`, `Top Four`, and `Final Day`,
  plus known hosted competitions such as FIBA final tournaments, `League Cup`,
  `Leaders Cup`, and `Semaine des As`.
- `neutral` sets the home advantage to `0` for every game in the competition.
- `home` forces the normal home advantage, even when automatic metadata would
  otherwise infer a neutral site.

`games.IsNeutralSite` is a nullable per-game override. When it is set, it wins
over the competition policy; `true` means neutral and `false` means the home
advantage applies. The competition setting is available in the admin
competition editor, so an edition or competition with a different format can
be corrected without changing the ELO formula or rewriting provider parsers. The
admin Games explorer exposes the same three choices for an individual game;
selecting `Automatic` clears the override and returns to competition logic.

FIBA final tournaments are treated as neutral by their exact competition name,
including the catalogued `EuroBasket`, `AfroBasket`, `FIBA World Cup`, `Asian
Games`, and `Summer Olympics` names. FIBA qualifier competitions are
intentionally not treated as neutral by name: the current qualifier-window
format is home-and-away, while exceptional historic neutral qualifier games
can be corrected individually.

The hosted tournament backfill also corrects imported games when API-Sports has
no reliable stage or venue field. This includes the EuroLeague, Basketball
Champions League, and ENBL final events, as well as the confirmed centralized
domestic cup and supercup rounds. Ordinary two-leg finals, distributed cup
rounds, and qualifiers remain home/away.

The same policy is applied by rebuilds, Model Lab, upcoming-game probabilities,
and game explanations. After changing a policy, queue the affected pool's ELO
rebuild so the stored ratings match the new treatment.

## Point-Margin Adjustment

The margin-adjusted ruleset still preserves normal ELO direction:

- Winners always gain ELO.
- Losers always lose ELO.
- Point margin only boosts or dampens the normal win/loss delta.

The adjustment compares the winner's actual margin with the winner's expected margin. Overperforming the expected margin increases the multiplier, while underperforming it dampens the multiplier. The multiplier is bounded between `0.6667` and `1.5`.

The dampener/boost size is:

```text
ln(marginDifference + 1) / marginDampenerFactor
```

`marginDampenerFactor` defaults to `5`. Higher values make the margin adjustment gentler; lower values make it reach the cap faster.

`maxMarginMultiplier` defaults to `1.5`. It caps the strongest boost at `1.5x` and derives the strongest dampener as `1 / 1.5`, or about `0.6667x`.
