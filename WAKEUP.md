# Wake-up summary — 2026-07-20 session (species + move editing)

Added species and move editing to the detail-page editor, behind a clear
legality warning, round-trip verified across Gen 1/5/9 and driven once
on-device through the real FileSaver/FilePicker. Committed. Nothing hit the
"same error 3 times, stop and log as blocked" condition; two genuine
pre-existing bugs were found *and fixed* (not just logged) because they made
the new feature produce broken saves.

## What was built (commit: see `git log`)

`PokemonDetailPage` now edits **species** (one `Picker`) and the **four
moves** (four `Picker`s) in addition to nickname/level/IV/EV. Species/moves
were removed from the read-only `StatsList` (Nature/Ability stay there).

- Species picker bounded by `pk.MaxSpeciesID`, moves by `pk.MaxMoveID`
  (format structural limits, not legality). Move index 0 = "(None)".
- Save applies `pk.Species = id` then `pk.SetMoves(moves)` (the latter
  recomputes PP - raw `Move1..4` would leave stale PP).
- **Legality warning**: persistent orange label always shown under the
  pickers, plus a contextual note appended to the save-success status only
  when species/moves actually changed. **No** auto-validation/correction
  anywhere (out of scope by request).

## Two real bugs found via verification and fixed

Both affected the **pre-existing** level/IV/EV edit path too, not only the
new code - they only surfaced now because this was the first edit that
changes species (which makes stale stats/level obviously wrong).

1. **Party stat block was never recomputed on any edit.**
   `SaveFile.SetPartyValues` only recalculates stats when none are present
   (`Stat_HPMax == 0`); real party mons always have stats, so the block went
   stale. A species change kept the old species' HP; level/IV/EV edits also
   never updated stats. (The previous session's PROGRESS claim that
   `SetPartySlotAtIndex` recalculates the stat block was incorrect - the new
   harness disproves it.) **Fixed** by calling `pk.ResetPartyStats()` before
   `SetPartySlotAtIndex`, gated on a stat-affecting change so nickname-only
   edits don't heal/clear status.
2. **Level misread when species + level both change.** Level is stored as
   EXP, and EXP↔level depends on the species' growth-rate group. Setting
   level before species reinterpreted the EXP under the new rate (level-50
   Skeledirge → level-45 Garchomp). **Fixed** by setting `pk.Species` before
   `pk.CurrentLevel`.

## Verification

- **Library round-trip** (`verify/SpeciesMoveEdit/Program.cs`): Gen1
  (Mew→Charizard), Gen5 (Serperior→Pikachu, incl. clearing 2 move slots),
  Gen9 (Skeledirge→Garchomp), each also changing 4 moves + level. Asserts
  species/moves/PP/`Stat_HPMax`/`Stat_Level` round-trip, Gen1 stored types
  match the new species, and stats are genuinely recomputed (not stale). Plus
  a **form-staleness probe** proving an out-of-range Form after a species
  change can't crash (PersonalInfo falls back to base form). All pass;
  originals byte-for-byte untouched.
- **On-device** (`PkhexMobile_Emulator`, real `gen9_real.sav`): changed
  species/move/level via the pickers, saved via the real FileSaver dialog,
  reloaded via the real FilePicker - party + detail show the edited values.
  Screenshots in `verify/OnDeviceSpeciesMove/screenshots/`.

## Environment reminders (unchanged, still true)

- Deploy Debug builds with `dotnet build PkhexMobile/PkhexMobile.csproj -f
  net10.0-android -c Debug -t:Run`. A bare `adb install` crashes (Fast
  Deployment). This session, a `-t:Run` that reported "up-to-date" did *not*
  redeploy - had to `adb uninstall` first, then `-t:Run` deployed cleanly.
- FileSaver's filename field can autocomplete to an existing file's name;
  clear it and type a distinct name before confirming (caught again here).
- In Git Bash, prefix `adb shell`/`adb pull` of `/sdcard/...` paths with
  `MSYS_NO_PATHCONV=1` or the path gets mangled to a Windows path.

## What I'd do next

1. **Species/move legality checking is still explicitly deferred** - the app
   applies edits as-is with a warning, by design. If a future pass wants it,
   PKHeX.Core has `LegalityAnalysis`; surface results read-only, don't
   auto-fix, and keep it opt-in.
2. Box (PC) editing remains read-only (structural null-`parentSave` guard).
   Box→party moves / slot swaps are still undone.
3. Form and Ability/Nature are not user-editable yet; Form silently carries
   over on species change (safe, base-form fallback). Editing Form would need
   a per-species form list and is its own scope.
