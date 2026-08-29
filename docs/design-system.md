# BasketElo Fieldhouse Design System

Fieldhouse is the visual and interaction system for BasketElo. It combines the clarity of an analytical workspace with the pacing and typography of an independent sports journal. It should feel precise, curious, and basketball-native—not like a generic AI dashboard or a reskinned administration template.

The CSS custom properties in `src/BasketElo.Web/wwwroot/app.css` are the implementation source of truth. This document explains why they exist and how future work should use them.

## 1. Design principles

1. **The data is the interface.** Rankings, movement, graphs, fixtures, and results remain visually dominant. Decoration must never compete with comparison or scanning.
2. **Editorial outside, analytical inside.** Navigation, page openings, explanatory content, and transitions can carry personality. Tables, charts, and controls stay calm and predictable.
3. **Show the trail.** Method, source, scope, ruleset, and freshness are part of the product—not footnotes.
4. **Use hierarchy before containers.** Prefer whitespace, typography, and alignment over wrapping every group in a rounded card. Rules are reserved for places where they explain interaction or comparison.
5. **Motion explains change.** Animation may reveal hierarchy, connect a trigger to a panel, or confirm selection. It is never ambient noise.

## 2. Research basis

The system adapts useful principles from mature product systems without copying their visual language:

- Carbon’s UI shell guidance supports a header-only shell when a product has a small number of primary sections, with deeper navigation disclosed only when needed: [Carbon UI shell header](https://carbondesignsystem.com/components/UI-shell-header/usage/) and [global header pattern](https://carbondesignsystem.com/patterns/global-header/).
- Atlassian treats color as a semantic role system and design tokens as the shared source of truth: [color](https://atlassian.design/foundations/color) and [foundations](https://atlassian.design/foundations).
- Atlassian’s motion guidance recommends purposeful, subtle transitions and favors transform/opacity for smoothness: [motion](https://atlassian.design/foundations/motion) and [applying motion](https://atlassian.design/foundations/motion/applying-motion).
- Carbon’s choreography guidance sequences the stable shell before content and keeps staggered entrances brief: [motion choreography](https://preview.carbondesignsystem.com/building-blocks/foundations/motion/choreography).
- Porsche’s motion tokens provide a useful high-end timing range for small, moderate, and larger transitions: [motion tokens](https://designsystem.porsche.com/v4/tokens/motion/).

BasketElo’s interpretation is intentionally more editorial and less corporate: serif display type, scorebook neutrals, selectively used court-like rules, indexed sections, and restrained geometric motifs.

## 3. Color system and rationale

### Core palette

| Role | Token | Value | Purpose |
|---|---|---:|---|
| Chalk canvas | `--color-canvas` | `#F5F4EF` | A soft scorebook/paper ground that is easier on the eyes than pure white. |
| Deep canvas | `--color-canvas-deep` | `#EBEAE3` | Quiet separation between large page regions. |
| Paper surface | `--color-surface` | `#FFFDF8` | Tables, controls, and elevated reading surfaces. |
| Muted surface | `--color-surface-muted` | `#EEEFE9` | Secondary controls, hover states, and filter expansions. |
| Ink | `--color-text` | `#17262A` | Primary text; near-black with a subtle green cast. |
| Secondary ink | `--color-muted` | `#5E6C70` | Explanations and metadata that must remain readable. |
| Court teal | `--color-brand` | `#0F4B52` | Trust, structure, links, and committed actions. |
| Deep court | `--color-brand-dark` | `#0B343A` | Inverse panels and high-emphasis states. |
| Ball orange | `--color-accent` | `#E9782D` | Selection, active rules, focus, and a small amount of visual energy. |
| Burnt orange | `--color-accent-dark` | `#A94F16` | Accessible accent text on light surfaces. |

### Why these colors

- **Warm neutrals** connect the product to paper scorebooks, arena programs, and hardwood light without using literal sports imagery.
- **Deep teal** provides the seriousness and stability required for statistical work, while avoiding the blue-purple gradient language common to AI products.
- **Orange** is a basketball-native signal color. It should remain sparse—active indicators, focus, key annotations, and a few editorial details—so it continues to mean “look here.”
- **Status colors are semantic.** Green means success/positive, amber means warning, rust means danger/negative, and teal means information. Brand orange must not replace these roles.

Color is never the only carrier of meaning. Pair it with text, position, icons, borders, or signs such as `+` and `−`.

### Contrast baseline

The current primary combinations meet WCAG AA for normal text:

| Combination | Contrast |
|---|---:|
| Ink on chalk | 14.16:1 |
| Secondary ink on chalk | 4.95:1 |
| Court teal on paper | 9.61:1 |
| Burnt orange on chalk | 5.00:1 |
| Inverse text on deep court | 12.79:1 |
| Danger text on danger surface | 5.51:1 |
| Success text on success surface | 6.35:1 |

Do not use the brighter `--color-accent` for small text on the light canvas; use `--color-accent-dark` instead.

## 4. Typography

- `--font-display` is a serif stack for editorial page titles, section statements, key context labels, and selected high-emphasis metrics.
- `--font-sans` is the workhorse for navigation, controls, body copy, tables, and UI labels.
- `--font-mono` is reserved for formulas, sequence numbers, ruleset metadata, and compact technical annotations.

The contrast between editorial serif and practical sans is a core part of the identity. Do not use serif type inside dense tables, form controls, or routine buttons.

## 5. Navigation architecture

### Desktop

BasketElo uses a sticky top masthead rather than a permanent left rail.

- **Primary workspaces:** Rankings, Movers, and Model Lab are always visible.
- **Information links:** Method, Sources, About, and Support remain individually visible as a quiet group on the right. This makes the left/right distinction explicit: features on the left, context about the project on the right.
- **Admin disclosure:** role-gated operational links live in a separate menu and never compete with public navigation.
- **Account:** identity controls remain at the far edge of the masthead.

This reduces simultaneous navigation layers. A page may have one local workspace bar, but it must combine context and switching controls into a single band rather than stacking multiple tab rows.

### Mobile

The masthead collapses into one round menu control. Opening it reveals primary destinations first, followed by Project, Admin when authorized, and account controls. The reading order must match this hierarchy.

### Active states

Primary and local navigation use an orange rule plus stronger text, not filled pills. Filled segmented controls are reserved for mutually exclusive compact settings where the container itself adds meaning.

## 6. Two page anatomies

### Data workspaces

Use for Rankings, Movers, Model Lab, Games, and operational pages.

1. Compact identity/title area.
2. One workspace bar containing current context, scope switches, and view switches.
3. An open filter strip; advanced filters expand in a subtle contrasting wash beneath it.
4. Snapshot metadata.
5. The primary table, graph, or list.

Tables and graphs may keep bounded surfaces because containment helps dense data. Filters and navigation should not become floating cards by default.

### Editorial stories

Use for How it works, Data sources, About, methodology notes, and future explainers.

1. Typographic opening with a short kicker, large serif statement, lede, and optional geometric data motif.
2. Numbered sections with a slim metadata rail and a broad reading column.
3. Rules and whitespace instead of repeated card boxes.
4. A single high-contrast case file or field note when the story benefits from emphasis.
5. A closing caveat or next action.

Use `.be-story`, `.be-story-hero`, `.be-story-section`, `.be-story-meta`, `.be-story-heading`, `.be-story-copy`, `.be-story-orbit`, and `.be-story-note` before introducing page-specific equivalents.

## 7. Component rules

- **Buttons:** primary for committed actions, outline/quiet for secondary actions, danger only for destructive actions. Sentence case.
- **Tabs and view switches:** text plus an active rule; avoid nested pill bars.
- **Filters:** labels stay visible. Search is available at the first level; lower-frequency controls expand in one related panel. Active filters are removable chips.
- **Tables:** tabular numerals, clear numeric alignment, stable column widths, visible row focus, and sticky columns only when they materially aid comparison.
- **Cards:** use only when an item is independently actionable or movable. Related prose, formulas, and principles should generally be arranged through spacing, numbering, and alignment.
- **Disclosures:** use semantic `details`/`summary` where possible. The trigger must communicate open state visually and remain keyboard operable.
- **Status:** consume semantic status tokens. Include a text label or sign, never color alone.
- **Empty/loading/error states:** every asynchronous surface needs all three. Loading should identify what is being fetched; errors should say what the user can do next.

### Separator budget

- Use full-width rules for sticky-surface boundaries, dense tables, and disclosure rows where the line explains behavior.
- Do not place a rule between every editorial section. Section numbers, whitespace, and type changes already establish rhythm.
- Within one viewport, aim for one dominant horizontal rule and only the local lines required by data or interaction.
- Prefer a soft surface change, short accent mark, or additional spacing before adding another separator.

## 8. Motion system

| Token | Timing | Use |
|---|---:|---|
| `--motion-fast` | 160 ms | Hover, focus, small color changes. |
| `--motion-standard` | 260 ms | Selection, chevrons, short panel state changes. |
| `--motion-enter` | 480 ms | Page, disclosure, and editorial content entrance. |

Rules:

- Animate `opacity` and `transform` whenever possible.
- Animate no more than two properties for one response.
- A page entrance establishes hierarchy once; it must not replay during routine filtering.
- Staggered entrances use 70 ms offsets and should complete in roughly half a second.
- Decorative motion must pause effectively under `prefers-reduced-motion`; the global stylesheet reduces all animation and transition durations.
- Do not animate table geometry, numeric values, or layout dimensions when it can cause scanning targets to move.

## 9. Accessibility and interaction checklist

Every new or modified component must be checked for:

- Keyboard reachability and a visible orange focus ring.
- A semantic landmark, label, or heading relationship.
- Hover, focus, active, disabled, loading, empty, and error states where applicable.
- 44 px touch targets where controls stand alone on mobile; compact table controls may be smaller when grouped.
- Contrast against the exact surface used, not only against white.
- A functional layout at 390 px and a desktop layout at 1440 px or wider.
- Reduced-motion behavior.

## 10. Implementation rules for future work

- Reuse or extend tokens in `app.css` before adding hard-coded page colors, shadows, radii, or timing values.
- Keep scoped page CSS focused on layout and page-specific structure.
- Prefer native HTML behavior before custom JavaScript for disclosure, links, focus, and form semantics.
- Preserve the distinction between editorial surrounds and calm analytical data surfaces.
- Do not reintroduce a permanent left rail unless the number of always-visible primary workspaces grows beyond what the masthead can support.
- Do not add another horizontal tab row when the option can live in the existing workspace bar, filter ribbon, or contextual disclosure.

## 11. Implemented in the Fieldhouse pass

- Replaced the desktop left rail with a sticky masthead: feature navigation on the left and individually visible information links on the right.
- Combined ranking pool and view switching into one labeled workspace bar.
- Changed filters from a floating card to a ruled ribbon with an animated advanced panel.
- Introduced the Fieldhouse palette, serif/sans/mono type roles, focus treatment, and motion tokens.
- Rebuilt How it works, Data sources, About, and Support as responsive editorial stories.
- Added a reusable court/data orbit motif, indexed story sections, formula tracks, principle rows, an interactive source accordion, and high-contrast case-file/field-note treatments.
- Preserved skip navigation, main landmarks, semantic disclosures, keyboard focus, and reduced-motion support.
- Reduced decorative separators across ranking surrounds and editorial pages; retained them in tables and source disclosures where they aid scanning or communicate interaction.

When extending the system, update this section and the relevant rule above so future contributors understand both what changed and why.
