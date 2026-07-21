# PkhexMobile

A .NET MAUI mobile front-end for viewing and editing Pokémon save files, built
on top of the vendored [PKHeX.Core](https://github.com/kwsch/PKHeX) library.
This project is **not** a port or fork of PKHeX's save-parsing/editing logic —
it's a UI layer that loads a save file via the platform file picker, hands it
to PKHeX.Core's existing APIs, and renders/edits the result.

## Status

- **Save parsing**: verified against real save files for Generations 1–9,
  including Let's Go Pikachu/Eevee (7b) and Legends Z-A (9). Per-generation
  detail and quirks are documented in `verify/GenN/PROGRESS-genN.md`.
- **Party list & PC box viewing**: read-only box browsing and a full party
  list, both backed by the same detail screen, with species sprites and
  held-item icons.
- **Editing**: nickname, level, species, moves, and IV/EV editing with export
  to a new save file via the native file picker/`FileSaver`, verified
  round-trip on-device across Generations 1, 5, and 9. Gen1/2 IV fields are
  capped at the hardware-accurate 0–15 range (instead of the modern 0–31)
  with derived/linked HP and Special stats; Gen1/2 EV fields use the correct
  0–65535 Stat Exp range, matching real Gen1/2 mechanics. Species/move edits
  are applied exactly as chosen, with a persistent on-screen warning that
  PKHeX.Core does not auto-fix legality.
- **Visual design**: a full design-system pass (colors, typography, spacing,
  motion) applied to every screen, with a dirty/clean Save button (disabled
  until an edit is made, disabled again after a successful save).
- **Not yet implemented**: box editing (moving/swapping slots) and a wired
  legality-check engine.

See `PROGRESS.md` and `WAKEUP.md` for full session-by-session history.

## Design & assets

The visual design (colors, typography, spacing, radii, shadows, motion,
component behavior) was specified in a separate design-handoff bundle and
implemented natively in MAUI `ResourceDictionary`/XAML — no HTML/React/CSS
ships in the app itself.

- **Fonts**: Space Grotesk, Manrope, and JetBrains Mono (all SIL Open Font
  License), self-hosted as static weight instances under
  `PkhexMobile/Resources/Fonts/`.
- **Species sprites**: regular and shiny icons for National Dex #1–905,
  sourced from [msikma/pokesprite](https://github.com/msikma/pokesprite)
  (MIT license for the packaging/tooling; the sprite artwork itself depicts
  Pokémon, © Nintendo/Creatures Inc./GAME FREAK Inc.). pokesprite does not
  yet cover Generation 9 (Scarlet/Violet/Legends Z-A) species; those render
  a neutral Poké Ball placeholder instead of a broken image.
- **Held-item icons**: sourced from the same repository, matched from
  PKHeX.Core's own item name list (not pokesprite's internal numbering,
  which does not correspond to PKHeX's item IDs) by normalized English name.
  Coverage is partial (roughly a third of all item IDs) — TMs/TRs and many
  minor items have no icon in the source pack and fall back to the same
  placeholder.
- **App icon / splash**: an original Poké Ball SVG authored for this project
  (not copied from any source), referencing the general silhouette of
  Nintendo/Creatures Inc./GAME FREAK Inc.'s Poké Ball design. This is an
  unofficial fan project; no affiliation with or endorsement by Nintendo,
  Creatures Inc., GAME FREAK Inc., or The Pokémon Company is implied.

## License

This project vendors [PKHeX.Core](https://github.com/kwsch/PKHeX) unmodified
under `vendor/PKHeX.Core/`, which is licensed under the **GNU General Public
License v3.0 or later (GPL-3.0-or-later)** — see
`vendor/PKHeX.Core/LICENSE`. PkhexMobile is a front-end UI layer around that
library, not a fork or modification of its logic. Because the app links
directly against PKHeX.Core, the combined work is likewise governed by the
terms of the GPL-3.0-or-later.
