# Generation 6 (X/Y/Omega Ruby/Alpha Sapphire) Save-Parsing Verification

## Status: VERIFIED against a REAL save file (2026-07-17)

Originally verified library-generated only (see below); real Pokémon Alpha Sapphire
saves (`27.Current`, 483328 bytes, Trainer Theseus, 6-member party; also
`oldASsave/main`, Trainer Isabella, 1-member party) and an Omega Ruby save (`main(6)`,
Trainer Jack, 6-member party) became available and were confirmed both via
console-level `SaveUtil.GetSaveFile` read (all three) and through the actual app UI
(file picker → party list → detail screen, `27.Current`) on the PkhexMobile_Emulator AVD.

- Party list showed all 6 members correctly (Parasect Lv.95, Ampharos Lv.91, Latias
  Lv.49, Chansey Lv.100, Lugia Lv.51, Rayquaza Lv.71).
- Detail screen for Parasect showed: Species Parasect, Nature Hardy, Ability Dry Skin,
  Moves Spore/False Swipe/Swords Dance/Rest, IVs 6/24/24/29/15/21, EVs
  158/37/9/39/15/252 — all matching the console-level read exactly.

This closes the "not a real save file" note below — Gen6 is now confirmed against
genuine save files (both AS and OR sub-versions), not just a library-generated one.

## Summary (original)

Verified that PKHeX.Core's `SAV6XY` save object can be constructed, populated, and read
back correctly using the library's own APIs. **This used a library-generated blank save,
not a real save file dumped from a 3DS/emulator** — no `.sav` file exists anywhere in this
worktree (confirmed via a recursive search for `*.sav` before starting).

## What was tested

- Source read first: `vendor/PKHeX.Core/Saves/SAV6XY.cs` was inspected to confirm the
  actual blank-constructor signature before writing any code. `SAV6XY` has two
  constructors:
  - `SAV6XY(Memory<byte> data)` — load from existing bytes.
  - `SAV6XY()` — parameterless, builds a `SaveUtil.SIZE_G6XY`-sized buffer, sets up
    `SaveBlockAccessor6XY`, calls `Initialize()` (sets block offsets: Party, PSS, HoF, Box,
    JPEG) and `ClearBoxes()`.
- A small console harness was created at `verify/Gen6/` (`Gen6Verify.csproj`, net10.0,
  `ProjectReference` to `../../vendor/PKHeX.Core/PKHeX.Core.csproj`, `Program.cs`) that:
  1. Constructs `new SAV6XY()`.
  2. Sets `sav.OT = "VERIFY"`.
  3. Gets `sav.BlankPKM`, sets `Species = 25` (Pikachu), `CurrentLevel = 10`.
  4. Assigns via `sav.PartyData = [pk]` (the list-setter, **not** `SetPartySlotAtIndex`,
     per the known Gen4 trap — the setter is what reliably updates `PartyCount`).
  5. Reads everything back directly off the live `SAV6XY` object (no serialization).

### Build & run result

`dotnet build` succeeded on the first attempt (0 warnings, 0 errors). `dotnet run` output:

```
=== Gen6 (X/Y) Save Verification ===
Trainer: VERIFY
PartyCount: 1
  Species=25 Level=10

Version: Any
Generation: 6
IsVersionValid (before setting Version): False
Version (after set): Y
IsVersionValid (after setting Version=Y): True

=== Bonus: Write()+GetSaveFile round-trip (optional) ===
Write() succeeded, 415232 bytes.
SaveUtil.GetSaveFile returned null (format not re-detected from blank-constructor bytes). This matches the known Gen4-style limitation.

Done.
```

### Confirmed working (PRIMARY, required verification)

- **Trainer name**: `sav.OT` round-trips correctly ("VERIFY").
- **Party count**: `sav.PartyCount` correctly reflects `1` after `sav.PartyData = [pk]`.
- **Species**: `PartyData[0].Species == 25` (Pikachu) reads back correctly.
- **Level**: `PartyData[0].CurrentLevel == 10` reads back correctly.
- **Generation/version plumbing**: `sav.Generation == 6` by default. `sav.Version`
  defaults to `GameVersion.Any` (not X/Y) until explicitly set — `SAV6XY()` does not pick
  a sub-version for you. `IsVersionValid()` (overridden in `SAV6XY` to require
  `Version is GameVersion.X or GameVersion.Y`) correctly returns `false` before the
  version is set and `true` after `sav.Version = GameVersion.Y` is assigned. This applies
  to blank-constructed saves only — a real save loaded from disk comes with `Version`
  already populated from the save data, so parsing a genuine X/Y save is not expected to
  hit this. Noted here as a quirk of the `new SAV6XY()` blank-constructor path specifically,
  in case the MAUI app ever creates a fresh save in memory rather than only parsing one.

### Bonus/secondary check (optional, as instructed) — partial limitation, not a crash

Per the task instructions this was attempted only once and treated as informational:

- `sav.Write()` **succeeded** and returned a 415,232-byte buffer (`SaveUtil.SIZE_G6XY`) —
  unlike the Gen4 case, this did **not** throw `ArgumentOutOfRangeException`. The blank
  `SAV6XY()` constructor evidently does allocate a full-size backing buffer that `Write()`
  is happy with.
- However, `SaveUtil.GetSaveFile(bytes)` (simulating "load this as a file from disk")
  **returned `null`** — the format was not re-detected. This is the same class of issue
  documented for Gen4: the blank constructor doesn't populate whatever
  magic/footer/checksum field `SaveUtil`'s format-sniffing logic depends on to recognize a
  valid Gen6 save. There is no public PKHeX.Core API exercised here to set that field, and
  per the task rules, no hand-patched/synthetic bytes were used to work around it.
- This was attempted exactly once, as instructed, and is documented here rather than
  further investigated.

### Gen6-specific notes

- Gen6 introduced the Fairy type and Mega Evolution; nothing in this verification path
  touched type charts or Mega mechanics, so no findings there — this check is scoped to
  save-container-level parsing (trainer/party/species/level), not battle mechanics.
- Gen6 has two save layouts: **X/Y** (`SAV6XY`, tested here) and **Omega Ruby/Alpha
  Sapphire** (`SAV6AO`, and a separate `SAV6AODemo`). **Only XY was tested** in this pass,
  per the task instructions. `SAV6AO`/`SAV6AODemo` were not built or exercised — they live
  alongside `SAV6XY` in `vendor/PKHeX.Core/Saves/` and both derive from the shared `SAV6`
  base class, so the same recipe (parameterless constructor → `OT` → `BlankPKM` →
  `PartyData = [...]`) is expected to apply, but this has **not been confirmed** and
  should be checked separately if OR/AS support specifically needs sign-off.

## Known limitations

- Testing used a **library-generated** blank save (`new SAV6XY()`), not a real 3DS save
  dump. No `.sav` file was found anywhere in this worktree prior to starting.
- The `Write()` → `SaveUtil.GetSaveFile()` re-detection round trip does not work for a
  blank-constructor-generated save (returns `null`, does not throw). Loading a
  library-generated save "as if from disk" is therefore not currently verified end-to-end;
  only the direct in-memory object model (construct → mutate → read back) is verified.
- SAV6AO (Omega Ruby/Alpha Sapphire) was not separately tested; only SAV6XY (X/Y) was
  exercised.

## Recommendation

**Library-generated saves should be re-verified against real game saves once available.**
A real 3DS save dump (X, Y, OR, or AS) run through `SaveUtil.GetSaveFile()` would confirm
both the format-detection path and give higher confidence that trainer/party/species/level
parsing holds on authentic data, not just on freshly-constructed in-memory objects.

## Files

- `verify/Gen6/Gen6Verify.csproj` — net10.0 console project, references
  `vendor/PKHeX.Core/PKHeX.Core.csproj`.
- `verify/Gen6/Program.cs` — verification harness (see recipe above).
