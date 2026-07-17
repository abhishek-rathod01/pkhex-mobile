# Gen 5 (Black/White/Black 2/White 2) Save Support — Verification Progress

## Status: VERIFIED against a REAL save file (2026-07-17)

Originally verified library-generated only (see below); real Pokémon Black
(`Pokemon Black Version.sav`, 524288 bytes, Trainer Noah, 6-member party) and White 2
(`Pokemon White 2 Version.sav`, Trainer Antonio, 6-member party) saves became available
and were confirmed both via console-level `SaveUtil.GetSaveFile` read (both sub-versions)
and through the actual app UI (file picker → party list → detail screen, Black save) on
the PkhexMobile_Emulator AVD.

- Party list (Black) showed all 6 members correctly (Snake/Serperior Lv.100, Cab
  Driver/Seismitoad, Astronaut/Golurk, Surfer/Simipour, Wild Wings/Trumbeak, Alvin/Boldore).
- Detail screen for Snake showed: Species Serperior, Nature Bashful, Ability Overgrow,
  Moves Leaf Blade/Coil/Leech Seed/Slam, IVs 4/30/15/23/22/17, EVs
  100/100/100/11/99/100 — all matching the console-level read exactly.

This closes the "should be re-verified against real game saves" note below — Gen5 is
now confirmed against genuine save files (both BW and B2W2 sub-versions), not just a
library-generated one.

## Test data used (original)
No real Gen 5 `.sav` file exists anywhere in this worktree (searched for `*.sav` / `*.dsv` across
the whole repo — none found). Verification therefore used a **library-generated blank save**,
constructed entirely through PKHeX.Core's own public API (`new SAV5BW()`). No synthetic/hand-written
save bytes were used anywhere in this process.

**Library-generated saves should be re-verified against real game saves once one becomes available.**

## What was read before writing any code
- `vendor/PKHeX.Core/Saves/SAV5.cs` — abstract base for all Gen5 saves (BW and B2W2). Confirms:
  - `protected SAV5([ConstantExpected] int size) : base(size)` — size-based blank constructor, calls
    `Initialize()` (sets `Box = 0x400`, `Party = 0x18E00`) then `ClearBoxes()`.
  - `PartyCount` getter/setter reads/writes `Data[Party + 4]` directly.
  - `BlankPKM => new PK5()`.
  - `OT`, `Version`, etc. are forwarded to `PlayerData` block properties.
- `vendor/PKHeX.Core/Saves/SAV5BW.cs` — concrete BW save:
  - `public SAV5BW() : base(SaveUtil.SIZE_G5RAW) => Blocks = new SaveBlockAccessor5BW(this);`
  - `SaveUtil.SIZE_G5RAW = 0x80000` (found in `vendor/PKHeX.Core/Saves/Util/SaveUtil.cs`).
  - Also confirmed sibling `SAV5B2W2.cs` has the identical parameterless-constructor pattern
    (`new(SaveUtil.SIZE_G5RAW)`), just wired to a different `SaveBlockAccessor5B2W2`.

This matches the proven Gen4 recipe pattern: a parameterless blank constructor exists and is safe to
call without hand-built bytes.

## Harness
- `verify/Gen5/Gen5Verify.csproj` — net10.0 console app, `ProjectReference` to
  `../../vendor/PKHeX.Core/PKHeX.Core.csproj`.
- `verify/Gen5/Program.cs` — exercises:
  ```csharp
  var sav = new SAV5BW();
  sav.OT = "VERIFY";
  sav.Version = GameVersion.W;
  var pk = sav.BlankPKM;
  pk.Species = 25; // Pikachu
  pk.CurrentLevel = 10;
  sav.PartyData = [pk]; // list-setter, NOT SetPartySlotAtIndex directly
  ```

Run via `dotnet run` from `verify/Gen5/`.

## PRIMARY verification result (read directly off the live SAV object) — PASS

```
Trainer: VERIFY
Version: W
Generation: 5
PartyCount: 1
  Slot[0]: Species=25 Level=10 IsPartyValid=True
PRIMARY VERIFICATION: PASS
```

Confirmed working:
- Trainer name (OT) read/write round-trip on the live object.
- Party count (`sav.PartyCount`) correctly reflects 1 after using the `PartyData` list-setter.
- Species (25 / Pikachu) and Level (10) correctly readable off the party slot.
- `sav.Version` correctly round-trips when explicitly set (defaults to `GameVersion.Any` if left
  unset, since the blank constructor does not populate the player-data "Game" byte — this is
  expected, not a bug, and matters if a caller assumes a default version).

## SECONDARY (bonus) check — Write() + SaveUtil.GetSaveFile round-trip — PASS

Unlike Gen4 (where this bonus round-trip crashed with an `ArgumentOutOfRangeException` in checksum
code and/or failed to re-detect the format), **Gen5's blank-constructed `SAV5BW` round-trips
cleanly**:

```
Write() succeeded, 524288 bytes.
Re-detected as: SAV5BW, OT=VERIFY, PartyCount=1
```

`sav.Write()` produced the full 0x80000-byte (524288 byte) buffer, and
`SaveUtil.GetSaveFile(bytes)` correctly re-detected it as `SAV5BW` with the correct OT and
PartyCount preserved. This works because `SAV5.SetChecksums()` (`AllBlocks.SetChecksums(Data)`)
and the associated block/footer-writing logic in `SaveBlockAccessor5BW` fully populate the
footer/checksum fields that `SaveUtil`'s format-sniffing (`IsG5BW`, which checks a magic value at
a footer offset — see `SaveUtil.cs` `IsValidFooter5(data, SIZE_G5BW, 0x8C)`) depends on, even
starting from a blank/zeroed buffer. This is a stronger result than what was achieved for Gen4.

## Gen5-specific notes / quirks
- Gen5 has two save-layout families with a shared abstract base (`SAV5`): `SAV5BW` (Black/White)
  and `SAV5B2W2` (Black 2/White 2), selected via different `SaveBlockAccessor5*` implementations
  and different extdata offsets (e.g. `ExtBattleVideoNativeOffset` etc. differ between the two
  subclasses). **Only `SAV5BW` was exercised in this verification; `SAV5B2W2` was not separately
  tested.** Given the identical constructor shape (`new(SaveUtil.SIZE_G5RAW)`) and shared base
  class, it is expected to behave the same way, but this has not been empirically confirmed.
- Both raw sizes (`SaveUtil.SIZE_G5RAW = 0x80000`) are identical between BW and B2W2 — the two are
  distinguished purely by a footer magic value PKHeX writes/reads (`IsG5BW` vs `IsG5B2W2` in
  `SaveUtil.cs`), not by file size.
- `PartyCount` is stored at `Data[Party + 4]` where `Party = 0x18E00` — a simple direct byte, no
  encoding quirks encountered.
- As with other generations, `PartyData` setter (not `SetPartySlotAtIndex` directly) must be used
  to get `PartyCount` reliably updated.

## Known limitations
- No real Gen5 save file was available to cross-check against; all results are from a
  library-generated blank save. **This should be re-verified against a real BW/B2W2 save file once
  one is available**, to confirm the blank-constructor code path matches real game-produced saves
  byte-for-byte in the areas that matter for parsing (trainer info, party, boxes).
- `SAV5B2W2` (Black 2/White 2) was not separately tested — only `SAV5BW` (Black/White). Per task
  instructions this was acceptable ("you only need to test one, BW is fine").
- Nothing beyond trainer name / version / party count / species / level was verified (e.g. box
  storage, items, Pokédex, mystery gift, C-Gear skin, musical, battle subway data, etc. were not
  exercised). This is out of scope for this pass (read-only save-parsing smoke test only).

## Summary
Gen 5 (BW) save construction, trainer/party read-write, and even the full Write()/re-detection
round-trip all work correctly via PKHeX.Core's public API with a library-generated blank save.
No blockers encountered.
