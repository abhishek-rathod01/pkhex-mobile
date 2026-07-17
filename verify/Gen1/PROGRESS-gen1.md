# Gen 1 (Red/Blue/Yellow) Save-Parsing Verification

## Status: VERIFIED against a REAL save file (2026-07-17)

Originally verified library-generated only (see below); a real Pokémon Red save
(`POKEMON RED-0.sav`, 32768 bytes, Trainer ASH, 6-member party) became available and
was confirmed both via console-level `SaveUtil.GetSaveFile` read and through the actual
app UI (file picker → party list → detail screen) on the PkhexMobile_Emulator AVD.

- Party list showed all 6 members correctly: MEW Lv.100, MEWTWO Lv.100, VENUSAUR Lv.100,
  BLASTOISE Lv.100, DRAGONITE Lv.100, CHARIZARD Lv.100.
- Detail screen for MEW showed: Species Mew, Nature Hardy, Ability — (correct for Gen1,
  which has no abilities), Moves Rock Slide/Ice Beam/Psychic/Tri Attack, IVs
  13/15/11/15/15/12, EVs 65535/65535/65535/65535/65535/65535 (Gen1 "EVs" are actually
  raw 0-65535 stat experience, which is what the abstract `EV_*` properties expose for
  this generation) — all matching the console-level read exactly.

This closes the "should be re-verified against a real game save" note below — Gen1 is
now confirmed against a genuine cartridge/emulator save, not just a library-generated
one.

## Status (original): VERIFIED (library-generated save)

## Test method

No real Gen 1 `.sav` file was available in the repo (searched for `*.sav` under the
worktree; none found), so verification used a **library-generated** blank save built
entirely through PKHeX.Core's own public API — no hand-written/synthetic save bytes
were used anywhere.

Harness: `verify/Gen1/Gen1Verify.csproj` (net10.0 console app, `ProjectReference` to
`vendor/PKHeX.Core/PKHeX.Core.csproj`) + `verify/Gen1/Program.cs`. Run via `dotnet run`
from `verify/Gen1/`.

Recipe used (confirmed against `vendor/PKHeX.Core/Saves/SAV1.cs` before writing code):

```csharp
var sav = new SAV1(); // LanguageID.English, GameVersion defaults to RBY
sav.OT = "VERIFY";
var pk = sav.BlankPKM; // PK1
pk.Species = 25;       // Pikachu, National Dex number
pk.CurrentLevel = 10;
sav.PartyData = [pk];  // list-setter; sets PartyCount correctly
```

`SAV1`'s blank constructor signature is:
`public SAV1(LanguageID language = LanguageID.English, GameVersion version = default)`,
which internally calls `base(SaveUtil.SIZE_G1RAW)` (0x8000 bytes), allocating a fully
sized, zeroed backing buffer — unlike some other generations' blank constructors, Gen1's
buffer is already the exact size the real save format expects.

## What was confirmed working

Primary verification (direct read off the live `SAV1`/`PK1` objects, no serialization):

- **Trainer name**: `sav.OT` round-trips correctly ("VERIFY").
- **Party count**: `sav.PartyCount` == 1 after `PartyData = [pk]`, == 2 after adding a
  second slot (Mew, species 151).
- **Species**: `PartyData[i].Species` reads back the correct **National Dex number**
  (25 for Pikachu, 151 for Mew) for every slot.
- **Level**: `PartyData[i].CurrentLevel` reads back correctly (10, then 5 for the second
  slot).

Console output (excerpt):

```
Trainer: VERIFY
PartyCount: 1
  Slot 0: Species=25 Level=10 Nickname=''
...
PartyCount: 2
  Slot 0: Species=25 Level=10
  Slot 1: Species=151 Level=5
SECOND SLOT VERIFICATION: PASS
```

### Gen1-specific quirk: internal species index vs. National Dex number

Gen 1's on-cartridge party/box list stores species using an internal index order that is
**not** the National Dex order (this internal order is unique to Gen 1/2 and differs
from every later generation's format). Confirmed that `PKHeX.Core` fully abstracts this:

- `PK1.Species` (public property) always gets/sets the **National Dex number** — verified
  Pikachu (25) round-trips as 25.
- `PK1.SpeciesInternal` (raw byte) exposes the actual Gen-1 internal index PKHeX writes
  into the party buffer — for Pikachu this was internal byte `84`, confirming it is a
  genuinely different value from the public `Species` (25), i.e. the abstraction is real
  and not a no-op.
- Cross-checked directly via the converter: `SpeciesConverter.GetInternal1(25) == 84`,
  `SpeciesConverter.GetNational1(84) == 25`. Round trips correctly.

No held items, abilities, or natures exist in Gen 1 (`PK1.HeldItem`, `CanHoldItem`,
etc. are hardcoded to 0/false), so these are correctly out of scope for Gen 1 — this
is expected/correct behavior, not a limitation.

## Optional bonus check: Write() + SaveUtil.GetSaveFile round-trip

Unlike the Gen4 case (which crashed with `ArgumentOutOfRangeException` in checksum
code), Gen 1's `sav.Write()` **succeeded** without throwing and produced exactly 32768
(0x8000) bytes — the correct raw Gen1 save size. However, the first attempt to
re-detect the format via `SaveUtil.GetSaveFile(written)` **returned null** (format not
recognized), when the save only had party data set (no box/PC content).

Root cause identified by reading the source (not guessed):
`SaveUtil`'s Gen1 detection (`SaveUtil.cs`, `IsG1INT`/`IsG1JPN`) validates that both the
Party list *and* the Current Box list have a valid `count` byte followed by an `0xFF`
terminator at the expected fixed offsets. `SAV1.GetFinalData()` only flushes box data
with a real terminator once `BoxesInitialized` is true; that flag is only set when
`AnyBoxSlotSpeciesPresent(...)` finds at least one occupied box slot. With only the
party populated, no box slot is ever occupied, so `PokeList1.MergeSingles(...,
isDestInitialized: false)` intentionally skips writing that box (per its own early-return
`if (count == 0 && (!isDestInitialized || ...)) return false;`), leaving the Current Box
region as zeroed memory — a `0x00` terminator instead of `0xFF`, which fails
`IsListValidG12` and thus fails Gen1 detection.

**Follow-up experiment (still using only public PKHeX.Core API, no hand-patched
bytes)**: adding one Pokémon to a PC box via the public `SetBoxSlotAtIndex(pk, box,
slot)` method before calling `Write()` made the round-trip **succeed**:

```
Re-detected as: SAV1, Generation=1
Reloaded Trainer: VERIFY, PartyCount: 2
Reloaded Box 1 Slot 0: Species=1 Level=5
```

So the Write()/re-detect round trip is **not fundamentally broken** for Gen 1 the way
it appeared to be for Gen 4 — it simply requires at least one PC box slot to be
populated (matching how a real save behaves once box storage has ever been touched in
game). This was an optional/bonus check per the task instructions, not required for the
primary verification, but is documented here since it was investigated and resolved.

## Known limitations

- All testing used a **library-generated** blank save (`new SAV1()`), not a real
  Red/Blue/Yellow cartridge/emulator save. No `.sav` files were found in this worktree
  to test against.
- Only `OT`, `TID16`, `PartyCount`, `Species`, `CurrentLevel`, and one PC box slot were
  exercised. Other Gen1 SAV1 fields (Pokédex seen/caught flags, badges, money, Hall of
  Fame, daycare, item bag, event flags/work, Yellow-specific Pikachu friendship/beach
  score) were read from source but not individually exercised in this harness.
- The Write()/GetSaveFile round-trip requires at least one PC box slot to be populated;
  a save with only party data (no box content) will not currently re-detect via
  `SaveUtil.GetSaveFile`. This is a real behavior of the library's detection logic, not
  a bug introduced by this harness — it stems from `BoxesInitialized` gating the box
  list terminator write in `SAV1.GetFinalData()`.
- **This library-generated save should be re-verified against one or more real
  Red/Blue/Yellow game saves once available**, to confirm PKHeX.Core parses genuine
  cartridge/emulator dumps identically to how it round-trips its own generated saves.

## Files

- `verify/Gen1/Gen1Verify.csproj` — net10.0 console harness project.
- `verify/Gen1/Program.cs` — verification code (primary + quirk checks + optional bonus
  round-trip + follow-up box-slot experiment).
- `PROGRESS-gen1.md` — this file.
