# BasketElo Design System

BasketElo should feel like a serious basketball intelligence workspace: precise, fast to scan, and confident without looking like a generic Blazor admin template. The product spans public rankings, game browsing, team inspection, model tuning, backfill operations, and internal identity/admin review, so the design system favors dense information, restrained contrast, and clear operational states.

## Product Character

- **Analytical:** ranking tables, filters, and charts are the core experience. Prioritize readable numbers, stable table layouts, and direct controls.
- **Basketball-native:** use warm court-inspired neutrals and amber highlights, balanced by teal and blue data accents. Avoid novelty sports decoration.
- **Operational:** internal workflows need clear status colors, review affordances, and low-friction forms. Avoid marketing-style hero sections in admin surfaces.
- **Trustworthy:** UI copy should be specific and factual. Avoid hype, vague empty states, or decorative labels that do not help users decide what to do.

## Visual Tokens

Use the CSS custom properties in `wwwroot/app.css` as the source of truth.

- **Canvas:** `--color-canvas` for the page background, `--color-surface` for primary panels, `--color-surface-muted` for filters and secondary panels.
- **Text:** `--color-text` for primary content, `--color-muted` for helper copy, and `--color-subtle` for labels.
- **Brand:** `--color-brand` is the main action color. `--color-accent` is the basketball amber accent. `--color-teal` is the positive/data operations accent.
- **Status:** use semantic tokens for success, warning, danger, info, and neutral. Do not invent one-off status palettes unless a new semantic state is needed.
- **Shape:** panels and cards use `--radius-md` or `--radius-lg`; pills use `--radius-pill`. Avoid large rounded cards unless they are a repeated data object.
- **Shadow:** use `--shadow-sm` for cards and `--shadow-md` for elevated panels. Keep shadows subtle.

## Layout

- The left navigation is a fixed workspace rail on desktop and a collapsible rail on mobile.
- Page content should sit in a constrained work area and use vertical rhythm from `--space-*` tokens.
- Primary pages start with a compact page header: eyebrow, `h1`, short context copy, and optional action group or key metric card.
- Avoid nested cards. A page section can be a surface; repeated records inside it can be cards, but card-in-card layouts should be avoided.
- Tables are first-class UI. Give them rounded shells, sticky-feeling headers where practical, numeric alignment, and row hover states.

## Components

- **Buttons:** primary for committed actions, outline/secondary for navigation and reset actions, danger only for destructive actions. Buttons use sentence case.
- **Forms:** labels are compact and bold; controls use the global Bootstrap overrides and should not introduce page-specific border colors.
- **Filters:** use muted surfaces, tight grid layouts, and clear action placement at the end of the grid.
- **Metric cards:** label, strong value, optional helper. Keep them compact and comparable.
- **Status pills:** uppercase or title case is acceptable, but status color must be semantic and consistent across pages.
- **Charts:** use the shared chart palette from ranking/model pages. Favor legibility over decorative gradients.

## Page Guidance

- **Rankings:** public-facing and polished. The strongest-team strip and summary metrics should establish confidence quickly.
- **Games:** data-browser feel. Keep filters prominent and table actions compact.
- **Team detail:** profile-focused. Rank and ELO should be visually dominant; history and games remain utilitarian.
- **Model Lab:** analytical workbench. Controls and output panels should feel paired and comparable.
- **Backfill/Admin/Identity:** operational command center. Status, warnings, and review state are more important than visual drama.

## Future Implementation Rules

- Prefer adding or reusing tokens in `app.css` before adding hard-coded page colors.
- Keep page-specific CSS focused on layout and exceptional component structure.
- Use the existing Bootstrap/Radzen stack, but override it through tokens so BasketElo keeps a recognizable identity.
- Any new feature should include loading, empty, error, hover, focus, and mobile states.
- When adding charts or visualizations, reuse the existing accessible data colors and verify contrast on the warm canvas.
