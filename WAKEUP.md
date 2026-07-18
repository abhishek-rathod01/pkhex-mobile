# Wake-up summary — 2026-07-18 session (Items 1–3)

Three items done in order, each committed separately to `master`. Nothing
hit the "same error 3 times, stop and log as blocked" condition for the
items themselves - one genuine pre-existing bug was found and documented
(not fixed, out of scope) rather than blocking progress.

## Item 1 — Gen1/2 IV/EV design decision, resolved (commits `df48f97`, `f4d88e8`)

Chose **option 3** from the previous session's open question: cap the IV
input fields themselves at the hardware-accurate range instead of a
post-save normalization message, matching PKHeX.WinForms' verified
`NumericUpDown.Maximum` approach.

- IV fields cap live at 15 for Gen1/2, 31 for Gen3+ (`ivMax`, computed from
  `pk.Generation`). A `TextChanged` handler clamps any typed value above
  the cap back down as the user types.
- HP IV (no independent storage in Gen1/2) and SpD IV/EV (mirrors SpA,
  shared "Special" stat) are disabled and kept in sync live for Gen1/2.
- Defensive `Math.Clamp` backstop when populating fields from a loaded
  PKM object.
- `verify/Gen12IvCap/Program.cs` confirms no crash from over-range input,
  both through the real PKM setters (which already clamp at the library
  level) and via adversarial synthetic input to the app's own clamp.
- **Advisor review caught two gaps**, both closed in the second commit:
  Gen3+ HP/SpD IV fields weren't live-clamped (only checked at save time);
  and a genuine pre-existing bug was surfaced (see "Known bug" below).

## Item 2 — On-device edit flow verification (commit `a4dd9a5`)

Drove the full load → edit → save → reload flow on the
`PkhexMobile_Emulator` AVD using the real `FileSaver`/`FilePicker` UI, not
the library-level proxy `verify/EditRoundTrip/Program.cs` had relied on.
Screenshots in `verify/OnDeviceEdit/screenshots/`.

- **Full round-trip** on Gen5 (`gen5_real.sav`): edited nickname, level,
  an IV, and an EV through the on-screen keyboard, saved via the real
  document-picker dialog, reloaded the exported file through the real
  picker, confirmed every value round-tripped.
- **Caught and avoided a real hazard**: the FileSaver dialog's filename
  field autocompleted to the *original* file's name (`gen5_real.sav`)
  partway through - saving with that name would have silently overwritten
  the real save. Cleared it and typed a distinct name before confirming.
  Worth remembering for any future on-device save-flow testing.
- **Gen1 IV cap enforcement confirmed live**, not just in code: the IV
  label read the Gen1/2-specific text, `uiautomator dump` confirmed HP/SpD
  fields have `enabled="false"` in the actual view hierarchy, and typing
  `99` into an editable IV field live-clamped to `15` on screen.
- **Environment gotcha, costly to rediscover**: installing the Debug APK
  via a bare `adb install` crashes on launch (`SIGABRT`, "No assemblies
  found... Assuming this is part of Fast Deployment"). Debug builds
  default to Fast Deployment, which needs the .NET Android build's own
  deploy step, not a raw `adb install`. **Always deploy with `dotnet build
  PkhexMobile/PkhexMobile.csproj -f net10.0-android -t:Run`** (or
  `-t:Rebuild` then a proper install), not a manual `adb install` of a
  pre-existing APK path. Also: plain `dotnet build` without `-t:Run` does
  not reliably refresh the on-disk `-Signed.apk`'s timestamp even when it
  reports success - check the APK's file time against source files before
  trusting a stale-looking `adb install -r`.

## Item 3 — PC box viewing, read-only (commit `91043ec`)

`BoxListPage` reuses the party list's `PartyEntryDisplay` record and item
template, adds a box-name `Picker`, and filters empty slots
(`Species == 0`) since boxes are mostly empty unlike a full party. "View
Boxes" button on `MainPage`, shown only when `sav.HasBox`.

- **Read-only guarantee is structural, not a UI toggle**: box entries
  navigate to the existing `PokemonDetailPage` with
  `NavigationState.PendingPokemonSave` left **null**. The detail page
  already hides "Save Changes" whenever `parentSave is null` - no new
  read-only code path was needed, and it means a box mon can never be
  written back through `SetPartySlotAtIndex` (which is party-slot-indexed,
  0-5, and would have silently corrupted data or gone out of range against
  a box-slot index of 0-29).
- Verified on-device against two real saves with populated boxes, chosen
  via a quick inventory harness (`verify/BoxInventory/Program.cs`) rather
  than guessing: Gen1 Red (12 boxes, 235 boxed mons, default "Box N"
  names) and Gen9 Scarlet (32 boxes, 618 boxed mons, real box names from
  the save itself). Both: box list matched the harness's occupied-slot
  count exactly, box switching via the Picker worked, and tapping an entry
  showed real stats with **no Save Changes button** on either generation.
  Screenshots in `verify/OnDeviceBoxes/screenshots/`.

## Known bug, not fixed (logged, not blocking)

**Gen1/2 EVs are real 16-bit Stat Exp (0-65535), not the modern 0-252 EV
system** - confirmed against the real `POKEMON RED-0.sav` (MEW at level
100 has all six EVs at `65535`, maxed stat exp). The app's EV fields still
parse into a `byte` and validate against 252 for *every* generation,
unchanged from before this session. Effect: `byte.TryParse("65535")`
**fails outright**, so `OnSaveChangesClicked` blocks saving **any** edit -
even a nickname-only change - on a Gen1/2 mon that already has real stat
exp loaded. This is pre-existing (the old hardcoded-252 validation had the
identical failure mode); it only surfaced now because Item 2's on-device
pass was the first time `OnSaveChangesClicked` was actually driven against
a real Gen1/2 save with real stat-exp values in the UI. Item 2's full
save-round-trip was therefore run against a Gen3+ save (Gen5) instead;
the Gen1 pass was display/cap-verification only, no Save Changes click.

**Fix, if picked up next**: widen the EV `Entry` parsing past `byte`
(e.g. `ushort`/`int`) and make the EV cap generation-aware (0-65535 for
Gen1/2 vs 0-252 for Gen3+), mirroring exactly what this session did for
IVs. Not done here - Item 1 was explicitly scoped to IV caps only, and
this is a separate, larger design decision worth its own review rather
than a drive-by fix buried in an unrelated item.

## Nothing else is blocked

All three items completed and verified as specified. No item required
falling back to a "blocked" log entry.

## What I'd do next

1. **Fix the Gen1/2 EV bug above** - same pattern as the IV fix, but for
   EVs (wider type, gen-aware cap 0-65535 vs 0-252). This is the most
   obvious next correctness gap, and it currently blocks saving *any*
   edit on a decent fraction of real Gen1/2 saves.
2. Per this session's instructions, do **not** start species/move editing
   or legality checking yet - those are explicitly deferred to a separate
   session.
3. Box editing (move/swap) was explicitly out of scope for Item 3 and
   remains undone - PC box viewing is read-only by design in this pass.
