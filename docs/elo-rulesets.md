# ELO Rulesets

Basket ELO stores ratings by ruleset version from the start, so a new formula can be rebuilt without deleting or corrupting ratings produced by an older formula.

## Ruleset Names

Ruleset names follow short model-style slugs:

- `basic-elo-v1`: plain win/loss ELO.
- `point-margin-elo-v1`: ELO with a point-margin adjustment.

`point-margin-elo-v1` is the default public ruleset.

## Shared Constants

Both day-one rulesets use:

- Base rating: `1500`
- K-factor: `20`
- Home advantage: `100` ELO points
- Competition weight: `1.0`

The home advantage value is intentionally aligned with FiveThirtyEight's published NBA ELO methodology, where the home team receives a constant 100-point ELO adjustment.

## Point-Margin Conversion

`point-margin-elo-v1` converts ELO difference into expected point margin with:

```text
expectedMargin = eloDiff / 28
```

We chose `28` because FiveThirtyEight's NBA ELO methodology used the same basketball-specific conversion: team ELO difference plus home advantage, divided by 28, gives projected point margin. That means:

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
