# DCode Brand

DCode is a professional, focused, developer-first workspace. Its primary
tagline is **Build software.** and its supporting line is **Your development
workspace, from code to production.**

## Mark and wordmark

The canonical mark is the code-native SVG in
`frontend/src/components/brand/dcode-logo.tsx`. It combines a geometric red
`D`, an angular lower-left notch, and a terminal `> _` prompt. The mark must
remain usable without the wordmark at compact IDE sizes.

Reusable components:

- `DCodeLogo` for compact application and workspace chrome
- `DCodeWordmark` for the product name
- `DCodeBrand` for configurable lockups with the wordmark, tagline, and
  supporting line

The full tagline lockup is reserved for welcome, empty, About, and marketing
surfaces. Normal IDE chrome uses the compact logo and wordmark.

## Color system

The DCode product overrides live in `packages/theme/src/themes/dcode.css`.
Brand red is `#E53935` and is used as an accent for the logo, primary actions,
focus, active navigation, selected states, and important indicators. Editor and
workspace backgrounds remain neutral.

Product code should consume semantic theme tokens rather than repeat palette
values in components. DCode defines primary, hover, foreground, background,
surface, elevated surface, border, text, and muted text tokens for both light
and dark themes.

## Application shell

The global DCode sidebar is the single primary navigation surface. It uses the
compact brand lockup, grouped navigation, a restrained red active indicator,
and a separated Settings action. Global pages share consistent content width,
spacing, eyebrow labels, headers, borders, and neutral surfaces.

The application shell occupies the viewport. Its sidebar and top status bar
remain visible while the central content area scrolls. Project focus mode and
Chat may use internal IDE-style panes where independent scrolling is necessary.

## Native assets

The SVG React component is the source for future exported assets. A Windows
application icon still needs a dedicated `.ico` containing appropriate raster
sizes (including 16, 24, 32, 48, 64, and 256 pixels), followed by native host
and installer manifest wiring. A favicon export should be generated from the
same source. These exports must be visually checked at each small size.
