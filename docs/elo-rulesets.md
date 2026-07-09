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

## Point-Margin Adjustment

The margin-adjusted ruleset still preserves normal ELO direction:

- Winners always gain ELO.
- Losers always lose ELO.
- Point margin only boosts or dampens the normal win/loss delta.

The adjustment compares the winner's actual margin with the winner's expected margin. Overperforming the expected margin increases the multiplier, while underperforming it dampens the multiplier. The multiplier is bounded between `0.6667` and `1.5`.
