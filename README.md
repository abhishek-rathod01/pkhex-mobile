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
  list, both backed by the same detail screen.
- **Editing**: nickname, level, and IV/EV editing with export to a new save
  file via the native file picker/`FileSaver`, verified round-trip on-device
  for multiple generations. Gen1/2 IV fields are capped at the
  hardware-accurate 0–15 range (instead of the modern 0–31) with derived/
  linked HP and Special stats, matching real Gen1/2 mechanics.
- **Known issue**: Gen1/2 "EVs" are actually 16-bit Stat Exp (0–65535), not
  the modern 0–252 EV system. The EV input fields still parse as a `byte`
  capped at 252, which causes saving *any* edit to fail on a Gen1/2 mon that
  already has real stat exp loaded. Not yet fixed — see `WAKEUP.md`.
- **Not yet implemented**: species/move editing, box editing (moving/
  swapping slots), and legality checking.

See `PROGRESS.md` and `WAKEUP.md` for full session-by-session history.

## License

This project vendors [PKHeX.Core](https://github.com/kwsch/PKHeX) unmodified
under `vendor/PKHeX.Core/`, which is licensed under the **GNU General Public
License v3.0 or later (GPL-3.0-or-later)** — see
`vendor/PKHeX.Core/LICENSE`. PkhexMobile is a front-end UI layer around that
library, not a fork or modification of its logic. Because the app links
directly against PKHeX.Core, the combined work is likewise governed by the
terms of the GPL-3.0-or-later.
