# Charting

Basket ELO currently uses `Radzen.Blazor` for ELO history charts in the Blazor web app.

## Current Choice

`Radzen.Blazor` is the active implementation for the rankings ELO evolution chart and the team detail rating history chart.

Reasons:

- Free and MIT licensed.
- Native Blazor component model, so no React or npm integration is required.
- Supports multi-series line charts, date axes, legends, crosshairs, and tooltip templates.
- Good enough for the current requirement: hover over the chart to inspect date, ELO value, team, and related point metadata.

The shared component is `EloEvolutionChart` under `src/BasketElo.Web/Components/Charts`.

## Current Notes

- Rankings shows the top 20 teams in the active result set.
- The chart uses axis-triggered tooltips and crosshairs so the cursor can inspect the nearest date point.
- Team detail uses the same component with a single series and includes ELO delta and rank when available.
- Markers are intentionally small so dense history lines do not become visually heavy.

## Possible Upgrades

If Radzen becomes limiting, the main candidates are:

- ApexCharts through a Blazor wrapper. We are eligible for ApexCharts' free/community use, so this is a valid future option. It may offer a more polished chart interaction model, richer zooming, and stronger tooltip behavior.
- Apache ECharts through a Blazor wrapper such as Vizor.ECharts. This is the strongest free/open-source option for advanced chart behavior, but configuration is more JavaScript-style.
- Plotly.Blazor. This is useful for data exploration with zoom, pan, and rich hover behavior, but it may feel heavier and more analytics-oriented than the current app UI.

Do not introduce React just for charts unless none of the Blazor or light JS interop options can satisfy the interaction requirement.
